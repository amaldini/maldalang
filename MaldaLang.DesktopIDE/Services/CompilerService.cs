// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.IO.Compression;
using MaldaLang.Compiler;

namespace MaldaLang.DesktopIDE.Services;

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

    public async Task<Compiler.Compiler.CompilationResult> CompileAsync(
        string sourcePath,
        string outputPath,
        Compiler.CompilationMode mode = Compiler.CompilationMode.Interpreter,
        bool includeLLamaSharp = false,
        CancellationToken cancellationToken = default)
    {
        return await _compilerService.CompileAsync(sourcePath, outputPath, mode, includeLLamaSharp, cancellationToken);
    }

    public async Task<Compiler.Compiler.CompilationResult> CompileFromTextAsync(
        string sourceText,
        string outputPath,
        Compiler.CompilationMode mode = Compiler.CompilationMode.Interpreter,
        bool includeLLamaSharp = false,
        CancellationToken cancellationToken = default)
    {
        // Create temporary file for source
        var tempFile = Path.Combine(Path.GetTempPath(), $"spl_temp_{Guid.NewGuid()}.malda");
        try
        {
            File.WriteAllText(tempFile, sourceText);
            return await _compilerService.CompileAsync(tempFile, outputPath, mode, includeLLamaSharp, cancellationToken);
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

    public string? CreateZipWithDependencies(string exePath, string zipPath)
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
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // Add exe
                archive.CreateEntryFromFile(exePath, exeName);

                // Add DLL
                archive.CreateEntryFromFile(dllPath, "MaldaLang.dll");
            }

            return zipPath;
        }
        catch
        {
            return null;
        }
    }
}