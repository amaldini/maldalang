// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Expressions;

/// <summary>
/// Shared interpret-mode debug core. Breakpoints, step mode, and the pause gate
/// live here so Desktop, Web, and tests share one implementation.
/// All line numbers on this type are 1-based.
/// </summary>
public sealed class DebugSession : IDebuggerHook
{
    private readonly object _gate = new();
    private readonly object _bpLock = new();
    private readonly Dictionary<string, Dictionary<int, string?>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    private DebugMode _mode = DebugMode.Continue;
    private int _currentDepth;
    private int _stepOverDepth;
    private int _stepOutDepth;
    private bool _pauseOnNextStatement;
    private bool _stopOnEntry;
    private TaskCompletionSource? _resumeTcs;
    private CancellationTokenSource _stopCts = new();
    private string? _mainFile;
    private Interpreter? _interpreter;
    private readonly object _inspectLock = new();
    private readonly Dictionary<int, InspectHandle> _inspectHandles = new();
    private int _nextVariablesReference = 1;

    private static readonly HashSet<string> HiddenGlobalNames = new(StringComparer.Ordinal)
    {
        StdLibNamespaces.MathModule,
        StdLibNamespaces.StrModule,
        StdLibNamespaces.IoModule,
        StdLibNamespaces.PdfModule,
        StdLibNamespaces.DocModule,
        StdLibNamespaces.ResultModule,
        StdLibNamespaces.OptionModule,
        StdLibNamespaces.GroundedModule,
        StdLibNamespaces.CapModule,
        StdLibNamespaces.DeprecatedMathModuleAlias,
        "ui",
        "AnsiConsole",
        "VectorDB",
        "GraphMemory"
    };

    private static readonly TokenType[] AssignmentTokenTypes =
    {
        TokenType.Assign,
        TokenType.PlusAssign,
        TokenType.MinusAssign,
        TokenType.MultiplyAssign,
        TokenType.DivideAssign
    };

    /// <summary>Raised from <see cref="OnPause"/> with a 1-based line.</summary>
    public event Action<int, string?>? Paused;

    /// <summary>
    /// DAP-style output (condition errors, later program stdout). Message includes
    /// the <c>breakpoint condition error:</c> prefix when a condition fails to eval.
    /// </summary>
    public event Action<string>? Output;

    /// <summary>Raised when a breakpoint condition throws; the session still breaks.</summary>
    public event Action<string>? ConditionError;

    /// <summary>
    /// When false (default), Globals omits stdlib namespaces (<c>math</c>/<c>str</c>/<c>io</c>
    /// and the rest of <see cref="StdLibNamespaces"/>) plus <c>ui</c>, <c>AnsiConsole</c>,
    /// <c>VectorDB</c>, and <c>GraphMemory</c>. Maps to DAP <c>malda.debug.showBuiltins</c> later.
    /// </summary>
    public bool ShowBuiltins { get; set; }

    /// <summary>Bind the live interpreter so inspect/watches/conditions can read paused state.</summary>
    public void Bind(Interpreter interpreter)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
    }

    public int CurrentLine { get; private set; }
    public string? CurrentFile { get; private set; }

    /// <summary>
    /// DAP <c>stopped.reason</c> for the last pause: <c>entry</c>, <c>breakpoint</c>,
    /// <c>step</c>, <c>pause</c>, or <c>exception</c>.
    /// v1 pauses only on uncaught interpret exceptions
    /// (<see cref="RuntimeException"/> / <see cref="MALDAException"/>).
    /// MALDA <c>try</c>/<c>catch</c> still swallows without a debug pause.
    /// <c>setExceptionBreakpoints</c> is v1.1.
    /// </summary>
    public string LastStopReason { get; private set; } = "step";

    /// <summary>
    /// DAP <c>stopped.text</c> / <c>description</c> for the last pause
    /// (exception message when <see cref="LastStopReason"/> is <c>exception</c>).
    /// </summary>
    public string? LastStopText { get; private set; }

    /// <summary>Exception message from the last uncaught interpret stop, if any.</summary>
    public string? ExceptionMessage => LastStopReason == "exception" ? LastStopText : null;

    /// <summary>
    /// File used when one side of a breakpoint compare is null/empty.
    /// Defaults to the first <see cref="OnStatement"/> file or <c>main.malda</c>.
    /// </summary>
    public string? MainFile
    {
        get => _mainFile;
        set => _mainFile = value;
    }

    public bool StopOnEntry
    {
        get => _stopOnEntry;
        set => _stopOnEntry = value;
    }

    public void SetBreakpoints(string file, IReadOnlyList<int> lines)
    {
        var key = NormalizeFile(file);
        lock (_bpLock)
        {
            var map = new Dictionary<int, string?>();
            foreach (var line in lines)
            {
                if (line > 0)
                    map[line] = null;
            }
            _breakpoints[key] = map;
        }
    }

    public void SetBreakpoint(string file, int line, string? condition = null)
    {
        if (line < 1)
            return;

        var key = NormalizeFile(file);
        lock (_bpLock)
        {
            if (!_breakpoints.TryGetValue(key, out var map))
            {
                map = new Dictionary<int, string?>();
                _breakpoints[key] = map;
            }
            map[line] = condition;
        }
    }

    public void ClearBreakpoints(string? file = null)
    {
        lock (_bpLock)
        {
            if (file == null)
            {
                _breakpoints.Clear();
                return;
            }

            _breakpoints.Remove(NormalizeFile(file));
        }
    }

    public bool HasBreakpoint(int line, string? file = null)
    {
        lock (_bpLock)
        {
            foreach (var kvp in _breakpoints)
            {
                if (FilesMatch(kvp.Key, file) && kvp.Value.ContainsKey(line))
                    return true;
            }
            return false;
        }
    }

    public bool CheckBreakpointCondition(int line, string? file, Func<bool> evaluator)
    {
        string? condition = null;
        var found = false;
        lock (_bpLock)
        {
            foreach (var kvp in _breakpoints)
            {
                if (FilesMatch(kvp.Key, file) && kvp.Value.TryGetValue(line, out condition))
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found)
            return true;

        if (string.IsNullOrEmpty(condition))
            return true;

        try
        {
            return evaluator();
        }
        catch
        {
            return true;
        }
    }

    public bool OnStatement(int line, string? file = null)
    {
        EnsureFreshRunToken();

        if (_mainFile == null && !string.IsNullOrEmpty(file))
            _mainFile = file;

        CurrentLine = line;
        CurrentFile = file;

        var mode = GetDebugMode();
        var stopOnEntry = _stopOnEntry;
        if (mode == DebugMode.Continue && !stopOnEntry && !_pauseOnNextStatement && !HasBreakpoint(line, file))
            return true;

        if (mode == DebugMode.Paused)
        {
            SetStopReason("pause");
            EnterPause(line, file);
            return false;
        }

        if (stopOnEntry)
        {
            _stopOnEntry = false;
            SetStopReason("entry");
            EnterPause(line, file);
            return false;
        }

        if (HasBreakpoint(line, file))
        {
            if (!ShouldBreakOnBreakpoint(line, file))
                return true;

            SetStopReason("breakpoint");
            EnterPause(line, file);
            return false;
        }

        if (_pauseOnNextStatement)
        {
            _pauseOnNextStatement = false;
            SetStopReason("step");
            EnterPause(line, file);
            return false;
        }

        if (mode == DebugMode.StepOver && _currentDepth <= _stepOverDepth)
        {
            SetStopReason("step");
            EnterPause(line, file);
            return false;
        }

        if (mode == DebugMode.StepInto)
        {
            SetStopReason("step");
            EnterPause(line, file);
            return false;
        }

        return true;
    }

    public void OnPause(int line, string? file = null)
    {
        CurrentLine = line;
        CurrentFile = file;
        Paused?.Invoke(line, file);
    }

    /// <summary>
    /// Pause for an uncaught interpret exception. Caller must then
    /// <see cref="OnPause"/> and <see cref="WaitIfPausedAsync"/>, then rethrow.
    /// </summary>
    public void PauseForUncaughtException(string message, int line, string? file)
    {
        SetStopReason("exception", message);
        EnterPause(line, file);
    }

    /// <summary>Raises <see cref="Output"/> (await-prompt wait, condition errors, …).</summary>
    public void EmitOutput(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Output?.Invoke(message);
    }

    public void OnFunctionEnter(string functionName, string? className, int line)
    {
        _currentDepth++;
    }

    public void OnFunctionExit(string functionName)
    {
        _currentDepth--;
        if (_currentDepth < 0)
            _currentDepth = 0;

        if (GetDebugMode() == DebugMode.StepOut && _currentDepth < _stepOutDepth)
            _pauseOnNextStatement = true;
    }

    public DebugMode GetDebugMode()
    {
        lock (_gate)
        {
            return _mode;
        }
    }

    public void SetDebugMode(DebugMode mode)
    {
        switch (mode)
        {
            case DebugMode.Continue:
                Continue();
                break;
            case DebugMode.StepOver:
                StepOver();
                break;
            case DebugMode.StepInto:
                StepInto();
                break;
            case DebugMode.StepOut:
                StepOut();
                break;
            case DebugMode.Paused:
                lock (_gate)
                {
                    _mode = DebugMode.Paused;
                }
                break;
        }
    }

    public void Continue() => ReleaseGate(DebugMode.Continue);

    public void StepOver()
    {
        _stepOverDepth = _currentDepth;
        ReleaseGate(DebugMode.StepOver);
    }

    public void StepInto() => ReleaseGate(DebugMode.StepInto);

    public void StepOut()
    {
        _stepOutDepth = _currentDepth;
        ReleaseGate(DebugMode.StepOut);
    }

    public void Stop()
    {
        lock (_gate)
        {
            try
            {
                _stopCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _resumeTcs?.TrySetCanceled(_stopCts.Token);
            _resumeTcs = null;
        }
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (_stopCts.IsCancellationRequested)
            throw new OperationCanceledException(_stopCts.Token);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource? tcs;
        lock (_gate)
        {
            if (_stopCts.IsCancellationRequested)
                throw new OperationCanceledException(_stopCts.Token);
            if (_mode != DebugMode.Paused)
                return;
            tcs = _resumeTcs;
            if (tcs == null)
                return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        if (linked.Token.IsCancellationRequested)
            throw new OperationCanceledException(linked.Token);

        using (linked.Token.Register(() => tcs.TrySetCanceled(linked.Token)))
        {
            await tcs.Task.ConfigureAwait(false);
        }
    }

    internal static string NormalizeFile(string? file)
    {
        if (string.IsNullOrEmpty(file))
            return string.Empty;

        if (!IsFilesystemPath(file))
            return file;

        try
        {
            return Path.GetFullPath(file);
        }
        catch
        {
            return file;
        }
    }

    /// <summary>
    /// Call stack with DAP frame ids: index 0 is frame id 1 (current / top),
    /// increasing toward the <c>&lt;script&gt;</c> frame.
    /// </summary>
    public IReadOnlyList<InterpreterCallStackFrame> GetStackFrames()
    {
        if (_interpreter == null)
            return Array.Empty<InterpreterCallStackFrame>();

        var stack = _interpreter.GetCallStack();
        stack.Reverse();
        return stack;
    }

    public IReadOnlyList<DebugScope> GetFrameScopes(int frameId)
    {
        ResetInspectHandles();
        if (_interpreter == null)
            return Array.Empty<DebugScope>();

        if (!TryGetFrame(frameId, out _, out var env, out var thisObject))
            return Array.Empty<DebugScope>();

        var globals = _interpreter.GlobalsEnvironment;
        var scopes = new List<DebugScope>();

        var localsEnv = env ?? globals;
        scopes.Add(new DebugScope
        {
            Name = "Locals",
            VariablesReference = AllocHandle(InspectHandle.ForLocals(localsEnv))
        });

        for (var enclosing = localsEnv.GetEnclosing();
             enclosing != null && !ReferenceEquals(enclosing, globals);
             enclosing = enclosing.GetEnclosing())
        {
            var own = enclosing.GetOwnVariables();
            if (own.Count == 0 || (own.Count == 1 && own.ContainsKey("this")))
                continue;

            scopes.Add(new DebugScope
            {
                Name = "Closure",
                VariablesReference = AllocHandle(InspectHandle.ForClosure(enclosing))
            });
        }

        scopes.Add(new DebugScope
        {
            Name = "Globals",
            VariablesReference = AllocHandle(InspectHandle.ForGlobals(globals))
        });

        if (thisObject != null)
        {
            scopes.Add(new DebugScope
            {
                Name = "This",
                VariablesReference = AllocHandle(InspectHandle.ForThis(thisObject))
            });
        }

        return scopes;
    }

    public IReadOnlyList<DebugVariable> GetVariables(int variablesReference)
    {
        if (variablesReference <= 0)
            return Array.Empty<DebugVariable>();

        InspectHandle? handle;
        lock (_inspectLock)
        {
            if (!_inspectHandles.TryGetValue(variablesReference, out handle))
                return Array.Empty<DebugVariable>();
        }

        return ExpandHandle(handle);
    }

    /// <summary>
    /// Parse and evaluate <paramref name="expression"/> in the selected frame.
    /// Side-effecting watches are allowed (a watch that calls a function runs it).
    /// Assignments are rejected.
    /// </summary>
    public async Task<DebugVariable> EvaluateWatchAsync(string expression, int frameId = 1)
    {
        var value = await EvaluateWatchValueAsync(expression, frameId).ConfigureAwait(false);
        return CreateVariable(expression, value);
    }

    private async Task<RuntimeValue> EvaluateWatchValueAsync(string expression, int frameId)
    {
        if (_interpreter == null)
            throw new RuntimeException("Debug session is not bound to an interpreter.");
        if (string.IsNullOrWhiteSpace(expression))
            throw new RuntimeException("Watch expression is empty.");

        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        if (tokens.Any(t => AssignmentTokenTypes.Contains(t.Type)))
            throw new RuntimeException("watch assignments are not supported");

        Expression expr;
        try
        {
            expr = Parser.ParseExpression(tokens);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Cannot parse watch expression: {ex.Message}");
        }

        if (!TryGetFrame(frameId, out _, out var env, out var thisObject))
            throw new RuntimeException($"Invalid frame id {frameId}.");

        return await _interpreter.EvaluateInEnvironmentAsync(expr, env ?? _interpreter.GlobalsEnvironment, thisObject)
            .ConfigureAwait(false);
    }

    private bool ShouldBreakOnBreakpoint(int line, string? file)
    {
        string? condition = null;
        var found = false;
        lock (_bpLock)
        {
            foreach (var kvp in _breakpoints)
            {
                if (FilesMatch(kvp.Key, file) && kvp.Value.TryGetValue(line, out condition))
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found || string.IsNullOrEmpty(condition))
            return true;

        if (_interpreter == null)
            return true;

        try
        {
            var value = EvaluateWatchValueAsync(condition, frameId: 1).GetAwaiter().GetResult();
            return value.IsTruthy();
        }
        catch (Exception ex)
        {
            var message = "breakpoint condition error: " + UnwrapExceptionMessage(ex);
            ConditionError?.Invoke(message);
            Output?.Invoke(message);
            return true;
        }
    }

    private bool TryGetFrame(int frameId, out InterpreterCallStackFrame? frame, out Environment? env, out ObjectInstance? thisObject)
    {
        frame = null;
        env = null;
        thisObject = null;
        if (_interpreter == null || frameId < 1)
            return false;

        var stack = _interpreter.GetCallStack();
        if (frameId > stack.Count)
            return false;

        // stack is bottom-to-top; frame id 1 is the last (current) frame.
        frame = stack[stack.Count - frameId];
        if (frameId == 1)
        {
            env = _interpreter.CurrentEnvironment;
            thisObject = frame.ThisObject ?? _interpreter.CurrentThisObject;
        }
        else
        {
            env = frame.Environment ?? _interpreter.GlobalsEnvironment;
            thisObject = frame.ThisObject;
        }

        return true;
    }

    private void ResetInspectHandles()
    {
        lock (_inspectLock)
        {
            _inspectHandles.Clear();
            _nextVariablesReference = 1;
        }
    }

    private int AllocHandle(InspectHandle handle)
    {
        lock (_inspectLock)
        {
            var id = _nextVariablesReference++;
            _inspectHandles[id] = handle;
            return id;
        }
    }

    private List<DebugVariable> ExpandHandle(InspectHandle handle)
    {
        switch (handle.Kind)
        {
            case InspectKind.Locals:
            {
                var hideStdlib = !ShowBuiltins
                    && _interpreter != null
                    && ReferenceEquals(handle.Env, _interpreter.GlobalsEnvironment);
                return VariablesFromEnvironment(handle.Env!, hideBuiltins: hideStdlib);
            }
            case InspectKind.Closure:
                return VariablesFromEnvironment(handle.Env!, hideBuiltins: false);
            case InspectKind.Globals:
                return VariablesFromEnvironment(handle.Env!, hideBuiltins: !ShowBuiltins);
            case InspectKind.This:
                return VariablesFromObject(handle.Object!);
            case InspectKind.Value:
                return VariablesFromValue(handle.Value);
            default:
                return new List<DebugVariable>();
        }
    }

    private List<DebugVariable> VariablesFromEnvironment(Environment env, bool hideBuiltins)
    {
        var result = new List<DebugVariable>();
        foreach (var kvp in env.GetOwnVariables())
        {
            if (kvp.Key == "this")
                continue;
            if (hideBuiltins && ShouldHideGlobal(kvp.Key, kvp.Value))
                continue;
            result.Add(CreateVariable(kvp.Key, kvp.Value));
        }

        return result;
    }

    private List<DebugVariable> VariablesFromObject(ObjectInstance obj)
    {
        var boxed = RuntimeValue.Object(obj);
        return VariablesFromValue(boxed);
    }

    private List<DebugVariable> VariablesFromValue(RuntimeValue value)
    {
        var result = new List<DebugVariable>();
        foreach (var child in DebugValueFormatter.GetChildren(value))
            result.Add(CreateVariable(child.Name, child.Value));
        return result;
    }

    private DebugVariable CreateVariable(string name, RuntimeValue value)
    {
        var preview = DebugValueFormatter.FormatPreview(value);
        var type = DebugValueFormatter.FormatType(value);
        var reference = 0;
        if (DebugValueFormatter.HasChildren(value))
            reference = AllocHandle(InspectHandle.ForValue(value));

        return new DebugVariable
        {
            Name = name,
            Value = preview,
            Type = type,
            VariablesReference = reference
        };
    }

    private static bool ShouldHideGlobal(string name, RuntimeValue value)
    {
        if (HiddenGlobalNames.Contains(name))
            return true;
        if (value.Type == ValueType.Function && BuiltInRegistry.IsInterpreterBuiltIn(name))
            return true;
        return false;
    }

    private static string UnwrapExceptionMessage(Exception ex)
    {
        var current = ex;
        while (current is AggregateException agg && agg.InnerException != null)
            current = agg.InnerException;
        return current.Message;
    }

    private enum InspectKind
    {
        Locals,
        Closure,
        Globals,
        This,
        Value
    }

    private sealed class InspectHandle
    {
        public InspectKind Kind { get; private init; }
        public Environment? Env { get; private init; }
        public ObjectInstance? Object { get; private init; }
        public RuntimeValue Value { get; private init; } = null!;

        public static InspectHandle ForLocals(Environment env) =>
            new() { Kind = InspectKind.Locals, Env = env };

        public static InspectHandle ForClosure(Environment env) =>
            new() { Kind = InspectKind.Closure, Env = env };

        public static InspectHandle ForGlobals(Environment env) =>
            new() { Kind = InspectKind.Globals, Env = env };

        public static InspectHandle ForThis(ObjectInstance obj) =>
            new() { Kind = InspectKind.This, Object = obj };

        public static InspectHandle ForValue(RuntimeValue value) =>
            new() { Kind = InspectKind.Value, Value = value };
    }

    private void SetStopReason(string reason, string? text = null)
    {
        LastStopReason = reason;
        LastStopText = text;
    }

    private void EnterPause(int line, string? file)
    {
        ResetInspectHandles();
        lock (_gate)
        {
            _mode = DebugMode.Paused;
            CurrentLine = line;
            CurrentFile = file;
            _resumeTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ReleaseGate(DebugMode mode)
    {
        lock (_gate)
        {
            _mode = mode;
            _resumeTcs?.TrySetResult();
            _resumeTcs = null;
        }
    }

    private void EnsureFreshRunToken()
    {
        if (!_stopCts.IsCancellationRequested)
            return;

        lock (_gate)
        {
            if (!_stopCts.IsCancellationRequested)
                return;
            _stopCts.Dispose();
            _stopCts = new CancellationTokenSource();
        }
    }

    private bool FilesMatch(string stored, string? incoming)
    {
        var normalizedStored = NormalizeFile(stored);
        var normalizedIncoming = NormalizeFile(incoming);
        if (PathEquals(normalizedStored, normalizedIncoming) && !string.IsNullOrEmpty(normalizedStored))
            return true;

        var main = NormalizeFile(string.IsNullOrEmpty(_mainFile) ? "main.malda" : _mainFile);
        if (string.IsNullOrEmpty(incoming) && PathEquals(normalizedStored, main))
            return true;
        if (string.IsNullOrEmpty(stored) && PathEquals(normalizedIncoming, main))
            return true;
        if (string.IsNullOrEmpty(incoming) && string.IsNullOrEmpty(stored))
            return true;

        return false;
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static bool IsFilesystemPath(string file)
    {
        if (file.StartsWith("memory:", StringComparison.OrdinalIgnoreCase))
            return false;

        var colon = file.IndexOf(':');
        if (colon > 1)
            return false;

        if (Path.IsPathRooted(file))
            return true;

        return file.Contains(Path.DirectorySeparatorChar) || file.Contains(Path.AltDirectorySeparatorChar);
    }
}
