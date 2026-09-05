// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Actors;

namespace MaldaLang.Tests;

/// <summary>
/// Base class for tests that provides common functionality:
/// - Console.Out redirection with proper cleanup
/// - Singleton state cleanup
/// - Temporary directory management
/// </summary>
public abstract class TestBase : IDisposable
{
    protected static readonly object _consoleLock = new object();
    private static readonly SemaphoreSlim _consoleSemaphore = new(1, 1);
    private TextWriter? _originalOut;
    private TextWriter? _originalError;
    private StringWriter? _outputWriter;
    private StringWriter? _errorWriter;
    private bool _disposed = false;

    protected TestBase()
    {
        // Clean up singleton state before each test
        CleanupSingletons();
    }

    /// <summary>
    /// Redirects Console.Out and Console.Error to StringWriters for capturing output.
    /// </summary>
    protected void RedirectConsole()
    {
        lock (_consoleLock)
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _outputWriter = new StringWriter();
            _errorWriter = new StringWriter();
            Console.SetOut(_outputWriter);
            Console.SetError(_errorWriter);
        }
    }

    /// <summary>
    /// Restores Console.Out and Console.Error to their original values.
    /// </summary>
    protected void RestoreConsole()
    {
        lock (_consoleLock)
        {
            if (_originalOut != null)
            {
                Console.SetOut(_originalOut);
                _originalOut = null;
            }
            if (_originalError != null)
            {
                Console.SetError(_originalError);
                _originalError = null;
            }
            _outputWriter?.Dispose();
            _errorWriter?.Dispose();
            _outputWriter = null;
            _errorWriter = null;
        }
        BuiltInFunctions.RebindSpectreConsoleForTesting(Console.Out);
    }

    /// <summary>
    /// Gets the captured standard output.
    /// </summary>
    protected string GetOutput()
    {
        if (_outputWriter == null)
            throw new InvalidOperationException("Console not redirected. Call RedirectConsole() first.");
        
        _outputWriter.Flush();
        return _outputWriter.ToString().Replace("\r", "").Trim();
    }

    /// <summary>
    /// Gets the captured standard error.
    /// </summary>
    protected string GetError()
    {
        if (_errorWriter == null)
            throw new InvalidOperationException("Console not redirected. Call RedirectConsole() first.");
        
        _errorWriter.Flush();
        return _errorWriter.ToString().Replace("\r", "").Trim();
    }

    /// <summary>
    /// Runs a program with Console.Out redirection and returns the output.
    /// </summary>
    protected async Task<string> RunProgramAsync(string source)
    {
        return await CaptureInterpretAsync(source);
    }

    /// <summary>
    /// Interpret outcome for ship-contract pairs: exit 0 on success, 1 when the
    /// walk throws (same convention as a transpiled <c>Main</c> catch).
    /// </summary>
    internal sealed class InterpretOutcome
    {
        public int ExitCode { get; init; }
        public string StdOut { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
    }

    /// <summary>
    /// Interpret <paramref name="source"/> with stdout capture. Shares the console gate
    /// with instance <see cref="RunProgramAsync"/> so pair tests do not race other fixtures.
    /// </summary>
    internal static async Task<string> CaptureInterpretAsync(string source, string? sourceFileName = null)
    {
        var outcome = await CaptureInterpretOutcomeAsync(source, sourceFileName);
        if (outcome.Exception != null)
            throw outcome.Exception;
        return outcome.StdOut;
    }

    /// <summary>
    /// Same capture as <see cref="CaptureInterpretAsync"/> but does not rethrow, so
    /// interpret/transpile pairs can compare exit identity.
    /// </summary>
    internal static async Task<InterpretOutcome> CaptureInterpretOutcomeAsync(string source, string? sourceFileName = null)
    {
        await _consoleSemaphore.WaitAsync();
        try
        {
            BuiltInFunctions.ClearGetEnvCacheForTesting();
            // Pair tests and runnable-manual snippets share these process-wide
            // registries. Clear under the console gate so `api Calc` in
            // 09-functions.html cannot collide with Examples/Prompts/api_program_calc.malda.
            SchemaRegistry.ClearForTesting();
            SumTypeRegistry.ClearForTesting();
            ApiRegistry.ClearForTesting();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var outputWriter = new StringWriter();
            using var errorWriter = new StringWriter();
            Console.SetOut(outputWriter);
            Console.SetError(errorWriter);
            BuiltInFunctions.RebindSpectreConsoleForTesting(outputWriter);
            try
            {
                var lexer = new Lexer(source, sourceFileName);
                var tokens = lexer.Tokenize();
                var parser = new Parser.Parser(tokens, sourceFileName);
                var statements = parser.Parse();
                if (parser.Errors.Count > 0)
                {
                    throw parser.Errors.Count == 1
                        ? parser.Errors[0]
                        : new Exception(string.Join(System.Environment.NewLine, parser.Errors.Select(e => e.Message)));
                }

                var interpreter = new Interpreter.Interpreter(currentFile: sourceFileName);
                await interpreter.InterpretAsync(statements);
                await Task.Delay(100);
                outputWriter.Flush();
                return new InterpretOutcome
                {
                    ExitCode = 0,
                    StdOut = outputWriter.ToString().Replace("\r", "").Trim()
                };
            }
            catch (Exception ex)
            {
                outputWriter.Flush();
                return new InterpretOutcome
                {
                    ExitCode = 1,
                    StdOut = outputWriter.ToString().Replace("\r", "").Trim(),
                    Exception = ex
                };
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                BuiltInFunctions.RebindSpectreConsoleForTesting(originalOut);
            }
        }
        finally
        {
            _consoleSemaphore.Release();
        }
    }

    /// <summary>
    /// Runs a program synchronously with Console.Out redirection and returns the output.
    /// </summary>
    protected string RunProgram(string source)
    {
        return RunProgramAsync(source).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Cleans up singleton state that might persist between tests.
    /// </summary>
    protected static void CleanupSingletons()
    {
        // Clean up ActorRuntime (interpreter mode)
        ActorRuntime.ClearInstanceForTesting();
        
        // Clean up ActorsRuntime (transpiled mode)
        ActorsRuntime.ResetForTesting();
        
        // ToolRegistry is cleared by Interpreter constructor, but we can also clear it explicitly
        // Note: We don't clear persistent tools as they might be from IDE services
        ToolRegistry.Instance.ClearUserDefinedTools();

        AgentPlanStore.Clear();
    }

    /// <summary>
    /// Creates a temporary directory for test files.
    /// </summary>
    protected string CreateTempDirectory(string prefix = "test_")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Safely deletes a directory, ignoring errors.
    /// </summary>
    protected void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                RestoreConsole();
                CleanupSingletons();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
