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
        await _consoleSemaphore.WaitAsync();
        try
        {
            BuiltInFunctions.ClearGetEnvCacheForTesting();
            RedirectConsole();
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                throw parser.Errors.Count == 1
                    ? parser.Errors[0]
                    : new Exception(string.Join(System.Environment.NewLine, parser.Errors.Select(e => e.Message)));
            }
            var interpreter = new Interpreter.Interpreter();
            await interpreter.InterpretAsync(statements);
            
            // Give actors time to process messages
            await Task.Delay(100);
            
            return GetOutput();
        }
        finally
        {
            RestoreConsole();
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
