// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Statements;
using Microsoft.JSInterop;

namespace MaldaLang.IDE.Services;

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
    private WebInputProvider? _inputProvider;
    private readonly IJSRuntime? _jsRuntime;
    private string _lastOutputSent = string.Empty;
    
    // Event fired when input is needed, allowing UI to update output before showing input prompt
    public event Action? InputNeeded;
    
    // Event fired when output needs to be updated (e.g., during sleep)
    public event Action? OutputNeedsUpdate;
    
    public ExecutionService(IJSRuntime? jsRuntime = null)
    {
        _jsRuntime = jsRuntime;
        if (_jsRuntime != null)
        {
            _inputProvider = new WebInputProvider(_jsRuntime);
        }
    }
    
    public async Task<ExecutionResult> ExecuteAsync(string source, string? input = null, string? sourceFileName = null)
    {
        _output.Clear();
        _lastOutputSent = string.Empty;
        if (!string.IsNullOrEmpty(input))
        {
            _inputQueue.Clear();
            // Split input by newlines and enqueue each line separately
            var lines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                _inputQueue.Enqueue(line);
            }
        }
        
        Interpreter.Interpreter? interpreter = null;
        try
        {
            // Capture output
            _originalOut = Console.Out;
            _originalIn = Console.In;
            
            _outputWriter = new StringWriter(_output);
            Console.SetOut(_outputWriter);
            
                // Configure Spectre.Console to output plain text (no ANSI codes) when output is redirected
                // This ensures IDE output panel shows clean text instead of escape sequences
                // Setting Ansi = false disables all ANSI codes including colors
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
                // Pre-queue input if provided
                if (!string.IsNullOrEmpty(input))
                {
                    var lines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        _inputProvider.QueueInput(line);
                    }
                }
            }
            
            // Set callback for output updates (e.g., during sleep)
            // Only trigger update if output content has changed
            interpreter.SetOutputUpdateCallback(() =>
            {
                var currentOutput = GetCurrentOutput();
                if (currentOutput != _lastOutputSent)
                {
                    _lastOutputSent = currentOutput;
                    OutputNeedsUpdate?.Invoke();
                }
            });
            
            await ExecuteWithInputHandling(interpreter, statements);
            
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
        // InputRequiredException is no longer used - input is handled via async/await
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
            if (_originalOut != null)
                Console.SetOut(_originalOut);
            if (_originalIn != null)
                Console.SetIn(_originalIn);
            _outputWriter?.Dispose();
            _inputReader?.Dispose();
        }
    }
    
    public void QueueInput(string input)
    {
        _inputQueue.Enqueue(input);
    }
    
    private async Task ExecuteWithInputHandling(Interpreter.Interpreter interpreter, List<Statement> statements)
    {
        await interpreter.InterpretAsync(statements);
        // Done - no retry needed! Execution continues exactly where it left off with async/await
    }
    
    public async Task<ExecutionResult> ExecuteWithDebuggerAsync(string source, IDebuggerHook debuggerHook, string? input = null, string? fileName = null)
    {
        _output.Clear();
        _lastOutputSent = string.Empty;
        if (!string.IsNullOrEmpty(input))
        {
            _inputQueue.Clear();
            // Split input by newlines and enqueue each line separately
            var lines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                _inputQueue.Enqueue(line);
            }
        }
        
        try
        {
            // Capture output
            _originalOut = Console.Out;
            _originalIn = Console.In;
            
            _outputWriter = new StringWriter(_output);
            Console.SetOut(_outputWriter);
            
                // Configure Spectre.Console to output plain text (no ANSI codes) when output is redirected
                // This ensures IDE output panel shows clean text instead of escape sequences
                // Setting Ansi = false disables all ANSI codes including colors
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
                
                return new ExecutionResult
                {
                    Success = false,
                    Output = _output.ToString(),
                    Error = $"Parse errors detected:\n{string.Join("\n", errorMessages)}\n\nPlease fix the syntax errors before running."
                };
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
            // Only trigger update if output content has changed
            _currentInterpreter.SetOutputUpdateCallback(() =>
            {
                var currentOutput = GetCurrentOutput();
                if (currentOutput != _lastOutputSent)
                {
                    _lastOutputSent = currentOutput;
                    OutputNeedsUpdate?.Invoke();
                }
            });
            
            // Run in a separate task to allow pausing
            var result = await Task.Run(async () =>
            {
                try
                {
                    await ExecuteWithInputHandling(_currentInterpreter, statements);
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
                    var errorMessage = FormatRuntimeError(ex, _currentInterpreter);
                    return new ExecutionResult
                    {
                        Success = false,
                        Output = _output.ToString(),
                        Error = errorMessage
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
            if (_originalOut != null)
                Console.SetOut(_originalOut);
            if (_originalIn != null)
                Console.SetIn(_originalIn);
            _outputWriter?.Dispose();
            _inputReader?.Dispose();
            _currentInterpreter = null;
            _currentDebuggerHook = null;
        }
    }
    
    public Interpreter.Interpreter? GetCurrentInterpreter()
    {
        return _currentInterpreter;
    }
    
    public string GetCurrentOutput()
    {
        // Flush the writer to ensure all buffered output is captured
        _outputWriter?.Flush();
        return _output.ToString();
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
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
}