// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.Compiler;

namespace MaldaLang.IDE.Services;

public class CompilerService
{
    private readonly Compiler.CompilerService _compilerService;

    public CompilerService()
    {
        _compilerService = new Compiler.CompilerService();
    }

    public event Compiler.CompilerService.ProgressHandler? OnProgress
    {
        add => _compilerService.OnProgress += value;
        remove => _compilerService.OnProgress -= value;
    }

    public async Task<CompilationResult> CompileAsync(
        string sourceText,
        string outputFileName,
        Compiler.CompilationMode mode = Compiler.CompilationMode.Interpreter,
        CancellationToken cancellationToken = default)
    {
        // Create temporary file for source
        var tempFile = Path.Combine(Path.GetTempPath(), $"spl_temp_{Guid.NewGuid()}.malda");
        var outputPath = Path.Combine(Path.GetTempPath(), outputFileName);
        
        try
        {
            File.WriteAllText(tempFile, sourceText);
            var result = await _compilerService.CompileAsync(tempFile, outputPath, mode, includeLLamaSharp: false, cancellationToken);
            
            return new CompilationResult
            {
                Success = result.Success,
                OutputPath = result.OutputPath,
                ErrorMessage = result.ErrorMessage,
                Errors = result.Success ? new List<string>() : new List<string> { result.ErrorMessage ?? "Unknown error" }
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                OutputPath = null,
                ErrorMessage = ex.Message,
                Errors = new List<string> { ex.Message }
            };
        }
        finally
        {
            // Cleanup temp file
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public byte[]? ReadCompiledExecutable(string filePath)
    {
        if (File.Exists(filePath))
        {
            return File.ReadAllBytes(filePath);
        }
        return null;
    }

    public byte[]? CreateZipWithDependencies(string exePath)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
                return null;

            var exeName = Path.GetFileName(exePath);
            var dllPath = Path.Combine(exeDir, "MaldaLang.dll");
            
            // Check if DLL exists separately (not embedded)
            // With PublishSingleFile=true, DLL should be embedded, so we only create zip if DLL is separate
            if (!File.Exists(dllPath))
            {
                // DLL is embedded in exe (single-file publish worked), no zip needed
                return null;
            }

            // DLL exists separately, create zip with both files
            using var memoryStream = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                // Add exe
                var exeEntry = archive.CreateEntry(exeName);
                using (var entryStream = exeEntry.Open())
                using (var fileStream = File.OpenRead(exePath))
                {
                    fileStream.CopyTo(entryStream);
                }

                // Add DLL
                var dllEntry = archive.CreateEntry("MaldaLang.dll");
                using (var entryStream = dllEntry.Open())
                using (var fileStream = File.OpenRead(dllPath))
                {
                    fileStream.CopyTo(entryStream);
                }
            }

            return memoryStream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}

public class CompilationResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Errors { get; set; } = new();
}