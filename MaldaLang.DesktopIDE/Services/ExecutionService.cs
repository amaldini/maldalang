// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Statements;
using Spectre.Console;

namespace MaldaLang.DesktopIDE.Services;

public class ExecutionService
{
    private readonly StringBuilder _output = new();
    private readonly Queue<string> _inputQueue = new();
    private StringWriter? _outputWriter;
    private StringReader? _inputReader;
    private TextWriter? _originalOut;
    private TextReader? _originalIn;
    private Interpreter.Interpreter? _currentInterpreter;
    private IDebuggerHook? _currentDebuggerHook;
    private DesktopInputProvider? _inputProvider;
    private ToolCallLogService? _toolCallLogService;
    private string _lastOutputSent = string.Empty;
    private bool _consoleAllocated = false;
    private IntPtr _consoleHandle = IntPtr.Zero;
    private StreamWriter? _consoleStreamWriter;
    
    // Debouncing for output updates
    private CancellationTokenSource? _outputUpdateDebounceCts;
    private readonly object _outputUpdateLock = new object();
    private const int OUTPUT_UPDATE_DEBOUNCE_MS = 100; // Debounce delay in milliseconds
    
    // Windows API for console allocation
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? HandlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool Add);
    
    private delegate bool ConsoleCtrlDelegate(uint dwCtrlType);
    
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const int SW_SHOW = 5;
    private const uint CTRL_CLOSE_EVENT = 2;
    
    // Console control handler to prevent IDE from closing when console window is closed
    private ConsoleCtrlDelegate? _consoleCtrlHandler;
    private static ExecutionService? _instanceForHandler;
    
    // Event fired when output needs to be updated (e.g., during sleep)
    public event Action? OutputNeedsUpdate;
    
    public ExecutionService()
    {
        _inputProvider = new DesktopInputProvider();
        
        // Store instance for console control handler
        _instanceForHandler = this;
        
        // Subscribe to AnsiConsole usage events to optionally open console window
        AnsiConsoleInstance.OnAnsiConsoleUsed += HandleAnsiConsoleUsed;
    }
    
    // Console control handler - prevents IDE from closing when console window is closed
    private static bool ConsoleCtrlHandler(uint dwCtrlType)
    {
        // Handle CTRL_CLOSE_EVENT (when user closes console window)
        if (dwCtrlType == CTRL_CLOSE_EVENT)
        {
            // Get the instance and gracefully free the console
            if (_instanceForHandler != null)
            {
                _instanceForHandler.HandleConsoleClose();
            }
            // Return true to indicate we handled the event (prevents default termination)
            return true;
        }
        // For other events, allow default handling
        return false;
    }
    
    private void HandleConsoleClose()
    {
        // Gracefully free the console without terminating the IDE
        try
        {
            // Free the console
            if (_consoleHandle != IntPtr.Zero)
            {
                // Restore original console output before freeing
                if (_originalOut != null)
                {
                    Console.SetOut(_originalOut);
                }
                FreeConsole();
                _consoleAllocated = false;
                _consoleHandle = IntPtr.Zero;
            }
            
            // Dispose console stream writer
            if (_consoleStreamWriter != null)
            {
                try
                {
                    _consoleStreamWriter.Flush();
                    _consoleStreamWriter.Dispose();
                }
                catch
                {
                    // Ignore errors
                }
                _consoleStreamWriter = null;
            }
        }
        catch
        {
            // Ignore errors during console cleanup
        }
    }
    
    private void HandleAnsiConsoleUsed()
    {
        // When AnsiConsole is used, allocate a console window if not already allocated
        if (!_consoleAllocated)
        {
            try
            {
                // Check if we already have a console (e.g., if running from command line)
                if (GetConsoleWindow() == IntPtr.Zero)
                {
                    // Allocate a new console window
                    if (AllocConsole())
                    {
                        _consoleAllocated = true;
                        _consoleHandle = GetConsoleWindow();
                        
                        // Set up console control handler to prevent IDE from closing when console is closed
                        _consoleCtrlHandler = ConsoleCtrlHandler;
                        SetConsoleCtrlHandler(_consoleCtrlHandler, true);
                        
                        // Set console title
                        Console.Title = "MaldaLang - Spectre.Console Output";
                        
                        // Configure console encoding for Unicode support
                        Console.OutputEncoding = System.Text.Encoding.UTF8;
                        
                        // Enable ANSI escape codes in Windows console
                        // This is required for Spectre.Console markup to work properly
                        try
                        {
                            var stdoutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                            if (GetConsoleMode(stdoutHandle, out uint mode))
                            {
                                mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                                SetConsoleMode(stdoutHandle, mode);
                            }
                        }
                        catch
                        {
                            // Ignore errors - ANSI might not be supported on older Windows versions
                        }
                        
                        // After AllocConsole(), we need to get a fresh reference to the actual console output
                        // Create a new StreamWriter that writes directly to the console
                        _consoleStreamWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        
                        // Show and bring console window to foreground
                        ShowWindow(_consoleHandle, SW_SHOW);
                        SetForegroundWindow(_consoleHandle);
                        
                        // Create a dual writer that writes to both StringWriter (for IDE panel) and console
                        // This allows both regular output and Spectre.Console output to appear in both places
                        var dualWriter = new DualTextWriter(_outputWriter, _consoleStreamWriter);
                        Console.SetOut(dualWriter);
                        
                        // Force flush to ensure console is ready
                        _consoleStreamWriter.Flush();
                        
                        // Re-enable ANSI codes for Spectre.Console since we now have a real console
                        // Configure Spectre.Console to use our dual writer via a custom console instance
                        try
                        {
                            // Create a custom console that writes to our dual writer
                            var settings = new AnsiConsoleSettings
                            {
                                Ansi = AnsiSupport.Yes,
                                ColorSystem = ColorSystemSupport.TrueColor, // Use TrueColor for best support
                                Interactive = InteractionSupport.No,
                                Out = new DualAnsiConsoleOutput(dualWriter)
                            };
                            
                            // Create and set the custom console
                            var customConsole = AnsiConsole.Create(settings);
                            AnsiConsole.Console = customConsole;
                            
                            // Verify ANSI is enabled
                            if (!AnsiConsole.Profile.Capabilities.Ansi)
                            {
                                AnsiConsole.Profile.Capabilities.Ansi = true;
                            }
                            if (!AnsiConsole.Profile.Capabilities.Unicode)
                            {
                                AnsiConsole.Profile.Capabilities.Unicode = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Fallback: try to enable ANSI on the default console
                            try
                            {
                                AnsiConsole.Profile.Capabilities.Ansi = true;
                                AnsiConsole.Profile.Capabilities.Unicode = true;
                            }
                            catch
                            {
                                // Ignore errors
                            }
                        }
                    }
                }
                else
                {
                    // Console already exists (running from command line)
                    _consoleAllocated = true;
                    _consoleHandle = GetConsoleWindow();
                    
                    // Enable ANSI escape codes in Windows console
                    try
                    {
                        var stdoutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                        if (GetConsoleMode(stdoutHandle, out uint mode))
                        {
                            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                            SetConsoleMode(stdoutHandle, mode);
                        }
                    }
                    catch
                    {
                        // Ignore errors - ANSI might not be supported on older Windows versions
                    }
                    
                    // Re-enable ANSI codes
                    try
                    {
                        AnsiConsole.Profile.Capabilities.Ansi = true;
                        AnsiConsole.Profile.Capabilities.Unicode = true;
                    }
                    catch
                    {
                        // Ignore errors
                    }
                }
            }
            catch
            {
                // Ignore errors - console allocation is optional
            }
        }
    }
    
    // Custom IAnsiConsoleOutput that writes to our dual writer
    private class DualAnsiConsoleOutput : Spectre.Console.IAnsiConsoleOutput
    {
        private readonly TextWriter _writer;
        
        public DualAnsiConsoleOutput(TextWriter writer)
        {
            _writer = writer;
        }
        
        public TextWriter Writer => _writer;
        public bool IsTerminal => true; // We have a real console now
        public int Width => Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        public int Height => Console.WindowHeight > 0 ? Console.WindowHeight : 24;
        
        public void SetEncoding(System.Text.Encoding encoding)
        {
            // Encoding is handled by the underlying TextWriter
        }
    }
    
    // Dual writer that writes to both StringWriter (for IDE panel) and Console.Out (for console window)
    private class DualTextWriter : TextWriter
    {
        private readonly TextWriter _ideWriter;
        private readonly TextWriter _consoleWriter;
        
        public DualTextWriter(TextWriter ideWriter, TextWriter consoleWriter)
        {
            _ideWriter = ideWriter;
            _consoleWriter = consoleWriter;
        }
        
        public override Encoding Encoding => _ideWriter.Encoding;
        
        public override void Write(char value)
        {
            try { _ideWriter.Write(value); } catch { }
            try 
            { 
                _consoleWriter.Write(value);
                // Force flush console immediately for better visibility
                if (_consoleWriter is StreamWriter sw && sw.AutoFlush == false)
                {
                    sw.Flush();
                }
            } 
            catch { }
        }
        
        public override void Write(string? value)
        {
            try { _ideWriter.Write(value); } catch { }
            try 
            { 
                _consoleWriter.Write(value);
                // Force flush console immediately for better visibility
                if (_consoleWriter is StreamWriter sw && sw.AutoFlush == false)
                {
                    sw.Flush();
                }
            } 
            catch { }
        }
        
        public override void Write(char[] buffer, int index, int count)
        {
            try { _ideWriter.Write(buffer, index, count); } catch { }
            try 
            { 
                _consoleWriter.Write(buffer, index, count);
                // Force flush console immediately for better visibility
                if (_consoleWriter is StreamWriter sw && sw.AutoFlush == false)
                {
                    sw.Flush();
                }
            } 
            catch { }
        }
        
        public override void WriteLine(string? value)
        {
            try { _ideWriter.WriteLine(value); } catch { }
            try 
            { 
                _consoleWriter.WriteLine(value);
                // Force flush console immediately for better visibility
                if (_consoleWriter is StreamWriter sw && sw.AutoFlush == false)
                {
                    sw.Flush();
                }
            } 
            catch { }
        }
        
        public override void WriteLine()
        {
            try { _ideWriter.WriteLine(); } catch { }
            try 
            { 
                _consoleWriter.WriteLine();
                // Force flush console immediately for better visibility
                if (_consoleWriter is StreamWriter sw && sw.AutoFlush == false)
                {
                    sw.Flush();
                }
            } 
            catch { }
        }
        
        public override void Flush()
        {
            try { _ideWriter.Flush(); } catch { }
            try { _consoleWriter.Flush(); } catch { }
        }
        
        protected override void Dispose(bool disposing)
        {
            // Don't dispose the underlying writers - they're managed elsewhere
        }
    }
    
    public async Task<ExecutionResult> ExecuteAsync(string source, string? input = null, string? sourceFileName = null)
    {
        // Cancel any pending output updates from previous execution
        lock (_outputUpdateLock)
        {
            _outputUpdateDebounceCts?.Cancel();
            _outputUpdateDebounceCts?.Dispose();
            _outputUpdateDebounceCts = null;
        }
        
        _output.Clear();
        _lastOutputSent = string.Empty;
        
        // Clear input provider queue at the start of each execution to prevent leftover input from previous runs
        if (_inputProvider is DesktopInputProvider desktopProvider)
        {
            desktopProvider.Clear();
        }
        
        if (!string.IsNullOrEmpty(input))
        {
            _inputQueue.Clear();
            // Split input by newlines and enqueue each line separately
            var lines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                _inputQueue.Enqueue(line);
            }
            
            // Also queue in input provider
            if (_inputProvider != null)
            {
                foreach (var line in lines)
                {
                    _inputProvider.QueueInput(line);
                }
            }
        }
        
        // Run execution on background thread to keep UI responsive
        return await Task.Run(async () =>
        {
            Interpreter.Interpreter? interpreter = null;
            try
            {
                // Capture output
                _originalOut = Console.Out;
                _originalIn = Console.In;
                
                // Reset console allocation state for new execution
                _consoleAllocated = false;
                _consoleHandle = IntPtr.Zero;
                
                _outputWriter = new StringWriter(_output);
                Console.SetOut(_outputWriter);
                
                // Configure Spectre.Console to output plain text (no ANSI codes) when output is redirected
                // This ensures IDE output panel shows clean text instead of escape sequences
                // Setting Ansi = false disables all ANSI codes including colors
                // Note: If AnsiConsole is used, HandleAnsiConsoleUsed() will allocate a console and re-enable ANSI
                try
                {
                    Spectre.Console.AnsiConsole.Profile.Capabilities.Ansi = false;
                    Spectre.Console.AnsiConsole.Profile.Capabilities.Unicode = false;
                }
                catch
                {
                    // Ignore errors - configuration is best effort
                }
                
                if (_inputQueue.Count > 0)
                {
                    _inputReader = new StringReader(string.Join("\n", _inputQueue));
                    Console.SetIn(_inputReader);
                }
                
                var lexer = new Lexer(source, sourceFileName);
                var tokens = lexer.Tokenize();
                
                var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
                var statements = parser.Parse();
                
                // Check for parse errors - if any exist, stop execution and report them
                if (parser.Errors.Count > 0)
                {
                    // Format all parse errors for the user
                    var errorMessages = parser.Errors.Select(e =>
                    {
                        var message = e.Message;
                        // Extract just the error message (after "Parse error at line X, column Y: ")
                        var colonIndex = message.LastIndexOf(": ");
                        if (colonIndex >= 0 && colonIndex < message.Length - 2)
                        {
                            message = message.Substring(colonIndex + 2);
                        }
                        if (!string.IsNullOrWhiteSpace(e.SourceFileName))
                        {
                            return $"{e.SourceFileName} (Line {e.Line}, Column {e.Column}): {message}";
                        }

                        return $"Line {e.Line}, Column {e.Column}: {message}";
                    });
                    
                    return new ExecutionResult
                    {
                        Success = false,
                        Output = _output.ToString(),
                        Error = $"Parse errors detected:\n{string.Join("\n", errorMessages)}\n\nPlease fix the syntax errors before running."
                    };
                }
                
                interpreter = new Interpreter.Interpreter();
                // Store source code for enhanced error reporting
                interpreter.SetSourceCode(source);
                
                if (_inputProvider != null)
                {
                    interpreter.SetInputProvider(_inputProvider);
                }
                
                // Set callback for output updates (e.g., during sleep)
                // Debounced to avoid updating too frequently
                interpreter.SetOutputUpdateCallback(() =>
                {
                    DebouncedOutputUpdate();
                });
                
                // Set up tool call logging
                if (_toolCallLogService != null)
                {
                    MaldaLang.BuiltIns.ConversationInstance.SetToolCallLogger((toolName, args, result, isError, fullArgs) =>
                    {
                        _toolCallLogService.LogToolCall(toolName, args, result, isError, fullArgs);
                        // Also write to output console in a concise format
                        WriteToolCallToOutput(toolName, args, result, isError);
                    });
                }
                
                await ExecuteWithInputHandling(interpreter, statements);
                
                // Flush any pending output updates
                FlushPendingOutputUpdate();
                
                return new ExecutionResult
                {
                    Success = true,
                    Output = _output.ToString(),
                    Error = null
                };
            }
            catch (MaldaLang.Parser.ParseException ex)
            {
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = ex.Message
                };
            }
            catch (RuntimeException ex)
            {
                var errorMessage = FormatRuntimeError(ex, interpreter);
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = errorMessage
                };
            }
            catch (InputRequiredException inputEx)
            {
                // InputRequiredException is thrown by ask_user tool when input is needed
                // The prompt has already been printed to output, so we just need to return
                // a proper error message that includes the prompt
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = $"Input required: {inputEx.Prompt}"
                };
            }
            catch (Exception ex)
            {
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = ex.Message
                };
            }
            finally
            {
                // Restore original streams
                if (_originalOut != null && !_consoleAllocated)
                {
                    Console.SetOut(_originalOut);
                }
                if (_originalIn != null && !_consoleAllocated)
                {
                    Console.SetIn(_originalIn);
                }
                
                // Dispose console stream writer if we created it
                if (_consoleStreamWriter != null)
                {
                    try
                    {
                        _consoleStreamWriter.Flush();
                        _consoleStreamWriter.Dispose();
                    }
                    catch
                    {
                        // Ignore errors
                    }
                    _consoleStreamWriter = null;
                }
                
                _outputWriter?.Dispose();
                _inputReader?.Dispose();
                
                // Free console if we allocated it
                if (_consoleAllocated && _consoleHandle != IntPtr.Zero)
                {
                    try
                    {
                        // Remove console control handler before freeing console
                        if (_consoleCtrlHandler != null)
                        {
                            SetConsoleCtrlHandler(_consoleCtrlHandler, false);
                            _consoleCtrlHandler = null;
                        }
                        
                        // Restore original console output before freeing
                        if (_originalOut != null)
                        {
                            Console.SetOut(_originalOut);
                        }
                        FreeConsole();
                    }
                    catch
                    {
                        // Ignore errors
                    }
                    _consoleAllocated = false;
                    _consoleHandle = IntPtr.Zero;
                }
            }
        });
    }
    
    public void QueueInput(string input)
    {
        _inputQueue.Enqueue(input);
        _inputProvider?.QueueInput(input);
    }
    
    private async Task ExecuteWithInputHandling(MaldaLang.Interpreter.Interpreter interpreter, List<Statement> statements)
    {
        await interpreter.InterpretAsync(statements);
        // Done - no retry needed! Execution continues exactly where it left off with async/await
    }
    
    public async Task<ExecutionResult> ExecuteWithDebuggerAsync(string source, IDebuggerHook debuggerHook, string? input = null, string? fileName = null, bool hostPartitionOnly = false)
    {
        // Cancel any pending output updates from previous execution
        lock (_outputUpdateLock)
        {
            _outputUpdateDebounceCts?.Cancel();
            _outputUpdateDebounceCts?.Dispose();
            _outputUpdateDebounceCts = null;
        }
        
        _output.Clear();
        _lastOutputSent = string.Empty;
        
        // Clear input provider queue at the start of each execution to prevent leftover input from previous runs
        if (_inputProvider is DesktopInputProvider desktopProvider)
        {
            desktopProvider.Clear();
        }
        
        if (!string.IsNullOrEmpty(input))
        {
            _inputQueue.Clear();
            // Split input by newlines and enqueue each line separately
            var lines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                _inputQueue.Enqueue(line);
            }
            
            // Also queue in input provider
            if (_inputProvider != null)
            {
                foreach (var line in lines)
                {
                    _inputProvider.QueueInput(line);
                }
            }
        }
        
        try
        {
            // Capture output
            _originalOut = Console.Out;
            _originalIn = Console.In;
            
            // Reset console allocation state for new execution
            _consoleAllocated = false;
            _consoleHandle = IntPtr.Zero;
            _consoleStreamWriter = null;
            
            _outputWriter = new StringWriter(_output);
            Console.SetOut(_outputWriter);
            
            // Configure Spectre.Console to output plain text (no ANSI codes) when output is redirected
            // This ensures IDE output panel shows clean text instead of escape sequences
            // Setting Ansi = false disables all ANSI codes including colors
            // Note: If AnsiConsole is used, HandleAnsiConsoleUsed() will allocate a console and re-enable ANSI
            try
            {
                Spectre.Console.AnsiConsole.Profile.Capabilities.Ansi = false;
                Spectre.Console.AnsiConsole.Profile.Capabilities.Unicode = false;
            }
            catch
            {
                // Ignore errors - configuration is best effort
            }
            
            if (_inputQueue.Count > 0)
            {
                _inputReader = new StringReader(string.Join("\n", _inputQueue));
                Console.SetIn(_inputReader);
            }
            
            var lexer = new Lexer(source, fileName);
            var tokens = lexer.Tokenize();
            
            var parser = new MaldaLang.Parser.Parser(tokens, fileName);
            var statements = parser.Parse();
            
            // Check for parse errors - if any exist, stop execution and report them
            if (parser.Errors.Count > 0)
            {
                // Format all parse errors for the user
                var errorMessages = parser.Errors.Select(e =>
                {
                    var message = e.Message;
                    // Extract just the error message (after "Parse error at line X, column Y: ")
                    var colonIndex = message.LastIndexOf(": ");
                    if (colonIndex >= 0 && colonIndex < message.Length - 2)
                    {
                        message = message.Substring(colonIndex + 2);
                    }
                    if (!string.IsNullOrWhiteSpace(e.SourceFileName))
                    {
                        return $"{e.SourceFileName} (Line {e.Line}, Column {e.Column}): {message}";
                    }

                    return $"Line {e.Line}, Column {e.Column}: {message}";
                });
                
                // Flush any pending output updates
                FlushPendingOutputUpdate();
                
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = $"Parse errors detected:\n{string.Join("\n", errorMessages)}\n\nPlease fix the syntax errors before running."
                };
            }

            if (hostPartitionOnly)
            {
                statements = HostDebugPartition.KeepHostStatements(statements);
            }
            
            _currentDebuggerHook = debuggerHook;
            _currentInterpreter = new Interpreter.Interpreter(debuggerHook, fileName);
            // Store source code for enhanced error reporting
            _currentInterpreter.SetSourceCode(source);
            
            // Set interpreter reference in hook if it's a DebuggerHook
            if (debuggerHook is DebuggerHook hook)
            {
                hook.SetInterpreter(_currentInterpreter);
            }
            
            // Set callback for output updates (e.g., during sleep)
            // Debounced to avoid updating too frequently
            _currentInterpreter.SetOutputUpdateCallback(() =>
            {
                DebouncedOutputUpdate();
            });
            
            // Set up tool call logging
            if (_toolCallLogService != null)
            {
                MaldaLang.BuiltIns.ConversationInstance.SetToolCallLogger((toolName, args, result, isError, fullArgs) =>
                {
                    _toolCallLogService.LogToolCall(toolName, args, result, isError, fullArgs);
                    // Also write to output console in a concise format
                    WriteToolCallToOutput(toolName, args, result, isError);
                });
            }
            
            // Run in a separate task to allow pausing
            var result = await Task.Run(async () =>
            {
                try
                {
                    await ExecuteWithInputHandling(_currentInterpreter, statements);
                    
                    // Flush any pending output updates
                    FlushPendingOutputUpdate();
                    
                    return new ExecutionResult
                    {
                        Success = true,
                        Output = _output.ToString(),
                        Error = null
                    };
                }
            catch (MaldaLang.Parser.ParseException ex)
            {
                // Flush any pending output updates
                FlushPendingOutputUpdate();
                
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = ex.Message
                };
            }
            catch (RuntimeException ex)
            {
                // Flush any pending output updates
                FlushPendingOutputUpdate();
                
                var errorMessage = FormatRuntimeError(ex, _currentInterpreter);
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = errorMessage
                };
            }
                catch (OperationCanceledException)
                {
                    FlushPendingOutputUpdate();
                    return new ExecutionResult
                    {
                        Success = true,
                        Output = _output.ToString(),
                        Error = null
                    };
                }
                catch (InputRequiredException inputEx)
                {
                    // This should not happen here - input should be handled in ExecuteWithInputHandling
                    return new ExecutionResult
                    {
                        Success = false,
                        Output = _output.ToString(),
                        Error = $"Input required: {inputEx.Prompt}"
                    };
                }
                catch (Exception ex)
                {
                    return new ExecutionResult
                    {
                        Success = false,
                        Output = _output.ToString(),
                        Error = ex.Message
                    };
                }
            });
            
            return result;
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                Success = false,
                Output = _output.ToString(),
                Error = ex.Message
            };
        }
        finally
        {
            // Restore original streams
            if (_originalOut != null && !_consoleAllocated)
            {
                Console.SetOut(_originalOut);
            }
            if (_originalIn != null && !_consoleAllocated)
            {
                Console.SetIn(_originalIn);
            }
            
            // Dispose console stream writer if we created it
            if (_consoleStreamWriter != null)
            {
                try
                {
                    _consoleStreamWriter.Flush();
                    _consoleStreamWriter.Dispose();
                }
                catch
                {
                    // Ignore errors
                }
                _consoleStreamWriter = null;
            }
            
            _outputWriter?.Dispose();
            _inputReader?.Dispose();
            
            // Free console if we allocated it
            if (_consoleAllocated && _consoleHandle != IntPtr.Zero)
            {
                try
                {
                    // Remove console control handler before freeing console
                    if (_consoleCtrlHandler != null)
                    {
                        SetConsoleCtrlHandler(_consoleCtrlHandler, false);
                        _consoleCtrlHandler = null;
                    }
                    
                    // Restore original console output before freeing
                    if (_originalOut != null)
                    {
                        Console.SetOut(_originalOut);
                    }
                    FreeConsole();
                }
                catch
                {
                    // Ignore errors
                }
                _consoleAllocated = false;
                _consoleHandle = IntPtr.Zero;
            }
            
            _currentInterpreter = null;
            _currentDebuggerHook = null;
        }
    }
    
    public Interpreter.Interpreter? GetCurrentInterpreter()
    {
        return _currentInterpreter;
    }
    
    public DesktopInputProvider? GetInputProvider()
    {
        return _inputProvider;
    }
    
    private void WriteToolCallToOutput(string toolName, string args, string result, bool isError)
    {
        if (_outputWriter == null)
            return;
        
        // Format tool call concisely (1-2 lines)
        var argsPreview = args ?? "";
        if (argsPreview.Length > 100)
        {
            argsPreview = argsPreview.Substring(0, 100) + "...";
        }
        
        var resultPreview = result ?? "";
        if (resultPreview.Length > 150)
        {
            resultPreview = resultPreview.Substring(0, 150) + "...";
        }
        
        // Write concise tool call info (1-2 lines)
        if (!string.IsNullOrEmpty(argsPreview))
        {
            _outputWriter.WriteLine($"🔧 {toolName}({argsPreview})");
        }
        else
        {
            _outputWriter.WriteLine($"🔧 {toolName}()");
        }
        
        if (!string.IsNullOrEmpty(resultPreview))
        {
            var status = isError ? "❌" : "✅";
            _outputWriter.WriteLine($"  {status} {resultPreview}");
        }
    }
    
    public string GetCurrentOutput()
    {
        // Flush the writer to ensure all buffered output is captured
        _outputWriter?.Flush();
        return _output.ToString();
    }
    
    public void SetToolCallLogService(ToolCallLogService? service)
    {
        _toolCallLogService = service;
    }
    
    public ToolCallLogService? GetToolCallLogService()
    {
        return _toolCallLogService;
    }
    
    private string FormatRuntimeError(RuntimeException ex, Interpreter.Interpreter? interpreter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Error: {ex.Message}");
        
        // Add line number if available
        if (ex.Line.HasValue)
        {
            sb.AppendLine($"Line: {ex.Line.Value}");
            if (!string.IsNullOrEmpty(ex.File))
            {
                sb.AppendLine($"File: {ex.File}");
            }
            
            // Show problematic source line with context
            string? sourceLine = ex.SourceLine;
            if (sourceLine == null && interpreter != null && ex.Line.HasValue)
            {
                sourceLine = interpreter.GetSourceLine(ex.Line.Value);
            }
            
            if (!string.IsNullOrEmpty(sourceLine))
            {
                sb.AppendLine();
                sb.AppendLine("Source code:");
                
                // Show context lines (previous and next) if available
                if (interpreter != null && ex.Line.HasValue)
                {
                    var lineNum = ex.Line.Value;
                    var contextLines = 2; // Show 2 lines before and after
                    
                    // Show previous context lines
                    for (int i = Math.Max(1, lineNum - contextLines); i < lineNum; i++)
                    {
                        var contextLine = interpreter.GetSourceLine(i);
                        if (contextLine != null)
                        {
                            sb.AppendLine($"  {i,4} | {contextLine}");
                        }
                    }
                    
                    // Show the problematic line with indicator
                    sb.AppendLine($"  {lineNum,4} | {sourceLine}");
                    sb.AppendLine($"       | {new string('^', Math.Min(sourceLine.Length, 50))} <-- Error here");
                    
                    // Show next context lines
                    for (int i = lineNum + 1; i <= lineNum + contextLines; i++)
                    {
                        var contextLine = interpreter.GetSourceLine(i);
                        if (contextLine != null)
                        {
                            sb.AppendLine($"  {i,4} | {contextLine}");
                        }
                    }
                }
                else
                {
                    // Fallback: just show the line without context
                    sb.AppendLine($"  {ex.Line.Value,4} | {sourceLine}");
                }
            }
        }
        
        // Add call stack if interpreter is available (especially in debug mode)
        if (interpreter != null)
        {
            var callStack = interpreter.GetCallStack();
            if (callStack.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Call Stack:");
                for (int i = callStack.Count - 1; i >= 0; i--)
                {
                    var frame = callStack[i];
                    var frameInfo = string.IsNullOrEmpty(frame.ClassName) 
                        ? frame.FunctionName 
                        : $"{frame.ClassName}.{frame.FunctionName}";
                    sb.AppendLine($"  at {frameInfo} (line {frame.Line} in {frame.File})");
                }
            }
            
            // Add variables if in debug mode
            var debuggerHook = interpreter.GetDebuggerHook();
            if (debuggerHook != null)
            {
                var variables = interpreter.GetVariables();
                if (variables.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Variables:");
                    foreach (var kvp in variables)
                    {
                        var valueStr = FormatRuntimeValue(kvp.Value);
                        sb.AppendLine($"  {kvp.Key} = {valueStr}");
                    }
                }
            }
        }
        
        return sb.ToString().TrimEnd();
    }
    
    private string FormatRuntimeValue(object value)
    {
        if (value == null) return "null";
        
        // Handle RuntimeValue types
        if (value is RuntimeValue rv)
        {
            return rv.Type switch
            {
                MaldaLang.Interpreter.ValueType.String => $"\"{rv.AsString()}\"",
                MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger().ToString(),
                MaldaLang.Interpreter.ValueType.Float => rv.AsFloat().ToString(),
                MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean().ToString().ToLower(),
                MaldaLang.Interpreter.ValueType.Null => "null",
                MaldaLang.Interpreter.ValueType.Array => $"[Array({rv.AsArrayInstance().Elements.Count})]",
                MaldaLang.Interpreter.ValueType.Object => $"[Object({rv.AsObject().Class.Name})]",
                MaldaLang.Interpreter.ValueType.Function => "[Function]",
                MaldaLang.Interpreter.ValueType.Class => $"[Class({rv.AsClass().Name})]",
                _ => value.ToString() ?? "null"
            };
        }
        
        return value.ToString() ?? "null";
    }
    
    /// <summary>
    /// Debounced output update to avoid updating UI too frequently.
    /// Cancels any pending update and schedules a new one after a delay.
    /// </summary>
    private void DebouncedOutputUpdate()
    {
        lock (_outputUpdateLock)
        {
            // Cancel any pending update
            _outputUpdateDebounceCts?.Cancel();
            _outputUpdateDebounceCts?.Dispose();
            
            // Create a new cancellation token source
            _outputUpdateDebounceCts = new CancellationTokenSource();
            var token = _outputUpdateDebounceCts.Token;
            
            // Schedule the update after debounce delay
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(OUTPUT_UPDATE_DEBOUNCE_MS, token);
                    
                    // Check if output has changed before updating
                    if (!token.IsCancellationRequested)
                    {
                        var currentOutput = GetCurrentOutput();
                        if (currentOutput != _lastOutputSent)
                        {
                            _lastOutputSent = currentOutput;
                            OutputNeedsUpdate?.Invoke();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when a new update comes in before the delay completes
                }
            }, token);
        }
    }
    
    /// <summary>
    /// Flushes any pending output update immediately, canceling the debounce delay.
    /// Used when execution completes to ensure final output is shown.
    /// </summary>
    private void FlushPendingOutputUpdate()
    {
        lock (_outputUpdateLock)
        {
            // Cancel any pending debounced update
            _outputUpdateDebounceCts?.Cancel();
            _outputUpdateDebounceCts?.Dispose();
            _outputUpdateDebounceCts = null;
            
            // Immediately update if output has changed
            var currentOutput = GetCurrentOutput();
            if (currentOutput != _lastOutputSent)
            {
                _lastOutputSent = currentOutput;
                OutputNeedsUpdate?.Invoke();
            }
        }
    }
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
}