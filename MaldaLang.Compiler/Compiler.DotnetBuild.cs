// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Compiler;

public partial class Compiler
{
    private string CompileExecutable(string tempDir, string outputPath, bool includeLLamaSharp = false, bool includeOptionalPacks = false)
    {
        var requestedOutputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(requestedOutputDir))
        {
            requestedOutputDir = tempDir;
        }
        var publishOutputDir = Path.Combine(tempDir, "publish");
        Directory.CreateDirectory(publishOutputDir);
        var csprojPath = Path.Combine(tempDir, "MaldaLang.Executable.csproj");
        // Optional native trading/timeseries packs need companion assemblies beside the executable.
        var shouldPublishSingleFile = !includeLLamaSharp && !includeOptionalPacks && !_includeOnnxRuntime;
        var publishSingleFileArg = shouldPublishSingleFile
            ? "/p:PublishSingleFile=true"
            : "/p:PublishSingleFile=false";
        var dotnetArgs = $"publish \"{csprojPath}\" -c Release -o \"{publishOutputDir}\" {publishSingleFileArg} /p:IncludeNativeLibrariesForSelfExtract=true";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = dotnetArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ConfigureDotnetCliLanguage(processStartInfo);

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new Exception("Failed to start dotnet publish process");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            // Write build errors to disk for analysis
            var reportDir = ResolveBuildReportDirectory(outputPath, tempDir);
            var errorLogPath = Path.Combine(reportDir, "build_errors.txt");
            var programCsPath = Path.Combine(tempDir, "Program.cs");
            var generatedCsPath = Path.Combine(reportDir, "GeneratedProgram.cs");
            
            try
            {
                // The requested output directory may not exist yet when publish fails, and
                // without it the whole diagnostic report is silently lost.
                Directory.CreateDirectory(reportDir);

                // Copy the full generated Program.cs to output directory for easy inspection
                if (File.Exists(programCsPath))
                {
                    File.Copy(programCsPath, generatedCsPath, true);
                }
                
                var errorReport = $"=== DOTNET BUILD ERROR REPORT ===\n" +
                    $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Exit Code: {process.ExitCode}\n" +
                    $"Temp Directory: {tempDir}\n" +
                    $"Output Directory: {requestedOutputDir}\n" +
                    $"Command: dotnet {dotnetArgs}\n\n" +
                    $"=== FULL GENERATED C# CODE ===\n" +
                    $"The complete generated C# code has been saved to:\n" +
                    $"{generatedCsPath}\n\n" +
                    $"=== STANDARD ERROR ===\n{error}\n\n" +
                    $"=== STANDARD OUTPUT ===\n{output}\n\n" +
                    $"=== CSPROJ CONTENT ===\n";
                if (File.Exists(csprojPath))
                {
                    errorReport += File.ReadAllText(csprojPath) + "\n\n";
                }
                errorReport += $"=== PROGRAM.CS CONTENT (full) ===\n";
                if (File.Exists(programCsPath))
                {
                    var programCsContent = File.ReadAllText(programCsPath);
                    errorReport += programCsContent;
                }
                File.WriteAllText(errorLogPath, errorReport);
            }
            catch (Exception)
            {
                // Ignore errors writing error log file
            }

            throw new Exception(BuildDotnetFailureMessage("dotnet publish", error, output, generatedCsPath, errorLogPath));
        }
        
        // With PublishSingleFile=true, DLL should be embedded, but check if it exists separately
        // (fallback: copy DLL if single-file publish didn't fully embed it)
        var dllDestPath = Path.Combine(publishOutputDir, "malda.dll");
        if (!File.Exists(dllDestPath))
        {
            // DLL not found - single-file publish likely embedded it successfully
            // No need to copy DLL separately
        }
        else
        {
            // DLL exists separately - single-file publish may not have worked fully
            // Keep it for zip creation fallback
        }

        // Windows publish emits MaldaLang.Executable.exe; Unix emits an extensionless apphost.
        var exeName = Path.GetFileName(outputPath);
        if (string.IsNullOrEmpty(exeName))
        {
            exeName = OperatingSystem.IsWindows() ? "MaldaLang.Executable.exe" : "MaldaLang.Executable";
        }
        else if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exeName += ".exe";
        }

        var generatedExePath = ResolvePublishedAppHost(publishOutputDir);
        // Always honor the named -o executable, including non-single-file publishes
        // (LLamaSharp / optional packs). Companion DLLs remain beside the exe.
        var finalExePath = Path.Combine(requestedOutputDir, exeName);

        if (includeOptionalPacks)
        {
            CopyOptionalPackRuntimeDlls(publishOutputDir, includeTrading: true);
            Directory.CreateDirectory(requestedOutputDir);
            CopyOptionalPackRuntimeDlls(requestedOutputDir, includeTrading: true);
        }

        Directory.CreateDirectory(requestedOutputDir);
        foreach (var publishedFile in Directory.GetFiles(publishOutputDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(publishOutputDir, publishedFile);
            var destinationPath = Path.Combine(requestedOutputDir, relativePath);
            if (string.Equals(publishedFile, generatedExePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(destinationPath, finalExePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }
            File.Copy(publishedFile, destinationPath, true);
        }

        // A prior failed compile may have left build_errors.txt beside -o; drop it on success
        // so a shippable folder is not mistaken for a failed one.
        var staleErrorLog = Path.Combine(requestedOutputDir, "build_errors.txt");
        if (File.Exists(staleErrorLog))
        {
            try { File.Delete(staleErrorLog); } catch { /* best-effort */ }
        }
        
        if (File.Exists(generatedExePath))
        {
            if (File.Exists(finalExePath))
            {
                File.Delete(finalExePath);
            }
            File.Move(generatedExePath, finalExePath);

            // Bulk copy above may also have placed the publish apphost beside -o;
            // remove that scaffolding when the shippable name differs.
            foreach (var scaffoldingName in new[] { "MaldaLang.Executable.exe", "MaldaLang.Executable" })
            {
                var scaffoldingExe = Path.Combine(requestedOutputDir, scaffoldingName);
                if (!string.Equals(scaffoldingExe, finalExePath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(scaffoldingExe))
                {
                    try { File.Delete(scaffoldingExe); } catch { /* best-effort */ }
                }
            }

            return finalExePath;
        }

        throw new Exception(
            "dotnet publish succeeded but the apphost was missing after copy. " +
            $"Looked for: {generatedExePath}");
    }

    /// <summary>
    /// <c>dotnet publish</c> names the apphost <c>MaldaLang.Executable.exe</c> on Windows
    /// and <c>MaldaLang.Executable</c> on Linux/macOS. Returning a missing <c>.exe</c>
    /// path made transpile report success on Unix while TranspileSmokeTests saw no file.
    /// </summary>
    private static string ResolvePublishedAppHost(string publishOutputDir)
    {
        var windowsName = Path.Combine(publishOutputDir, "MaldaLang.Executable.exe");
        if (File.Exists(windowsName))
        {
            return windowsName;
        }

        var unixName = Path.Combine(publishOutputDir, "MaldaLang.Executable");
        if (File.Exists(unixName))
        {
            return unixName;
        }

        throw new Exception(
            "dotnet publish succeeded but MaldaLang.Executable(.exe) was not in the publish folder. " +
            $"Looked in: {publishOutputDir}");
    }

    private string CompileDll(string tempDir, string outputPath, bool includeLLamaSharp = false)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = tempDir;
        }
        
        // Get assembly name from output path
        var assemblyName = Path.GetFileNameWithoutExtension(outputPath);
        if (string.IsNullOrEmpty(assemblyName))
        {
            assemblyName = "MaldaLangLibrary";
        }
        
        var csprojPath = Path.Combine(tempDir, $"{assemblyName}.csproj");
        var dotnetArgs = $"build \"{csprojPath}\" -c Release -o \"{outputDir}\"";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = dotnetArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ConfigureDotnetCliLanguage(processStartInfo);

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new Exception("Failed to start dotnet build process");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            // Write build errors to disk for analysis
            var reportDir = ResolveBuildReportDirectory(outputPath, tempDir);
            var errorLogPath = Path.Combine(reportDir, "build_errors.txt");
            var programCsPath = Path.Combine(tempDir, "Program.cs");
            var generatedCsPath = Path.Combine(reportDir, "GeneratedProgram.cs");
            
            try
            {
                // The requested output directory may not exist yet when the build fails, and
                // without it the whole diagnostic report is silently lost.
                Directory.CreateDirectory(reportDir);

                // Copy the full generated Program.cs to output directory for easy inspection
                if (File.Exists(programCsPath))
                {
                    File.Copy(programCsPath, generatedCsPath, true);
                }
                
                var errorReport = $"=== DOTNET BUILD ERROR REPORT ===\n" +
                    $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Exit Code: {process.ExitCode}\n" +
                    $"Temp Directory: {tempDir}\n" +
                    $"Output Directory: {outputDir}\n" +
                    $"Command: dotnet {dotnetArgs}\n\n" +
                    $"=== FULL GENERATED C# CODE ===\n" +
                    $"The complete generated C# code has been saved to:\n" +
                    $"{generatedCsPath}\n\n" +
                    $"=== STANDARD ERROR ===\n{error}\n\n" +
                    $"=== STANDARD OUTPUT ===\n{output}\n\n" +
                    $"=== CSPROJ CONTENT ===\n";
                if (File.Exists(csprojPath))
                {
                    errorReport += File.ReadAllText(csprojPath) + "\n\n";
                }
                errorReport += $"=== PROGRAM.CS CONTENT (full) ===\n";
                if (File.Exists(programCsPath))
                {
                    var programCsContent = File.ReadAllText(programCsPath);
                    errorReport += programCsContent;
                }
                File.WriteAllText(errorLogPath, errorReport);
            }
            catch (Exception)
            {
                // Ignore errors writing error log file
            }

            throw new Exception(BuildDotnetFailureMessage("dotnet build", error, output, generatedCsPath, errorLogPath));
        }
        
        // Find the generated DLL
        var generatedDllPath = Path.Combine(outputDir, $"{assemblyName}.dll");
        
        if (!File.Exists(generatedDllPath))
        {
            throw new Exception($"DLL not found at expected path: {generatedDllPath}");
        }

        var staleErrorLog = Path.Combine(outputDir, "build_errors.txt");
        if (File.Exists(staleErrorLog))
        {
            try { File.Delete(staleErrorLog); } catch { /* best-effort */ }
        }
        
        // Normalize paths for comparison
        var normalizedOutputPath = Path.GetFullPath(outputPath);
        var normalizedGeneratedPath = Path.GetFullPath(generatedDllPath);
        
        // If paths are the same, return the requested outputPath
        if (normalizedOutputPath.Equals(normalizedGeneratedPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedOutputPath;
        }
        
        // Paths are different - move the generated DLL to the requested output path
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }
        File.Move(generatedDllPath, outputPath);
        return Path.GetFullPath(outputPath);
    }
}
