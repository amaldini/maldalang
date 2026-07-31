// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

public class CompilerService
{
    public class CompilationProgress
    {
        public string Message { get; set; } = string.Empty;
        public int Percentage { get; set; }
    }

    public delegate void ProgressHandler(CompilationProgress progress);

    public event ProgressHandler? OnProgress;

    public async Task<Compiler.CompilationResult> CompileAsync(
        string sourcePath, 
        string outputPath,
        CompilationMode mode = CompilationMode.Interpreter,
        bool includeLLamaSharp = false,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var compiler = new Compiler();
            
            if (mode == CompilationMode.TranspileToCSharp)
            {
                ReportProgress("Validating source code...", 10);
                cancellationToken.ThrowIfCancellationRequested();
                
                ReportProgress("Transpiling to C#...", 30);
                cancellationToken.ThrowIfCancellationRequested();
                
                ReportProgress("Generating project files...", 50);
                cancellationToken.ThrowIfCancellationRequested();
                
                ReportProgress("Compiling executable...", 70);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                ReportProgress("Validating source code...", 10);
                cancellationToken.ThrowIfCancellationRequested();
                
                ReportProgress("Generating project files...", 30);
                cancellationToken.ThrowIfCancellationRequested();
                
                ReportProgress("Compiling executable...", 50);
                cancellationToken.ThrowIfCancellationRequested();
            }
            
            var result = compiler.Compile(sourcePath, outputPath, mode, includeLLamaSharp);
            
            if (result.Success)
            {
                ReportProgress("Compilation completed successfully!", 100);
            }
            else
            {
                ReportProgress($"Compilation failed: {result.ErrorMessage}", 100);
            }
            
            return result;
        }, cancellationToken);
    }

    private void ReportProgress(string message, int percentage)
    {
        OnProgress?.Invoke(new CompilationProgress
        {
            Message = message,
            Percentage = percentage
        });
    }
}