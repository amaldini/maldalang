// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using System.Text;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Executable;

class Program
{
    static void Main(string[] args)
    {
        // Subscribe to model loading progress
        ModelLoadingService.OnProgressChanged += OnModelLoadingProgress;
        
        try
        {
            // Read embedded source code
            var source = ReadEmbeddedResource("program.malda");
            MaldaProfiler.StartSession(ProfilingOptions.Disabled /*__MALDA_PROFILING_OPTIONS__*/, "program.malda" /*__MALDA_PROFILING_SESSION__*/);
            
            // Parse and execute
            var lexer = new Lexer(source, "program.malda");
            var tokens = lexer.Tokenize();
            
            var parser = new MaldaLang.Parser.Parser(tokens, "program.malda");
            var statements = parser.Parse();
            if (parser.Errors.Count > 0)
                throw parser.Errors[0];
            
            // Create interpreter without input provider (uses Console directly)
            var interpreter = new Interpreter.Interpreter(currentFile: "program.malda");
            interpreter.SetSourceCode(source);
            // For console environment, we can use GetAwaiter().GetResult() since there's no async context
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            System.Environment.Exit(1);
        }
        finally
        {
            MaldaProfiler.CompleteSession();
            // Unsubscribe
            ModelLoadingService.OnProgressChanged -= OnModelLoadingProgress;
        }
    }
    
    private static void OnModelLoadingProgress(ModelLoadingService.ModelLoadingProgress progress)
    {
        // Write progress to stdout using \r to overwrite the same line
        var progressBar = GenerateProgressBar(progress.Percentage);
        var message = $"\r{progress.Message} {progressBar} {progress.Percentage}%";
        
        Console.Write(message);
        Console.Out.Flush();
        
        // If loading is complete, write a newline
        if (progress.Percentage >= 100 || !progress.IsLoading)
        {
            Console.WriteLine();
        }
    }
    
    private static string GenerateProgressBar(int percentage)
    {
        const int barWidth = 30;
        var filled = (int)(barWidth * percentage / 100.0);
        var empty = barWidth - filled;
        return $"[{new string('=', filled)}{new string(' ', empty)}]";
    }
    
    static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullResourceName = $"MaldaLang.Executable.Resources.{resourceName}";
        
        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new Exception($"Could not find embedded resource: {fullResourceName}");
        }
        
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}