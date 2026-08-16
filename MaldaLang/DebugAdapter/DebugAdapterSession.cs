// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DebugAdapter;

using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using SysEnv = System.Environment;

/// <summary>
/// Hand-rolled DAP dispatcher bound to <see cref="DebugSession"/> + interpret-mode
/// <see cref="Interpreter"/>. Tests construct this in-process over pipes.
/// </summary>
public sealed class DebugAdapterSession
{
    private readonly DapTransport _transport;
    private readonly DebugSession _debugSession = new();
    private readonly object _stateLock = new();
    private int _nextSeq = 1;
    private int _nextBreakpointId = 1;
    private int _exitSent;
    private bool _shutdown;
    private bool _started;
    private LaunchConfig? _launch;
    private Task? _interpretTask;
    private CancellationTokenSource? _interpretCts;
    private string? _originalCwd;
    private readonly List<(string Key, string? Previous)> _envRestore = new();
    private TextWriter? _originalOut;
    private TextWriter? _originalError;

    private DebugAdapterSession(Stream input, Stream output)
    {
        _transport = new DapTransport(input, output);
        _debugSession.Paused += OnPaused;
        _debugSession.ConditionError += OnConditionError;
        _debugSession.Output += OnDebugOutput;
    }

    public static Task RunStdioAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), cancellationToken, redirectConsole: true);
    }

    public static async Task RunAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default,
        bool redirectConsole = true)
    {
        var session = new DebugAdapterSession(input, output);
        await session.ListenAsync(redirectConsole, cancellationToken).ConfigureAwait(false);
    }

    private async Task ListenAsync(bool redirectConsole, CancellationToken cancellationToken)
    {
        if (redirectConsole)
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            Console.SetOut(new DapOutputWriter(this, "stdout"));
            Console.SetError(new DapOutputWriter(this, "stderr"));
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested && !Volatile.Read(ref _shutdown))
            {
                string? json;
                try
                {
                    json = await _transport.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (json == null)
                    break;

                DapIncoming incoming;
                try
                {
                    incoming = DapProtocol.Parse(json);
                }
                catch (Exception ex)
                {
                    await SendOutputAsync("stderr", "Invalid DAP JSON: " + ex.Message + SysEnv.NewLine)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(incoming.Type, "request", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    await DispatchAsync(incoming).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await SendResponseAsync(incoming, success: false, message: UnwrapMessage(ex))
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await ShutdownInterpretAsync().ConfigureAwait(false);
            if (redirectConsole)
                RestoreConsole();
            RestoreProcessState();
            _transport.Dispose();
        }
    }

    private async Task DispatchAsync(DapIncoming request)
    {
        switch (request.Command)
        {
            case "initialize":
                await HandleInitializeAsync(request).ConfigureAwait(false);
                break;
            case "launch":
                await HandleLaunchAsync(request).ConfigureAwait(false);
                break;
            case "setBreakpoints":
                await HandleSetBreakpointsAsync(request).ConfigureAwait(false);
                break;
            case "configurationDone":
                await HandleConfigurationDoneAsync(request).ConfigureAwait(false);
                break;
            case "threads":
                await SendResponseAsync(request, success: true, body: new
                {
                    threads = new[] { new DapThread { Id = 1, Name = "main" } }
                }).ConfigureAwait(false);
                break;
            case "stackTrace":
                await HandleStackTraceAsync(request).ConfigureAwait(false);
                break;
            case "scopes":
                await HandleScopesAsync(request).ConfigureAwait(false);
                break;
            case "variables":
                await HandleVariablesAsync(request).ConfigureAwait(false);
                break;
            case "continue":
                _debugSession.Continue();
                await SendResponseAsync(request, success: true, body: new DapContinueBody { AllThreadsContinued = true })
                    .ConfigureAwait(false);
                break;
            case "next":
                _debugSession.StepOver();
                await SendResponseAsync(request, success: true).ConfigureAwait(false);
                break;
            case "stepIn":
                _debugSession.StepInto();
                await SendResponseAsync(request, success: true).ConfigureAwait(false);
                break;
            case "stepOut":
                _debugSession.StepOut();
                await SendResponseAsync(request, success: true).ConfigureAwait(false);
                break;
            case "pause":
                _debugSession.SetDebugMode(DebugMode.Paused);
                await SendResponseAsync(request, success: true).ConfigureAwait(false);
                break;
            case "evaluate":
                await HandleEvaluateAsync(request).ConfigureAwait(false);
                break;
            case "disconnect":
            case "terminate":
                await SendResponseAsync(request, success: true).ConfigureAwait(false);
                await ShutdownInterpretAsync().ConfigureAwait(false);
                Volatile.Write(ref _shutdown, true);
                break;
            default:
                await SendResponseAsync(request, success: false, message: "Unsupported DAP request: " + request.Command)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleInitializeAsync(DapIncoming request)
    {
        var capabilities = new DapCapabilities
        {
            SupportsConfigurationDoneRequest = true,
            SupportsConditionalBreakpoints = true,
            SupportsEvaluateForHovers = true,
            SupportsSetVariable = false
        };
        await SendResponseAsync(request, success: true, body: capabilities).ConfigureAwait(false);
        await SendEventAsync("initialized", new { }).ConfigureAwait(false);
    }

    private async Task HandleLaunchAsync(DapIncoming request)
    {
        var args = request.Arguments;
        var program = DapProtocol.ReadString(args, "program");
        if (string.IsNullOrWhiteSpace(program))
        {
            await SendResponseAsync(request, success: false, message: "launch requires arguments.program (.malda path).")
                .ConfigureAwait(false);
            return;
        }

        program = StripFileUri(program);
        var cwd = DapProtocol.ReadString(args, "cwd");
        if (!string.IsNullOrWhiteSpace(cwd))
            ApplyCwd(StripFileUri(cwd));

        var fullPath = Path.GetFullPath(program);
        var ext = Path.GetExtension(fullPath);
        if (!string.Equals(ext, ".malda", StringComparison.OrdinalIgnoreCase)
            && !fullPath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase))
        {
            await SendResponseAsync(request, success: false, message: "program must be a .malda file.")
                .ConfigureAwait(false);
            return;
        }

        if (!File.Exists(fullPath))
        {
            await SendResponseAsync(request, success: false, message: "program file not found: " + fullPath)
                .ConfigureAwait(false);
            return;
        }

        ApplyEnv(args);
        _launch = new LaunchConfig
        {
            Program = fullPath,
            StopOnEntry = DapProtocol.ReadBoolean(args, "stopOnEntry")
        };
        _debugSession.MainFile = fullPath;
        _debugSession.StopOnEntry = _launch.StopOnEntry;
        await SendResponseAsync(request, success: true).ConfigureAwait(false);
    }

    private async Task HandleSetBreakpointsAsync(DapIncoming request)
    {
        var args = request.Arguments;
        string? path = null;
        if (DapProtocol.TryGetProperty(args, "source", out var source) && source.ValueKind == JsonValueKind.Object)
            path = DapProtocol.ReadString(source, "path");

        if (string.IsNullOrWhiteSpace(path))
        {
            await SendResponseAsync(request, success: false, message: "setBreakpoints requires source.path.")
                .ConfigureAwait(false);
            return;
        }

        path = Path.GetFullPath(StripFileUri(path));
        _debugSession.ClearBreakpoints(path);

        var requested = ReadRequestedBreakpoints(args);
        var stoppable = LoadStoppableLines(path);
        var results = new List<DapBreakpoint>();
        foreach (var item in requested)
        {
            var mapped = DebugStoppableLines.MapToStoppable(stoppable, item.Line);
            if (mapped is int actual)
            {
                _debugSession.SetBreakpoint(path, actual, item.Condition);
                results.Add(new DapBreakpoint
                {
                    Id = Interlocked.Increment(ref _nextBreakpointId),
                    Verified = true,
                    Line = actual
                });
            }
            else
            {
                results.Add(new DapBreakpoint
                {
                    Id = Interlocked.Increment(ref _nextBreakpointId),
                    Verified = false,
                    Line = item.Line
                });
            }
        }

        await SendResponseAsync(request, success: true, body: new { breakpoints = results }).ConfigureAwait(false);
    }

    private async Task HandleConfigurationDoneAsync(DapIncoming request)
    {
        LaunchConfig? launch;
        var alreadyStarted = false;
        lock (_stateLock)
        {
            launch = _launch;
            alreadyStarted = _started;
            if (launch != null && !_started)
                _started = true;
        }

        if (launch == null)
        {
            await SendResponseAsync(request, success: false, message: "configurationDone requires launch first.")
                .ConfigureAwait(false);
            return;
        }

        await SendResponseAsync(request, success: true).ConfigureAwait(false);
        if (!alreadyStarted)
            StartInterpret(launch);
    }

    private async Task HandleStackTraceAsync(DapIncoming request)
    {
        var args = request.Arguments;
        var startFrame = DapProtocol.ReadInt32(args, "startFrame");
        var levels = DapProtocol.ReadInt32(args, "levels");
        var frames = _debugSession.GetStackFrames();
        var result = new List<DapStackFrame>();
        for (var i = 0; i < frames.Count; i++)
        {
            if (i < startFrame)
                continue;
            if (levels > 0 && result.Count >= levels)
                break;
            result.Add(ToDapFrame(i + 1, frames[i]));
        }

        await SendResponseAsync(request, success: true, body: new
        {
            stackFrames = result,
            totalFrames = frames.Count
        }).ConfigureAwait(false);
    }

    private async Task HandleScopesAsync(DapIncoming request)
    {
        var frameId = DapProtocol.ReadInt32(request.Arguments, "frameId", 1);
        var scopes = _debugSession.GetFrameScopes(frameId);
        var result = scopes.Select(s => new DapScope
        {
            Name = s.Name,
            VariablesReference = s.VariablesReference,
            Expensive = false
        }).ToList();
        await SendResponseAsync(request, success: true, body: new { scopes = result }).ConfigureAwait(false);
    }

    private async Task HandleVariablesAsync(DapIncoming request)
    {
        var reference = DapProtocol.ReadInt32(request.Arguments, "variablesReference");
        var variables = _debugSession.GetVariables(reference);
        var result = variables.Select(v => new DapVariable
        {
            Name = v.Name,
            Value = v.Value,
            Type = v.Type,
            VariablesReference = v.VariablesReference
        }).ToList();
        await SendResponseAsync(request, success: true, body: new { variables = result }).ConfigureAwait(false);
    }

    private async Task HandleEvaluateAsync(DapIncoming request)
    {
        var expression = DapProtocol.ReadString(request.Arguments, "expression") ?? "";
        var frameId = DapProtocol.ReadInt32(request.Arguments, "frameId", 1);
        if (frameId < 1)
            frameId = 1;

        try
        {
            var value = await _debugSession.EvaluateWatchAsync(expression, frameId).ConfigureAwait(false);
            await SendResponseAsync(request, success: true, body: new DapEvaluateBody
            {
                Result = value.Value,
                Type = value.Type,
                VariablesReference = value.VariablesReference
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendResponseAsync(request, success: false, message: UnwrapMessage(ex)).ConfigureAwait(false);
        }
    }

    private void StartInterpret(LaunchConfig launch)
    {
        List<Statement> statements;
        try
        {
            var source = File.ReadAllText(launch.Program);
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, launch.Program);
            statements = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                foreach (var error in parser.Errors)
                    SendOutput("stderr", error.Message + SysEnv.NewLine);
                _ = SendExitedTerminatedAsync(1);
                return;
            }
        }
        catch (Exception ex)
        {
            SendOutput("stderr", UnwrapMessage(ex) + SysEnv.NewLine);
            _ = SendExitedTerminatedAsync(1);
            return;
        }

        _debugSession.MainFile = launch.Program;
        _debugSession.StopOnEntry = launch.StopOnEntry;
        var interpreter = new Interpreter(_debugSession, launch.Program);
        interpreter.SetOutputCallback(text =>
        {
            if (string.IsNullOrEmpty(text))
                return;
            SendOutput("stdout", text.EndsWith('\n') ? text : text + SysEnv.NewLine);
        });

        _interpretCts = new CancellationTokenSource();
        var token = _interpretCts.Token;
        _interpretTask = Task.Run(async () =>
        {
            try
            {
                await interpreter.InterpretAsync(statements, token).ConfigureAwait(false);
                await SendExitedTerminatedAsync(0).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await SendExitedTerminatedAsync(0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendOutputAsync("stderr", UnwrapMessage(ex) + SysEnv.NewLine).ConfigureAwait(false);
                await SendExitedTerminatedAsync(1).ConfigureAwait(false);
            }
        }, CancellationToken.None);
    }

    private async Task ShutdownInterpretAsync()
    {
        _debugSession.Stop();
        try
        {
            _interpretCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var task = _interpretTask;
        if (task != null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await SendExitedTerminatedAsync(0).ConfigureAwait(false);
    }

    private void OnPaused(int line, string? file)
    {
        var reason = string.IsNullOrEmpty(_debugSession.LastStopReason)
            ? "step"
            : _debugSession.LastStopReason;
        _ = SendEventAsync("stopped", new DapStoppedBody
        {
            Reason = reason,
            ThreadId = 1,
            AllThreadsStopped = true,
            Text = _debugSession.LastStopText,
            Description = _debugSession.LastStopText
        });
    }

    private void OnConditionError(string message)
    {
        SendOutput("stderr", message + SysEnv.NewLine);
    }

    private void OnDebugOutput(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        SendOutput("console", message.EndsWith('\n') ? message : message + SysEnv.NewLine);
    }

    private void SendOutput(string category, string text)
    {
        SendOutputAsync(category, text).GetAwaiter().GetResult();
    }

    private Task SendOutputAsync(string category, string text)
    {
        return SendEventAsync("output", new DapOutputBody
        {
            Category = category,
            Output = text
        });
    }

    private async Task SendExitedTerminatedAsync(int exitCode)
    {
        if (Interlocked.Exchange(ref _exitSent, 1) != 0)
            return;
        await SendEventAsync("exited", new DapExitedBody { ExitCode = exitCode }).ConfigureAwait(false);
        await SendEventAsync("terminated", new { }).ConfigureAwait(false);
    }

    private Task SendResponseAsync(DapIncoming request, bool success, object? body = null, string? message = null)
    {
        var seq = Interlocked.Increment(ref _nextSeq);
        var json = DapProtocol.FormatResponse(seq, request.Seq, request.Command, success, body, message);
        return WriteAsync(json);
    }

    private Task SendEventAsync(string eventName, object? body)
    {
        var seq = Interlocked.Increment(ref _nextSeq);
        var json = DapProtocol.FormatEvent(seq, eventName, body);
        return WriteAsync(json);
    }

    private async Task WriteAsync(string json)
    {
        try
        {
            await _transport.WriteMessageAsync(json).ConfigureAwait(false);
        }
        catch (IOException)
        {
            Volatile.Write(ref _shutdown, true);
        }
        catch (ObjectDisposedException)
        {
            Volatile.Write(ref _shutdown, true);
        }
    }

    private static DapStackFrame ToDapFrame(int id, InterpreterCallStackFrame frame)
    {
        var name = frame.FunctionName;
        if (!string.IsNullOrEmpty(frame.ClassName))
            name = frame.ClassName + "." + frame.FunctionName;
        if (string.IsNullOrEmpty(name))
            name = "<script>";

        var path = frame.File ?? "";
        return new DapStackFrame
        {
            Id = id,
            Name = name,
            Line = frame.Line,
            Column = 1,
            Source = new DapSource
            {
                Path = path,
                Name = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path)
            }
        };
    }

    private static SortedSet<int> LoadStoppableLines(string path)
    {
        if (!File.Exists(path))
            return new SortedSet<int>();

        try
        {
            var source = File.ReadAllText(path);
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, path);
            var statements = parser.Parse();
            return DebugStoppableLines.Collect(statements, path);
        }
        catch
        {
            return new SortedSet<int>();
        }
    }

    private static List<(int Line, string? Condition)> ReadRequestedBreakpoints(JsonElement args)
    {
        var result = new List<(int Line, string? Condition)>();
        if (DapProtocol.TryGetProperty(args, "breakpoints", out var breakpoints)
            && breakpoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var bp in breakpoints.EnumerateArray())
            {
                var line = DapProtocol.ReadInt32(bp, "line");
                if (line > 0)
                    result.Add((line, DapProtocol.ReadString(bp, "condition")));
            }

            return result;
        }

        if (DapProtocol.TryGetProperty(args, "lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var lineEl in lines.EnumerateArray())
            {
                if (lineEl.ValueKind == JsonValueKind.Number && lineEl.TryGetInt32(out var line) && line > 0)
                    result.Add((line, null));
            }
        }

        return result;
    }

    private void ApplyCwd(string cwd)
    {
        _originalCwd ??= Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetFullPath(cwd));
    }

    private void ApplyEnv(JsonElement args)
    {
        if (!DapProtocol.TryGetProperty(args, "env", out var env) || env.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in env.EnumerateObject())
        {
            _envRestore.Add((prop.Name, SysEnv.GetEnvironmentVariable(prop.Name)));
            var value = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.ToString();
            SysEnv.SetEnvironmentVariable(prop.Name, value);
        }
    }

    private void RestoreProcessState()
    {
        if (_originalCwd != null)
        {
            try
            {
                Directory.SetCurrentDirectory(_originalCwd);
            }
            catch
            {
            }

            _originalCwd = null;
        }

        foreach (var (key, previous) in _envRestore)
        {
            try
            {
                SysEnv.SetEnvironmentVariable(key, previous);
            }
            catch
            {
            }
        }

        _envRestore.Clear();
    }

    private void RestoreConsole()
    {
        try
        {
            if (_originalOut != null)
                Console.SetOut(_originalOut);
            if (_originalError != null)
                Console.SetError(_originalError);
        }
        catch
        {
        }
    }

    private static string StripFileUri(string path)
    {
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new Uri(path).LocalPath;
            }
            catch
            {
            }
        }

        return path;
    }

    private static string UnwrapMessage(Exception ex)
    {
        var current = ex;
        while (current is AggregateException agg && agg.InnerException != null)
            current = agg.InnerException;
        return current.Message;
    }

    private sealed class LaunchConfig
    {
        public string Program { get; init; } = "";
        public bool StopOnEntry { get; init; }
    }

    private sealed class DapOutputWriter : TextWriter
    {
        private readonly DebugAdapterSession _session;
        private readonly string _category;
        private readonly StringBuilder _buffer = new();
        private readonly object _lock = new();

        public DapOutputWriter(DebugAdapterSession session, string category)
        {
            _session = session;
            _category = category;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\r')
                return;
            lock (_lock)
            {
                _buffer.Append(value);
                if (value == '\n')
                    FlushBuffer();
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            foreach (var ch in value)
                Write(ch);
        }

        public override void Flush()
        {
            lock (_lock)
                FlushBuffer();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Flush();
            base.Dispose(disposing);
        }

        private void FlushBuffer()
        {
            if (_buffer.Length == 0)
                return;
            var text = _buffer.ToString();
            _buffer.Clear();
            _session.SendOutput(_category, text);
        }
    }
}
