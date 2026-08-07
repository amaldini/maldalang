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
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Compiler;

public enum CompilationMode
{
    Interpreter,
    TranspileToCSharp,
    TranspileToDll,
    JavaScript,
    PWA,
    FullStack
}

public class Compiler
{
    private const string EmbeddedUiHostStartMarker = "RegisterDecoratedFunctions();";
    private static readonly Regex EmbedAliasPattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public class CompilationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? OutputPath { get; set; }
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode = CompilationMode.Interpreter, bool includeLLamaSharp = false)
    {
        return Compile(sourcePath, outputPath, mode, includeLLamaSharp, includeUiHost: false, profilingOptions: null, typedTranspileLevel: 1);
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode, bool includeLLamaSharp, bool includeUiHost)
    {
        return Compile(sourcePath, outputPath, mode, includeLLamaSharp, includeUiHost, profilingOptions: null, typedTranspileLevel: 1);
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode, bool includeLLamaSharp, bool includeUiHost, ProfilingOptions? profilingOptions)
    {
        return Compile(sourcePath, outputPath, mode, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel: 1);
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode, bool includeLLamaSharp, bool includeUiHost, ProfilingOptions? profilingOptions, int typedTranspileLevel)
    {
        return Compile(sourcePath, outputPath, mode, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel, includeOptionalPacks: false);
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode, bool includeLLamaSharp, bool includeUiHost, ProfilingOptions? profilingOptions, int typedTranspileLevel, bool includeOptionalPacks)
    {
        return Compile(sourcePath, outputPath, mode, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel, includeOptionalPacks, embedFolderArgs: null);
    }

    /// <summary>
    /// <paramref name="embedFolderArgs"/> entries are <c>path</c> or <c>path=alias</c>.
    /// </summary>
    /// <summary>
    /// True when the MALDA source (or a packed ASK brain script) needs LLamaSharp
    /// native backends at publish time — e.g. <c>new LlamaEmbedder(...)</c>.
    /// Without this, portable exes find a .gguf beside the exe but fall back to hash
    /// because the llama.cpp backend DLLs were never copied into the publish folder.
    /// </summary>
    public static bool SourceRequiresLLamaSharp(string source)
    {
        if (string.IsNullOrEmpty(source))
            return false;
        return source.Contains("LlamaEmbedder", StringComparison.Ordinal);
    }

    public CompilationResult Compile(string sourcePath, string outputPath, CompilationMode mode, bool includeLLamaSharp, bool includeUiHost, ProfilingOptions? profilingOptions, int typedTranspileLevel, bool includeOptionalPacks, string[]? embedFolderArgs)
    {
        IReadOnlyList<EmbeddedFolderSpec> folders;
        try
        {
            folders = NormalizeEmbedFolders(ParseEmbedFolderArgs(embedFolderArgs));
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        if (!includeLLamaSharp && File.Exists(sourcePath))
        {
            try
            {
                if (SourceRequiresLLamaSharp(File.ReadAllText(sourcePath)))
                    includeLLamaSharp = true;
            }
            catch
            {
                // Keep caller-provided flag when the source cannot be probed.
            }
        }

        if (mode == CompilationMode.TranspileToCSharp)
        {
            return CompileWithTranspilation(sourcePath, outputPath, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel, includeOptionalPacks, folders);
        }
        
        if (mode == CompilationMode.TranspileToDll)
        {
            return CompileToDll(sourcePath, outputPath, includeLLamaSharp);
        }

        if (mode == CompilationMode.JavaScript)
        {
            return CompileToJavaScript(sourcePath, outputPath);
        }

        if (mode == CompilationMode.PWA)
        {
            return CompileToPwa(sourcePath, outputPath);
        }

        if (mode == CompilationMode.FullStack)
        {
            return CompileToFullStack(sourcePath, outputPath, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel);
        }

        // Default: Interpreter mode (existing behavior)
        try
        {
            // Validate source code
            var source = File.ReadAllText(sourcePath);
            ValidateSource(source, sourcePath);

            // Create temporary directory for generated project
            var tempDir = Path.Combine(Path.GetTempPath(), $"spl_compile_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Generate project files
                GenerateProjectFiles(tempDir, source, sourcePath, includeLLamaSharp, profilingOptions, folders);

                // Compile executable
                var exePath = CompileExecutable(tempDir, outputPath, includeLLamaSharp);

                return new CompilationResult
                {
                    Success = true,
                    OutputPath = exePath
                };
            }
            finally
            {
                // Cleanup temporary directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (ParseException ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Syntax error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Compilation error: {ex.Message}"
            };
        }
    }

    public class ValidationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public ValidationResult Validate(string source)
    {
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            
            var parser = new MaldaLang.Parser.Parser(tokens);
            var statements = parser.Parse(); // This will collect errors in parser.Errors
            
            if (parser.Errors.Count > 0)
            {
                var errors = parser.Errors.Select(e => e.Message).ToList();
                return new ValidationResult
                {
                    Success = false,
                    ErrorMessage = errors[0],
                    Errors = errors
                };
            }

            var targetDiagnostics = TargetPartitioner.Validate(statements);
            if (targetDiagnostics.Count > 0)
            {
                var errors = targetDiagnostics.Select(diagnostic => diagnostic.ToString()).ToList();
                return new ValidationResult
                {
                    Success = false,
                    ErrorMessage = errors[0],
                    Errors = errors
                };
            }
            
            return new ValidationResult
            {
                Success = true
            };
        }
        catch (ParseException ex)
        {
            return new ValidationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Errors = new List<string> { ex.Message }
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private void ValidateSource(string source, string? sourceFilePath = null)
    {
        var lexer = new Lexer(source, sourceFilePath);
        var tokens = lexer.Tokenize();
        
        var parser = new MaldaLang.Parser.Parser(tokens, sourceFilePath);
        var statements = parser.Parse(); // This will collect errors in parser.Errors
        
        if (parser.Errors.Count > 0)
        {
            var firstError = parser.Errors[0];
            throw firstError; // Throw the first error for compatibility
        }

        ThrowIfTargetPartitionDiagnostics(TargetPartitioner.Validate(statements));
    }

    private static string BuildDotnetFailureMessage(
        string action,
        string error,
        string output,
        string generatedCsPath,
        string? errorLogPath = null)
    {
        var combined = $"{error}\n{output}".Trim();
        var normalized = TryExtractMaldaCompilerMessage(combined);
        var paths = FormatGeneratedArtifactPaths(generatedCsPath, errorLogPath);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return $"{action} failed: {normalized}\n{paths}";
        }

        return $"{action} failed:\n{error}\n{output}\n\n{paths}";
    }

    private static string FormatGeneratedArtifactPaths(string generatedCsPath, string? errorLogPath)
    {
        var paths = $"Generated C# saved to: {generatedCsPath}";
        if (!string.IsNullOrWhiteSpace(errorLogPath))
        {
            paths += $"\nBuild errors written to: {errorLogPath}";
        }

        return paths;
    }

    private static string ResolveBuildReportDirectory(string outputPath, string fallbackDir)
    {
        var reportDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(reportDir))
        {
            reportDir = fallbackDir;
        }

        return Path.GetFullPath(reportDir);
    }

    private static void ConfigureDotnetCliLanguage(ProcessStartInfo processStartInfo)
    {
        // Agents and our error parsers match on English ": error " / CS#### tokens.
        // Without this, a non-English OS locale localizes Roslyn text and breaks extraction.
        processStartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        processStartInfo.Environment["VSLANG"] = "1033";
    }

    private static string? TryExtractMaldaCompilerMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        // Warnings carry the same file(line,col) shape as errors, so only errors may be
        // chosen here: a warning appearing first would otherwise hide the diagnostics that
        // actually failed the build.
        var errorLines = lines
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maldaLocation = new Regex("(?<file>[^:(]+\\.malda)\\((?<line>\\d+)(,(?<column>\\d+))?\\)", RegexOptions.IgnoreCase);
        foreach (var line in errorLines)
        {
            var locationMatch = maldaLocation.Match(line);
            if (!locationMatch.Success)
            {
                continue;
            }

            var file = locationMatch.Groups["file"].Value;
            var lineNumber = locationMatch.Groups["line"].Value;
            return AppendRemainingErrorCount(
                $"{StripErrorMarker(line)} ({file}:{lineNumber})",
                errorLines.Count);
        }

        if (errorLines.Count > 0)
        {
            return AppendRemainingErrorCount(StripErrorMarker(errorLines[0]), errorLines.Count);
        }

        return lines.FirstOrDefault();
    }

    private static string StripErrorMarker(string line)
    {
        var index = line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? line[(index + 8)..].Trim() : line.Trim();
    }

    private static string AppendRemainingErrorCount(string message, int errorCount)
    {
        if (errorCount <= 1)
        {
            return message;
        }

        var remaining = errorCount - 1;
        return $"{message} (+{remaining} more error{(remaining == 1 ? "" : "s")}; full list in build_errors.txt next to -o)";
    }

    private static IReadOnlyList<EmbeddedFolderSpec> ParseEmbedFolderArgs(string[]? embedFolderArgs)
    {
        if (embedFolderArgs == null || embedFolderArgs.Length == 0)
        {
            return Array.Empty<EmbeddedFolderSpec>();
        }

        var list = new List<EmbeddedFolderSpec>();
        foreach (var raw in embedFolderArgs)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var eq = raw.LastIndexOf('=');
            if (eq > 0)
            {
                list.Add(new EmbeddedFolderSpec(raw.Substring(0, eq), raw.Substring(eq + 1)));
            }
            else
            {
                list.Add(new EmbeddedFolderSpec(raw, ""));
            }
        }

        return list;
    }

    private static IReadOnlyList<EmbeddedFolderSpec> NormalizeEmbedFolders(IReadOnlyList<EmbeddedFolderSpec>? embedFolders)
    {
        if (embedFolders == null || embedFolders.Count == 0)
        {
            return Array.Empty<EmbeddedFolderSpec>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<EmbeddedFolderSpec>();
        foreach (var spec in embedFolders)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Path))
            {
                throw new Exception("--embed-folder requires a directory path.");
            }

            var fullPath = Path.GetFullPath(spec.Path);
            if (!Directory.Exists(fullPath))
            {
                throw new Exception($"--embed-folder directory not found: {spec.Path}");
            }

            var alias = string.IsNullOrWhiteSpace(spec.Alias)
                ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : spec.Alias.Trim();
            if (string.IsNullOrEmpty(alias) || !EmbedAliasPattern.IsMatch(alias))
            {
                throw new Exception($"Invalid --embed-folder alias '{alias}'. Use letters, digits, '_' or '-'.");
            }

            if (!seen.Add(alias))
            {
                throw new Exception($"Duplicate --embed-folder alias '{alias}'.");
            }

            normalized.Add(new EmbeddedFolderSpec(fullPath, alias));
        }

        return normalized;
    }

    private static void StageEmbeddedFolders(string tempDir, IReadOnlyList<EmbeddedFolderSpec> embedFolders)
    {
        if (embedFolders.Count == 0)
        {
            return;
        }

        var foldersRoot = Path.Combine(tempDir, "Resources", "folders");
        Directory.CreateDirectory(foldersRoot);
        foreach (var spec in embedFolders)
        {
            var destRoot = Path.Combine(foldersRoot, spec.Alias);
            Directory.CreateDirectory(destRoot);
            foreach (var file in Directory.GetFiles(spec.Path, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(spec.Path, file);
                var destPath = Path.Combine(destRoot, relative);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, destPath, overwrite: true);
            }
        }
    }

    private static string BuildEmbeddedFolderResourceItems(string tempDir)
    {
        var foldersDir = Path.Combine(tempDir, "Resources", "folders");
        if (!Directory.Exists(foldersDir))
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var file in Directory.GetFiles(foldersDir, "*", SearchOption.AllDirectories))
        {
            var relativeFromResources = Path.GetRelativePath(Path.Combine(tempDir, "Resources"), file)
                .Replace('/', '\\');
            var relativeFromFolders = Path.GetRelativePath(foldersDir, file).Replace('\\', '/');
            var slash = relativeFromFolders.IndexOf('/');
            if (slash <= 0)
            {
                continue;
            }

            var alias = relativeFromFolders.Substring(0, slash);
            var relativeFile = relativeFromFolders.Substring(slash + 1);
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(relativeFile))
            {
                continue;
            }

            var logicalName = $"malda.embed.{alias}/{relativeFile}";
            sb.AppendLine($"    <EmbeddedResource Include=\"Resources\\{relativeFromResources}\">");
            sb.AppendLine($"      <LogicalName>{logicalName}</LogicalName>");
            sb.AppendLine("    </EmbeddedResource>");
        }

        return sb.ToString();
    }

    private void GenerateProjectFiles(string tempDir, string source, string sourcePath, bool includeLLamaSharp = false, ProfilingOptions? profilingOptions = null, IReadOnlyList<EmbeddedFolderSpec>? embedFolders = null)
    {
        // Create Resources directory
        var resourcesDir = Path.Combine(tempDir, "Resources");
        Directory.CreateDirectory(resourcesDir);

        // Copy source file to Resources
        var resourceFileName = "program.malda";
        var resourcePath = Path.Combine(resourcesDir, resourceFileName);
        File.WriteAllText(resourcePath, source, Encoding.UTF8);
        StageEmbeddedFolders(tempDir, embedFolders ?? Array.Empty<EmbeddedFolderSpec>());
        
        // Analyze and bundle package dependencies
        try
        {
            var analyzer = new DependencyAnalyzer();
            var dependencies = analyzer.AnalyzeDependencies(sourcePath);
            var allDependencies = analyzer.GetAllDependencies(dependencies);
            
            // Create packages directory in resources
            var packagesDir = Path.Combine(resourcesDir, "packages");
            Directory.CreateDirectory(packagesDir);
            
            // Copy package source files
            foreach (var dep in allDependencies)
            {
                if (dep.ModulePath != null && File.Exists(dep.ModulePath))
                {
                    var packageDir = Path.Combine(packagesDir, dep.PackageName, dep.Version);
                    Directory.CreateDirectory(packageDir);
                    
                    // Copy the module file
                    var moduleFileName = Path.GetFileName(dep.ModulePath);
                    var destModulePath = Path.Combine(packageDir, moduleFileName);
                    File.Copy(dep.ModulePath, destModulePath, true);
                    
                    // Copy package.json if available
                    if (dep.Metadata != null)
                    {
                        var packageStorage = new MaldaLang.PackageManager.PackageStorage();
                        var packageJsonPath = packageStorage.GetPackageJsonPath(dep.PackageName, dep.Version);
                        if (File.Exists(packageJsonPath))
                        {
                            var destPackageJson = Path.Combine(packageDir, "package.json");
                            File.Copy(packageJsonPath, destPackageJson, true);
                        }
                    }
                }
            }
        }
        catch
        {
            // If package analysis fails, continue without packages
            // This allows compilation to work even if packages aren't installed
        }

        // Find and copy MaldaLang.dll to temp directory for reference
        var MaldaLangDllPath = FindMaldaLangDll();
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            var dllDestPath = Path.Combine(tempDir, "malda.dll");
            File.Copy(MaldaLangDllPath, dllDestPath, true);
        }

        // Generate .csproj file (pass tempDir for relative path calculation)
        var csprojPath = Path.Combine(tempDir, "MaldaLang.Executable.csproj");
        var csprojContent = GenerateCsprojContent(tempDir, MaldaLangDllPath, includeLLamaSharp);
        File.WriteAllText(csprojPath, csprojContent);

        // Generate Program.cs from template
        var programCsPath = Path.Combine(tempDir, "Program.cs");
        
        // Try to find template file relative to compiler assembly
        var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
        var templatePath = compilerLocation != null 
            ? Path.Combine(compilerLocation, "Templates", "ExecutableTemplate.cs")
            : null;
        
        string programCsContent;
        if (templatePath != null && File.Exists(templatePath))
        {
            programCsContent = File.ReadAllText(templatePath);
        }
        else
        {
            // Use embedded template if file not found
            programCsContent = GetEmbeddedTemplate();
        }

        programCsContent = InjectProfilingOptionsIntoTemplate(programCsContent, profilingOptions, sourcePath);
        File.WriteAllText(programCsPath, programCsContent);
    }

    private string? FindMaldaLangDll()
    {
        var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
        
        // List of locations to check (try both malda.dll and MaldaLang.dll for compatibility)
        var searchPaths = new List<string>();
        
        // 1. Current working directory
        searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "malda.dll"));
        searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "MaldaLang.dll"));
        
        // 2. Current directory's bin folders (prefer Debug to match default `dotnet build`)
        var currentDir = Environment.CurrentDirectory;
        searchPaths.Add(Path.Combine(currentDir, "bin", "Debug", "net8.0", "malda.dll"));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Release", "net8.0", "malda.dll"));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Debug", "net8.0", "MaldaLang.dll"));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Release", "net8.0", "MaldaLang.dll"));
        
        // 3. Try to find it by going up from compiler location
        if (compilerLocation != null)
        {
            var compilerDir = new DirectoryInfo(compilerLocation);
            while (compilerDir != null)
            {
                var debugDll = Path.Combine(compilerDir.FullName, "MaldaLang", "bin", "Debug", "net8.0", "malda.dll");
                var releaseDll = Path.Combine(compilerDir.FullName, "MaldaLang", "bin", "Release", "net8.0", "malda.dll");
                var debugDllOld = Path.Combine(compilerDir.FullName, "MaldaLang", "bin", "Debug", "net8.0", "MaldaLang.dll");
                var releaseDllOld = Path.Combine(compilerDir.FullName, "MaldaLang", "bin", "Release", "net8.0", "MaldaLang.dll");
                searchPaths.Add(debugDll);
                searchPaths.Add(releaseDll);
                searchPaths.Add(debugDllOld);
                searchPaths.Add(releaseDllOld);
                compilerDir = compilerDir.Parent;
            }
        }
        
        // 4. Try to find it by going up from current directory
        var currentDirInfo = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirInfo != null)
        {
            var debugDll = Path.Combine(currentDirInfo.FullName, "MaldaLang", "bin", "Debug", "net8.0", "malda.dll");
            var releaseDll = Path.Combine(currentDirInfo.FullName, "MaldaLang", "bin", "Release", "net8.0", "malda.dll");
            var debugDllOld = Path.Combine(currentDirInfo.FullName, "MaldaLang", "bin", "Debug", "net8.0", "MaldaLang.dll");
            var releaseDllOld = Path.Combine(currentDirInfo.FullName, "MaldaLang", "bin", "Release", "net8.0", "MaldaLang.dll");
            searchPaths.Add(debugDll);
            searchPaths.Add(releaseDll);
            searchPaths.Add(debugDllOld);
            searchPaths.Add(releaseDllOld);
            currentDirInfo = currentDirInfo.Parent;
        }
        
        // 5. Same directory as compiler (keep this last to avoid stale Debug runtime picks)
        if (compilerLocation != null)
        {
            searchPaths.Add(Path.Combine(compilerLocation, "malda.dll"));
            searchPaths.Add(Path.Combine(compilerLocation, "MaldaLang.dll"));
        }

        // Check all paths
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        
        return null;
    }

    private string? FindPackDll(string fileName)
    {
        var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
        var searchPaths = new List<string>();
        var currentDir = Environment.CurrentDirectory;
        searchPaths.Add(Path.Combine(AppContext.BaseDirectory, fileName));
        searchPaths.Add(Path.Combine(currentDir, fileName));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Debug", "net8.0", fileName));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Release", "net8.0", fileName));

        if (compilerLocation != null)
        {
            var compilerDir = new DirectoryInfo(compilerLocation);
            while (compilerDir != null)
            {
                searchPaths.Add(Path.Combine(compilerDir.FullName, fileName));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "bin", "Debug", "net8.0", fileName));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "bin", "Release", "net8.0", fileName));
                compilerDir = compilerDir.Parent;
            }
        }

        var currentDirInfo = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirInfo != null)
        {
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, fileName));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "bin", "Debug", "net8.0", fileName));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "bin", "Release", "net8.0", fileName));
            currentDirInfo = currentDirInfo.Parent;
        }

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private void CopyPackDllToDirectory(string? dllPath, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            return;

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(dllPath, Path.Combine(destinationDirectory, Path.GetFileName(dllPath)), true);
    }

    private void StageTranspilePackReferences(string tempDir, out string extraProjectReferences)
    {
        var references = new StringBuilder();
        foreach (var fileName in new[]
                 {
                     "MaldaLang.Timeseries.dll",
                     "MaldaLang.Trading.Core.dll"
                 })
        {
            var sourcePath = FindPackDll(fileName);
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            CopyPackDllToDirectory(sourcePath, tempDir);
            var localPath = Path.Combine(tempDir, fileName);
            var assemblyName = Path.GetFileNameWithoutExtension(fileName);
            references.AppendLine($@"      <Reference Include=""{assemblyName}"">");
            references.AppendLine($@"        <HintPath>{localPath}</HintPath>");
            references.AppendLine(@"        <Private>True</Private>");
            references.AppendLine(@"        <CopyLocal>True</CopyLocal>");
            references.AppendLine(@"      </Reference>");
        }

        extraProjectReferences = references.Length == 0
            ? string.Empty
            : $"    <ItemGroup>{Environment.NewLine}{references}    </ItemGroup>{Environment.NewLine}";
    }

    private void CopyOptionalPackRuntimeDlls(string outputDirectory, bool includeTrading)
    {
        CopyPackDllToDirectory(FindPackDll("MaldaLang.Timeseries.dll"), outputDirectory);
        CopyPackDllToDirectory(FindPackDll("MaldaLang.Trading.Core.dll"), outputDirectory);
        if (!includeTrading)
            return;

        CopyPackDllToDirectory(FindTradingPluginDll(), outputDirectory);
        CopyPackDllToDirectory(FindTradingAbstractionsDll(), outputDirectory);
    }

    private string? FindTradingPluginDll()
    {
        var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
        var searchPaths = new List<string>();

        var envPath = Environment.GetEnvironmentVariable("MALDA_TRADING_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        var currentDir = Environment.CurrentDirectory;
        searchPaths.Add(Path.Combine(currentDir, "bin", "Debug", "net8.0", "MaldaLang.Trading.Plugin.dll"));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Release", "net8.0", "MaldaLang.Trading.Plugin.dll"));
        searchPaths.Add(Path.Combine(currentDir, "MaldaLang.Trading.Plugin", "bin", "Debug", "net8.0", "MaldaLang.Trading.Plugin.dll"));
        searchPaths.Add(Path.Combine(currentDir, "MaldaLang.Trading.Plugin", "bin", "Release", "net8.0", "MaldaLang.Trading.Plugin.dll"));
        searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "MaldaLang.Trading.Plugin.dll"));

        if (compilerLocation != null)
        {
            var compilerDir = new DirectoryInfo(compilerLocation);
            while (compilerDir != null)
            {
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Plugin", "bin", "Debug", "net8.0", "MaldaLang.Trading.Plugin.dll"));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Plugin", "bin", "Release", "net8.0", "MaldaLang.Trading.Plugin.dll"));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Plugin.dll"));
                compilerDir = compilerDir.Parent;
            }
        }

        var currentDirInfo = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirInfo != null)
        {
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Plugin", "bin", "Debug", "net8.0", "MaldaLang.Trading.Plugin.dll"));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Plugin", "bin", "Release", "net8.0", "MaldaLang.Trading.Plugin.dll"));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Plugin.dll"));
            currentDirInfo = currentDirInfo.Parent;
        }

        return searchPaths
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}MaldaLang.Trading.Plugin{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private string? FindTradingAbstractionsDll()
    {
        var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
        var searchPaths = new List<string>();

        var currentDir = Environment.CurrentDirectory;
        searchPaths.Add(Path.Combine(currentDir, "bin", "Debug", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
        searchPaths.Add(Path.Combine(currentDir, "bin", "Release", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
        searchPaths.Add(Path.Combine(currentDir, "MaldaLang.Trading.Abstractions", "bin", "Debug", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
        searchPaths.Add(Path.Combine(currentDir, "MaldaLang.Trading.Abstractions", "bin", "Release", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
        var envPath = Environment.GetEnvironmentVariable("MALDA_TRADING_ABSTRACTIONS_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        searchPaths.Add(Path.Combine(Environment.CurrentDirectory, "MaldaLang.Trading.Abstractions.dll"));

        if (compilerLocation != null)
        {
            var compilerDir = new DirectoryInfo(compilerLocation);
            while (compilerDir != null)
            {
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Abstractions", "bin", "Debug", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Abstractions", "bin", "Release", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
                searchPaths.Add(Path.Combine(compilerDir.FullName, "MaldaLang.Trading.Abstractions.dll"));
                compilerDir = compilerDir.Parent;
            }
        }

        var currentDirInfo = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirInfo != null)
        {
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Abstractions", "bin", "Debug", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Abstractions", "bin", "Release", "net8.0", "MaldaLang.Trading.Abstractions.dll"));
            searchPaths.Add(Path.Combine(currentDirInfo.FullName, "MaldaLang.Trading.Abstractions.dll"));
            currentDirInfo = currentDirInfo.Parent;
        }

        return searchPaths
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}MaldaLang.Trading.Abstractions{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private string GenerateCsprojContent(string tempDir, string? MaldaLangDllPath, bool includeLLamaSharp = false)
    {
        string projectReference;
        
        // Prefer DLL reference if available (more reliable from temp directories)
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            // Use local copy in temp directory
            var localDllPath = Path.Combine(tempDir, "malda.dll");
            projectReference = $@"    <ItemGroup>
      <Reference Include=""MaldaLang"">
        <HintPath>{localDllPath}</HintPath>
        <Private>True</Private>
        <CopyLocal>True</CopyLocal>
      </Reference>
    </ItemGroup>";
        }
        else
        {
            // Try to find MaldaLang.csproj relative to compiler assembly location
            var compilerAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var compilerLocation = Path.GetDirectoryName(compilerAssembly.Location);
            
            string? MaldaLangProjectPath = null;
            
            if (compilerLocation != null)
            {
                var currentDir = new DirectoryInfo(compilerLocation);
                
                // Go up from bin/Debug/net8.0 to find solution directory
                while (currentDir != null)
                {
                    var testPath = Path.Combine(currentDir.FullName, "MaldaLang", "MaldaLang.csproj");
                    if (File.Exists(testPath))
                    {
                        MaldaLangProjectPath = testPath;
                        break;
                    }
                    
                    // Also check if we're at the solution root
                    var solutionPath = Path.Combine(currentDir.FullName, "MaldaLang.sln");
                    if (File.Exists(solutionPath))
                    {
                        testPath = Path.Combine(currentDir.FullName, "MaldaLang", "MaldaLang.csproj");
                        if (File.Exists(testPath))
                        {
                            MaldaLangProjectPath = testPath;
                            break;
                        }
                    }
                    
                    currentDir = currentDir.Parent;
                }
            }
            
            // Cannot use project reference because MaldaLang.csproj is a self-contained executable
            // Throw an error instead
            throw new Exception("malda.dll not found. Please ensure MaldaLang project is built and the DLL is available. " +
                "The DLL should be in the same directory as the compiler, or in MaldaLang/bin/Debug/net8.0 or MaldaLang/bin/Release/net8.0");
        }
        
        string packageReferences = @"  <ItemGroup>
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        if (includeLLamaSharp)
        {
            packageReferences = @"  <ItemGroup>
    <PackageReference Include=""LLamaSharp"" Version=""0.26.0"" />
    <PackageReference Include=""LLamaSharp.Backend.Cpu"" Version=""0.26.0"" />
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        }
        
        // Disable single-file publishing when LLamaSharp is included to avoid conflicts
        // with multiple native DLLs (avx, avx2, avx512, noavx variants)
        string publishSingleFile = includeLLamaSharp ? "false" : "true";
        
        // Generate embedded resource items for all package files
        var embeddedResources = new StringBuilder();
        embeddedResources.AppendLine("  <ItemGroup>");
        embeddedResources.AppendLine("    <EmbeddedResource Include=\"Resources\\program.malda\" />");
        
        // Include all package files as embedded resources
        var packagesDir = Path.Combine(tempDir, "Resources", "packages");
        if (Directory.Exists(packagesDir))
        {
            var resourcesBaseDir = Path.Combine(tempDir, "Resources");
            var packageFiles = Directory.GetFiles(packagesDir, "*", SearchOption.AllDirectories);
            foreach (var file in packageFiles)
            {
                var relativePath = Path.GetRelativePath(resourcesBaseDir, file);
                // Normalize path separators for .csproj (use backslashes)
                var normalizedPath = relativePath.Replace('/', '\\');
                embeddedResources.AppendLine($"    <EmbeddedResource Include=\"Resources\\{normalizedPath}\" />");
            }
        }

        embeddedResources.Append(BuildEmbeddedFolderResourceItems(tempDir));
        
        embeddedResources.AppendLine("  </ItemGroup>");
        
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MaldaLang.Executable</RootNamespace>
    <PublishSingleFile>{publishSingleFile}</PublishSingleFile>
    <SelfContained>false</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <NoWarn>$(NoWarn);CS1998;CS8600;CS8602;CS8603;CS8604;CS8605;CS8618;CS8625;CS8629</NoWarn>
  </PropertyGroup>
  {projectReference}
  {packageReferences}
{embeddedResources}
</Project>";
    }

    private string GetEmbeddedTemplate()
    {
        return @"using System.Reflection;
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
            var source = ReadEmbeddedResource(""program.malda"");
            MaldaProfiler.StartSession(ProfilingOptions.Disabled /*__MALDA_PROFILING_OPTIONS__*/, ""program.malda"" /*__MALDA_PROFILING_SESSION__*/);
            
            // Parse and execute
            var lexer = new Lexer(source, ""program.malda"");
            var tokens = lexer.Tokenize();
            
            var parser = new MaldaLang.Parser.Parser(tokens, ""program.malda"");
            var statements = parser.Parse();
            
            // Create interpreter without input provider (uses Console directly)
            var interpreter = new Interpreter.Interpreter(currentFile: ""program.malda"");
            interpreter.SetSourceCode(source);
            // For console environment, we can use GetAwaiter().GetResult() since there's no async context
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($""Error: {ex.Message}"");
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
        var message = $""\r{progress.Message} {progressBar} {progress.Percentage}%"";
        
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
        return $""[{new string('=', filled)}{new string(' ', empty)}]"";
    }
    
    static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullResourceName = $""MaldaLang.Executable.Resources.{resourceName}"";
        
        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new Exception($""Could not find embedded resource: {fullResourceName}"");
        }
        
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}";
    }

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
        var shouldPublishSingleFile = !includeLLamaSharp && !includeOptionalPacks;
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

        // Find the generated exe (name will be MaldaLang.Executable.exe)
        var exeName = Path.GetFileName(outputPath);
        if (string.IsNullOrEmpty(exeName))
        {
            exeName = "MaldaLang.Executable.exe";
        }
        else if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exeName += ".exe";
        }

        var generatedExePath = Path.Combine(publishOutputDir, "MaldaLang.Executable.exe");
        var finalExePath = shouldPublishSingleFile
            ? Path.Combine(requestedOutputDir, exeName)
            : Path.Combine(requestedOutputDir, "MaldaLang.Executable.exe");

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
            return finalExePath;
        }

        return generatedExePath;
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

    public string TranspileToCSharp(string sourcePath)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            return TranspileToCSharpFromSource(source, sourceFilePath: sourcePath);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public string TranspileToCSharpFromSource(string source)
    {
        return TranspileToCSharpFromSource(source, sourceFilePath: null, profilingOptions: null, typedTranspileLevel: 1);
    }

    public string TranspileToCSharpFromSource(string source, string? sourceFilePath)
    {
        return TranspileToCSharpFromSource(source, sourceFilePath, profilingOptions: null, typedTranspileLevel: 1);
    }

    public string TranspileToCSharpFromSource(string source, string? sourceFilePath, ProfilingOptions? profilingOptions, int typedTranspileLevel = 1)
    {
        try
        {
            var lexer = new Lexer(source, sourceFilePath);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFilePath);
            var statements = parser.Parse();
            
            if (parser.Errors.Count > 0)
            {
                throw new Exception($"Parse errors: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
            }

            var partitionResult = TargetPartitioner.Partition(statements);
            ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);
            
            var transpiler = new CSharpTranspiler(profilingOptions, typedTranspileLevel: typedTranspileLevel);
            return transpiler.Transpile(partitionResult.CSharpStatements, isLibrary: false, sourceFilePath);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public string TranspileToCSharpFromSource(string source, bool isLibrary)
    {
        return TranspileToCSharpFromSource(source, isLibrary, sourceFilePath: null, profilingOptions: null, typedTranspileLevel: 1);
    }

    public string TranspileToCSharpFromSource(string source, bool isLibrary, string? sourceFilePath)
    {
        return TranspileToCSharpFromSource(source, isLibrary, sourceFilePath, profilingOptions: null, typedTranspileLevel: 1);
    }

    public string TranspileToCSharpFromSource(string source, bool isLibrary, string? sourceFilePath, ProfilingOptions? profilingOptions, int typedTranspileLevel = 1)
    {
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFilePath);
            var statements = parser.Parse();
            
            if (parser.Errors.Count > 0)
            {
                throw new Exception($"Parse errors: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
            }

            var partitionResult = TargetPartitioner.Partition(statements);
            ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);
            
            var transpiler = new CSharpTranspiler(profilingOptions, typedTranspileLevel: typedTranspileLevel);
            return transpiler.Transpile(partitionResult.CSharpStatements, isLibrary, sourceFilePath);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public CompilationResult CompileToCSharp(string sourcePath, string outputPath)
    {
        try
        {
            var csharpCode = TranspileToCSharp(sourcePath);
            File.WriteAllText(outputPath, csharpCode, Encoding.UTF8);
            
            return new CompilationResult
            {
                Success = true,
                OutputPath = outputPath
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Transpilation error: {ex.Message}"
            };
        }
    }

    public string TranspileToJavaScript(string sourcePath)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: Path.GetFileName(sourcePath));
            return transpileResult.JavaScript;
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public string TranspileToJavaScriptFromSource(string source)
    {
        return TranspileToJavaScriptFromSource(source, sourceFilePath: null);
    }

    public string TranspileToJavaScriptFromSource(string source, string? sourceFilePath)
    {
        try
        {
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourceFilePath, generatedFileName: null);
            return transpileResult.JavaScript;
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    private static string PreprocessTemplateSourceIfNeeded(string source, string? sourceFilePath)
    {
        if (!TemplatePreprocessor.IsTemplatePath(sourceFilePath))
        {
            return source;
        }

        return TemplatePreprocessor.Preprocess(source, sourceFilePath);
    }

    public CompilationResult CompileToJavaScript(string sourcePath, string outputPath)
    {
        try
        {
            var finalOutputPath = ResolveJavaScriptOutputPath(sourcePath, outputPath);
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: Path.GetFileName(finalOutputPath));
            var mapFileName = Path.GetFileName(finalOutputPath) + ".map";
            var jsWithSourceMap = AppendSourceMapReference(transpileResult.JavaScript, mapFileName);

            File.WriteAllText(finalOutputPath, jsWithSourceMap, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            {
                var mapPath = finalOutputPath + ".map";
                File.WriteAllText(mapPath, transpileResult.SourceMapJson, Encoding.UTF8);
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath)) ?? Directory.GetCurrentDirectory();
            var mainScriptFileName = Path.GetFileName(finalOutputPath);
            var appName = Path.GetFileNameWithoutExtension(mainScriptFileName);
            File.WriteAllText(Path.Combine(outputDirectory, "malda-js-runtime.js"), GetJavaScriptRuntimeAssetContent(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), GenerateJavaScriptHostHtml(appName, mainScriptFileName), Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = finalOutputPath
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Transpilation error: {ex.Message}"
            };
        }
    }

    public CompilationResult CompileToPwa(string sourcePath, string outputPath)
    {
        try
        {
            var outputDirectory = ResolvePwaOutputDirectory(sourcePath, outputPath);
            Directory.CreateDirectory(outputDirectory);

            var appName = GetPwaAppName(sourcePath);
            var mainScriptFileName = appName + ".js";
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: mainScriptFileName);
            var jsOutputPath = Path.Combine(outputDirectory, mainScriptFileName);
            var mapFileName = mainScriptFileName + ".map";
            var jsWithSourceMap = AppendSourceMapReference(transpileResult.JavaScript, mapFileName);

            File.WriteAllText(jsOutputPath, jsWithSourceMap, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            {
                File.WriteAllText(jsOutputPath + ".map", transpileResult.SourceMapJson, Encoding.UTF8);
            }

            File.WriteAllText(Path.Combine(outputDirectory, "malda-js-runtime.js"), GetJavaScriptRuntimeAssetContent(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), GeneratePwaIndexHtml(appName, mainScriptFileName), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "icon.svg"), GeneratePwaIconSvg(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "manifest.webmanifest"), GeneratePwaManifest(appName), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "sw.js"), GeneratePwaServiceWorker(appName, mainScriptFileName), Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = outputDirectory
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Transpilation error: {ex.Message}"
            };
        }
    }

    public CompilationResult CompileToFullStack(string sourcePath, string outputPath, bool includeLLamaSharp = false, bool includeUiHost = false, ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            if (!FullStackSourceInspector.IsFullStackSource(source))
            {
                return new CompilationResult
                {
                    Success = false,
                    ErrorMessage = "Full-stack compile requires both client and server targets (or route decorators) in the same source."
                };
            }

            var outputDirectory = ResolveFullStackOutputDirectory(sourcePath, outputPath);
            var appName = GetPwaAppName(sourcePath);
            var serverDirectory = Path.Combine(outputDirectory, "server");
            var webDirectory = Path.Combine(outputDirectory, "web");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(webDirectory);

            var serverOutputPath = Path.Combine(serverDirectory, appName + ".server.exe");
            var clientScriptPath = Path.Combine(webDirectory, appName + ".js");

            var serverResult = CompileWithTranspilation(sourcePath, serverOutputPath, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel);
            if (!serverResult.Success)
            {
                return serverResult;
            }

            var clientResult = CompileToJavaScript(sourcePath, clientScriptPath);
            if (!clientResult.Success)
            {
                return clientResult;
            }

            var serverExecutablePath = serverResult.OutputPath ?? serverOutputPath;
            var clientScriptFileName = Path.GetFileName(clientResult.OutputPath ?? clientScriptPath);
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            var port = FullStackSourceInspector.ExtractHttpPort(source, 8090);

            var manifestObject = new
            {
                type = "malda-fullstack",
                source = Path.GetFullPath(sourcePath),
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                port,
                server = new
                {
                    executable = Path.GetFullPath(serverExecutablePath),
                    webDirectoryEnv = "MALDA_WEB_DIRECTORY"
                },
                web = new
                {
                    directory = Path.GetFullPath(webDirectory),
                    entryHtml = Path.GetFullPath(Path.Combine(webDirectory, "index.html")),
                    entryScript = Path.GetFullPath(Path.Combine(webDirectory, clientScriptFileName))
                },
                run = new
                {
                    command = Path.GetFileName(serverExecutablePath),
                    workingDirectory = Path.GetDirectoryName(Path.GetFullPath(serverExecutablePath)) ?? serverDirectory,
                    env = new
                    {
                        MALDA_WEB_DIRECTORY = Path.GetFullPath(webDirectory)
                    }
                }
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObject, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = outputDirectory
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Full-stack compilation error: {ex.Message}"
            };
        }
    }

    private static string ResolveJavaScriptOutputPath(string sourcePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.ChangeExtension(sourcePath, ".js");
        }

        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrEmpty(extension))
        {
            return outputPath + ".js";
        }

        return outputPath;
    }

    private static string ResolvePwaOutputDirectory(string sourcePath, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        var outputDirectoryName = GetPwaAppName(sourcePath);
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? outputDirectoryName
            : Path.Combine(sourceDirectory, outputDirectoryName);
    }

    private static string ResolveFullStackOutputDirectory(string sourcePath, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        var outputDirectoryName = GetPwaAppName(sourcePath) + "-fullstack";
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? outputDirectoryName
            : Path.Combine(sourceDirectory, outputDirectoryName);
    }

    private static string AppendSourceMapReference(string jsCode, string mapFileName)
    {
        var normalized = jsCode.TrimEnd();
        return $"{normalized}{Environment.NewLine}//# sourceMappingURL={mapFileName}{Environment.NewLine}";
    }

    private static JsTranspileResult TranspileToJavaScriptArtifactsFromSource(string source, string? sourceFilePath, string? generatedFileName)
    {
        source = PreprocessTemplateSourceIfNeeded(source, sourceFilePath);
        var lexer = new Lexer(source, sourceFilePath);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens, sourceFilePath);
        var statements = parser.Parse();

        if (parser.Errors.Count > 0)
        {
            throw new Exception($"Parse errors: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
        }

        var partitionResult = TargetPartitioner.Partition(statements);
        ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);

        var transpiler = new JsTranspiler();
        return transpiler.TranspileWithSourceMap(
            partitionResult.JavaScriptStatements,
            isLibrary: false,
            sourceFilePath,
            sourceContent: source,
            generatedFileName: generatedFileName);
    }

    private static void ThrowIfTargetPartitionDiagnostics(IReadOnlyList<TargetPartitionDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        var errorText = string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
        throw new Exception($"Target partitioning error(s): {errorText}");
    }

    private static string GetPwaAppName(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        const string templateSuffix = ".malda.html";
        if (fileName.EndsWith(templateSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^templateSuffix.Length];
        }

        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    private static string GetJavaScriptRuntimeAssetContent()
    {
        foreach (var path in GetRuntimeAssetCandidatePaths("Examples", "Web", "wwwroot", "malda-js-runtime.js"))
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return "console.warn('malda-js-runtime.js was not found at compile time. The generated PWA may not run correctly.');";
    }

    private static IEnumerable<string> GetRuntimeAssetCandidatePaths(params string[] relativeSegments)
    {
        var currentDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirectory != null)
        {
            yield return Path.Combine(new[] { currentDirectory.FullName }.Concat(relativeSegments).ToArray());
            currentDirectory = currentDirectory.Parent;
        }

        var compilerLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var compilerDirectory = string.IsNullOrWhiteSpace(compilerLocation) ? null : new DirectoryInfo(compilerLocation);
        while (compilerDirectory != null)
        {
            yield return Path.Combine(new[] { compilerDirectory.FullName }.Concat(relativeSegments).ToArray());
            compilerDirectory = compilerDirectory.Parent;
        }
    }

    private static string GeneratePwaIndexHtml(string appName, string mainScriptFileName)
    {
        var scriptLiteral = JsonSerializer.Serialize(mainScriptFileName);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#0f172a">
    <title>{{appName}}</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='14' fill='%230f172a'/%3E%3Cpath d='M18 44V16h8v20h20v8H18Z' fill='%2360a5fa'/%3E%3C/svg%3E">
    <link rel="manifest" href="./manifest.webmanifest">
    <style>
        :root {
            color-scheme: dark;
            font-family: Arial, sans-serif;
        }

        body {
            margin: 0;
            min-height: 100vh;
            background: #020617;
            color: #e2e8f0;
        }

        #status {
            padding: 10px 14px;
            border-bottom: 1px solid #1e293b;
            background: #0f172a;
            color: #94a3b8;
            font-size: 14px;
        }

        #status.error {
            color: #fecaca;
            background: #450a0a;
            border-bottom-color: #7f1d1d;
        }

        #app {
            min-height: calc(100vh - 45px);
        }
    </style>
</head>
<body>
    <div id="status">Loading {{appName}}...</div>
    <div id="app"></div>
    <script src="./malda-js-runtime.js"></script>
    <script src="./{{mainScriptFileName}}"></script>
    <script>
        (function () {
            var statusElement = document.getElementById("status");

            function setStatus(message, isError) {
                if (!statusElement) {
                    return;
                }

                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
            }

            async function runEntryPoint() {
                if (!window.MaldaApp) {
                    throw new Error("MaldaApp was not registered by " + {{scriptLiteral}} + ".");
                }

                if (typeof window.MaldaApp.main === "function") {
                    await window.MaldaApp.main();
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                    await window.MaldaApp.bootstrap("#app");
                    return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                    await window.MaldaApp.renderRoot("#app");
                    return;
                }

                if (typeof window.MaldaApp.main !== "function") {
                    throw new Error("No supported MALDA entry point was found. Expected main(), bootstrap(), or renderRoot().");
                }
            }

            window.addEventListener("load", function () {
                runEntryPoint()
                    .then(function () {
                        setStatus("Loaded {{appName}}", false);
                    })
                    .catch(function (error) {
                        console.error(error);
                        setStatus(error && error.message ? error.message : "PWA startup failed.", true);
                    });

                if ("serviceWorker" in navigator) {
                    navigator.serviceWorker.register("./sw.js").catch(function (error) {
                        console.warn("Service worker registration failed.", error);
                    });
                }
            });
        })();
    </script>
</body>
</html>
""";
    }

    private static string GenerateJavaScriptHostHtml(string appName, string mainScriptFileName)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{appName}}</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='14' fill='%230f172a'/%3E%3Cpath d='M18 44V16h8v20h20v8H18Z' fill='%2360a5fa'/%3E%3C/svg%3E">
    <style>
        :root {
            color-scheme: dark;
            font-family: Arial, sans-serif;
        }

        body {
            margin: 0;
            min-height: 100vh;
            background: #020617;
            color: #e2e8f0;
        }

        #status {
            padding: 10px 14px;
            border-bottom: 1px solid #1e293b;
            background: #0f172a;
            color: #94a3b8;
            font-size: 14px;
        }

        #status.error {
            color: #fecaca;
            background: #450a0a;
            border-bottom-color: #7f1d1d;
        }

        #app {
            min-height: calc(100vh - 45px);
        }
    </style>
</head>
<body>
    <div id="status">Loading {{appName}}...</div>
    <div id="app"></div>
    <script src="./malda-js-runtime.js"></script>
    <script src="./{{mainScriptFileName}}"></script>
    <script>
        (function () {
            var statusElement = document.getElementById("status");

            function setStatus(message, isError) {
                if (!statusElement) {
                    return;
                }

                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
            }

            async function runEntryPoint() {
                if (!window.MaldaApp) {
                    throw new Error("MaldaApp was not registered by " + "{{mainScriptFileName}}" + ".");
                }

                if (typeof window.MaldaApp.main === "function") {
                    await window.MaldaApp.main();
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                    await window.MaldaApp.bootstrap("#app");
                    return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                    await window.MaldaApp.renderRoot("#app");
                    return;
                }

                if (typeof window.MaldaApp.main !== "function") {
                    throw new Error("No supported MALDA entry point was found. Expected main(), bootstrap(), or renderRoot().");
                }
            }

            window.addEventListener("load", function () {
                runEntryPoint()
                    .then(function () {
                        setStatus("Loaded {{appName}}", false);
                    })
                    .catch(function (error) {
                        console.error(error);
                        setStatus(error && error.message ? error.message : "JavaScript app startup failed.", true);
                    });
            });
        })();
    </script>
</body>
</html>
""";
    }

    private static string GeneratePwaManifest(string appName)
    {
        var nameLiteral = JsonSerializer.Serialize(appName);
        return $$"""
{
  "name": {{nameLiteral}},
  "short_name": {{nameLiteral}},
  "start_url": "./index.html",
  "display": "standalone",
  "background_color": "#020617",
  "theme_color": "#0f172a",
  "icons": [
    {
      "src": "./icon.svg",
      "sizes": "512x512",
      "type": "image/svg+xml"
    }
  ]
}
""";
    }

    private static string GeneratePwaIconSvg()
    {
        return """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <defs>
    <linearGradient id="maldaGradient" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#0f172a" />
      <stop offset="100%" stop-color="#2563eb" />
    </linearGradient>
  </defs>
  <rect width="512" height="512" rx="96" fill="url(#maldaGradient)" />
  <path d="M128 384V128h64l64 88 64-88h64v256h-64V232l-64 88-64-88v152z" fill="#f8fafc" />
</svg>
""";
    }

    private static string GeneratePwaServiceWorker(string appName, string mainScriptFileName)
    {
        var cacheNameLiteral = JsonSerializer.Serialize($"malda-pwa-{appName.ToLowerInvariant()}-v1");
        var mainScriptLiteral = JsonSerializer.Serialize("./" + mainScriptFileName);
        return $$"""
const CACHE_NAME = {{cacheNameLiteral}};
const APP_SHELL = [
    "./",
    "./index.html",
    "./manifest.webmanifest",
    "./icon.svg",
    "./sw.js",
    "./malda-js-runtime.js",
    {{mainScriptLiteral}}
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL))
    );
    self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
    );
    self.clients.claim();
});

self.addEventListener("fetch", (event) => {
    if (event.request.method !== "GET") {
        return;
    }

    const requestUrl = new URL(event.request.url);
    if (requestUrl.origin !== self.location.origin) {
        return;
    }

    event.respondWith(
        caches.match(event.request).then((cachedResponse) => {
            if (cachedResponse) {
                return cachedResponse;
            }

            return fetch(event.request).then((networkResponse) => {
                if (!networkResponse || networkResponse.status !== 200) {
                    return networkResponse;
                }

                const responseToCache = networkResponse.clone();
                caches.open(CACHE_NAME).then((cache) => cache.put(event.request, responseToCache));
                return networkResponse;
            });
        })
    );
});
""";
    }

    private CompilationResult CompileToDll(string sourcePath, string outputPath, bool includeLLamaSharp = false)
    {
        try
        {
            // Validate source code
            var source = File.ReadAllText(sourcePath);
            ValidateSource(source, sourcePath);

            // Create temporary directory for generated project
            var tempDir = Path.Combine(Path.GetTempPath(), $"spl_dll_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Transpile to C# with library mode
                var csharpCode = TranspileToCSharpFromSource(source, isLibrary: true, sourceFilePath: sourcePath);

                // Write generated C# code to Examples/GeneratedProgram.cs for inspection
                try
                {
                    var sourceDir = Path.GetDirectoryName(sourcePath);
                    var examplesDir = sourceDir != null ? Path.Combine(sourceDir, "Examples") : null;
                    
                    if (examplesDir == null || !Directory.Exists(examplesDir))
                    {
                        var currentDir = sourceDir ?? Directory.GetCurrentDirectory();
                        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir, "Examples")))
                        {
                            var parent = Directory.GetParent(currentDir);
                            currentDir = parent?.FullName;
                        }
                        if (currentDir != null)
                        {
                            examplesDir = Path.Combine(currentDir, "Examples");
                        }
                    }
                    
                    if (examplesDir == null || !Directory.Exists(examplesDir))
                    {
                        examplesDir = Path.Combine(Directory.GetCurrentDirectory(), "Examples");
                    }
                    
                    if (!Directory.Exists(examplesDir))
                    {
                        Directory.CreateDirectory(examplesDir);
                    }
                    
                    var generatedProgramPath = Path.Combine(examplesDir, "GeneratedProgram.cs");
                    File.WriteAllText(generatedProgramPath, csharpCode, Encoding.UTF8);
                }
                catch
                {
                    // Ignore errors writing to Examples directory - not critical
                }

                // Generate project files for DLL
                GenerateDllProjectFiles(tempDir, csharpCode, outputPath, includeLLamaSharp);

                // Compile DLL
                var dllPath = CompileDll(tempDir, outputPath, includeLLamaSharp);

                return new CompilationResult
                {
                    Success = true,
                    OutputPath = dllPath
                };
            }
            finally
            {
                // Cleanup temporary directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (ParseException ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Syntax error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Compilation error: {ex.Message}"
            };
        }
    }

    private CompilationResult CompileWithTranspilation(string sourcePath, string outputPath, bool includeLLamaSharp = false, bool includeUiHost = false, ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1, bool includeOptionalPacks = false, IReadOnlyList<EmbeddedFolderSpec>? embedFolders = null)
    {
        try
        {
            // Validate source code
            var source = File.ReadAllText(sourcePath);
            ValidateSource(source, sourcePath);

            // Create temporary directory for generated project
            var tempDir = Path.Combine(Path.GetTempPath(), $"spl_transpile_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Transpile to C#
                var csharpCode = TranspileToCSharpFromSource(source, sourcePath, profilingOptions, typedTranspileLevel);
                var shouldEmbedUiHost = includeUiHost || UsesUiFramework(source);
                if (shouldEmbedUiHost)
                {
                    csharpCode = AddEmbeddedUiHostToTranspiledCode(csharpCode);
                }

                // Write generated C# code to Examples/GeneratedProgram.cs for inspection
                try
                {
                    // Try to find Examples directory relative to source file
                    var sourceDir = Path.GetDirectoryName(sourcePath);
                    var examplesDir = sourceDir != null ? Path.Combine(sourceDir, "Examples") : null;
                    
                    // If not found, try workspace root (go up from source until we find Examples)
                    if (examplesDir == null || !Directory.Exists(examplesDir))
                    {
                        var currentDir = sourceDir ?? Directory.GetCurrentDirectory();
                        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir, "Examples")))
                        {
                            var parent = Directory.GetParent(currentDir);
                            currentDir = parent?.FullName;
                        }
                        if (currentDir != null)
                        {
                            examplesDir = Path.Combine(currentDir, "Examples");
                        }
                    }
                    
                    // If still not found, try current directory
                    if (examplesDir == null || !Directory.Exists(examplesDir))
                    {
                        examplesDir = Path.Combine(Directory.GetCurrentDirectory(), "Examples");
                    }
                    
                    // Create directory if it doesn't exist
                    if (!Directory.Exists(examplesDir))
                    {
                        Directory.CreateDirectory(examplesDir);
                    }
                    
                    var generatedProgramPath = Path.Combine(examplesDir, "GeneratedProgram.cs");
                    File.WriteAllText(generatedProgramPath, csharpCode, Encoding.UTF8);
                }
                catch
                {
                    // Ignore errors writing to Examples directory - not critical
                }

                // Generate project files for transpiled C#
                GenerateTranspiledProjectFiles(tempDir, csharpCode, includeLLamaSharp, shouldEmbedUiHost, embedFolders ?? Array.Empty<EmbeddedFolderSpec>());

                // Compile executable
                var exePath = CompileExecutable(tempDir, outputPath, includeLLamaSharp, includeOptionalPacks);

                return new CompilationResult
                {
                    Success = true,
                    OutputPath = exePath
                };
            }
            finally
            {
                // Cleanup temporary directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (ParseException ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Syntax error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Compilation error: {ex.Message}"
            };
        }
    }

    private static string InjectProfilingOptionsIntoTemplate(string template, ProfilingOptions? profilingOptions, string? sessionName)
    {
        return template
            .Replace("ProfilingOptions.Disabled /*__MALDA_PROFILING_OPTIONS__*/", BuildProfilingOptionsLiteral(profilingOptions), StringComparison.Ordinal)
            .Replace("\"program.malda\" /*__MALDA_PROFILING_SESSION__*/", ToCSharpStringLiteral(sessionName ?? "program.malda"), StringComparison.Ordinal);
    }

    private static string BuildProfilingOptionsLiteral(ProfilingOptions? profilingOptions)
    {
        if (profilingOptions == null || !profilingOptions.Enabled)
        {
            return "MaldaLang.Runtime.Profiling.ProfilingOptions.Disabled";
        }

        return $@"new MaldaLang.Runtime.Profiling.ProfilingOptions
            {{
                Enabled = true,
                OutputPath = {ToCSharpStringLiteral(profilingOptions.OutputPath)},
                Format = MaldaLang.Runtime.Profiling.ProfilingFormat.{profilingOptions.Format},
                WriteToConsole = {(profilingOptions.WriteToConsole ? "true" : "false")},
                MaxEntriesPerSection = {profilingOptions.MaxEntriesPerSection}
            }}";
    }

    private static string ToCSharpStringLiteral(string? value)
    {
        if (value == null)
        {
            return "null";
        }

        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private void GenerateTranspiledProjectFiles(string tempDir, string csharpCode, bool includeLLamaSharp = false, bool includeUiHost = false, IReadOnlyList<EmbeddedFolderSpec>? embedFolders = null)
    {
        // Write transpiled C# code to Program.cs
        var programCsPath = Path.Combine(tempDir, "Program.cs");
        File.WriteAllText(programCsPath, csharpCode, Encoding.UTF8);

        // Find and copy malda.dll to temp directory for reference
        var MaldaLangDllPath = FindMaldaLangDll();
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            var dllDestPath = Path.Combine(tempDir, "malda.dll");
            File.Copy(MaldaLangDllPath, dllDestPath, true);
        }

        StageEmbeddedFolders(tempDir, embedFolders ?? Array.Empty<EmbeddedFolderSpec>());
        StageTranspilePackReferences(tempDir, out var extraProjectReferences);

        // Generate .csproj file
        var csprojPath = Path.Combine(tempDir, "MaldaLang.Executable.csproj");
        var csprojContent = GenerateTranspiledCsprojContent(tempDir, MaldaLangDllPath, includeLLamaSharp, includeUiHost, extraProjectReferences);
        File.WriteAllText(csprojPath, csprojContent);
    }

    private void GenerateDllProjectFiles(string tempDir, string csharpCode, string outputPath, bool includeLLamaSharp = false)
    {
        // Write transpiled C# code to Program.cs
        var programCsPath = Path.Combine(tempDir, "Program.cs");
        File.WriteAllText(programCsPath, csharpCode, Encoding.UTF8);

        // Find and copy malda.dll to temp directory for reference
        var MaldaLangDllPath = FindMaldaLangDll();
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            var dllDestPath = Path.Combine(tempDir, "malda.dll");
            File.Copy(MaldaLangDllPath, dllDestPath, true);
        }

        // Get assembly name from output path
        var assemblyName = Path.GetFileNameWithoutExtension(outputPath);
        if (string.IsNullOrEmpty(assemblyName))
        {
            assemblyName = "MaldaLangLibrary";
        }

        // Generate .csproj file
        var csprojPath = Path.Combine(tempDir, $"{assemblyName}.csproj");
        var csprojContent = GenerateDllCsprojContent(tempDir, MaldaLangDllPath, assemblyName, includeLLamaSharp);
        File.WriteAllText(csprojPath, csprojContent);
    }

    private string GenerateTranspiledCsprojContent(string tempDir, string? MaldaLangDllPath, bool includeLLamaSharp = false, bool includeUiHost = false, string extraProjectReferences = "")
    {
        string projectReference;
        
        // Prefer DLL reference if available
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            var localDllPath = Path.Combine(tempDir, "malda.dll");
            projectReference = $@"    <ItemGroup>
      <Reference Include=""MaldaLang"">
        <HintPath>{localDllPath}</HintPath>
        <Private>True</Private>
        <CopyLocal>True</CopyLocal>
      </Reference>
    </ItemGroup>";
        }
        else
        {
            // Cannot use project reference because MaldaLang.csproj is a self-contained executable
            // Throw an error instead
            throw new Exception("malda.dll not found. Please ensure MaldaLang project is built and the DLL is available. " +
                "The DLL should be in the same directory as the compiler, or in MaldaLang/bin/Debug/net8.0 or MaldaLang/bin/Release/net8.0");
        }
        
        string packageReferences = @"  <ItemGroup>
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        if (includeLLamaSharp)
        {
            packageReferences = @"  <ItemGroup>
    <PackageReference Include=""LLamaSharp"" Version=""0.26.0"" />
    <PackageReference Include=""LLamaSharp.Backend.Cpu"" Version=""0.26.0"" />
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        }
        
        // Disable single-file publishing when LLamaSharp is included to avoid conflicts
        // with multiple native DLLs (avx, avx2, avx512, noavx variants)
        string publishSingleFile = includeLLamaSharp ? "false" : "true";
        
        var uiHostFrameworkReference = includeUiHost
            ? @"  <ItemGroup>
    <FrameworkReference Include=""Microsoft.AspNetCore.App"" />
  </ItemGroup>"
            : string.Empty;

        var folderResources = BuildEmbeddedFolderResourceItems(tempDir);
        var embeddedResources = string.IsNullOrEmpty(folderResources)
            ? ""
            : $@"  <ItemGroup>
{folderResources}  </ItemGroup>";

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MaldaLang.Executable</RootNamespace>
    <PublishSingleFile>{publishSingleFile}</PublishSingleFile>
    <SelfContained>false</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <NoWarn>$(NoWarn);CS1998;CS8600;CS8602;CS8603;CS8604;CS8605;CS8618;CS8625;CS8629</NoWarn>
  </PropertyGroup>
  {projectReference}
  {extraProjectReferences}
  {uiHostFrameworkReference}
  {packageReferences}
{embeddedResources}
</Project>";
    }

    private static bool UsesUiFramework(string source)
    {
        return source.Contains("ui.") ||
               source.Contains("uiMount(") ||
               source.Contains("uiMountEnvelope(") ||
               source.Contains("uiRender(") ||
               source.Contains("uiDispatchEvent(") ||
               source.Contains("uiPullEvent(") ||
               source.Contains("uiState(") ||
               source.Contains("uiSetState(");
    }

    private static string AddEmbeddedUiHostToTranspiledCode(string csharpCode)
    {
        var patched = csharpCode;
        if (!patched.Contains("using Microsoft.AspNetCore.Builder;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Builder;\n" + patched;
        }
        if (!patched.Contains("using Microsoft.AspNetCore.Hosting;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Hosting;\n" + patched;
        }
        if (!patched.Contains("using Microsoft.AspNetCore.Http;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Http;\n" + patched;
        }

        if (patched.Contains(EmbeddedUiHostStartMarker, StringComparison.Ordinal))
        {
            var startCall = $"{EmbeddedUiHostStartMarker}\n                    await EmbeddedUiHostRuntime.TryStartAsync();";
            patched = patched.Replace(EmbeddedUiHostStartMarker, startCall, StringComparison.Ordinal);
        }
        else
        {
            var mainSignature = "public static async Task Main(string[] args)";
            var mainIndex = patched.IndexOf(mainSignature, StringComparison.Ordinal);
            if (mainIndex >= 0)
            {
                var openBraceIndex = patched.IndexOf('{', mainIndex);
                if (openBraceIndex >= 0)
                {
                    var insertion = "\n                await EmbeddedUiHostRuntime.TryStartAsync();";
                    patched = patched.Insert(openBraceIndex + 1, insertion);
                }
            }
        }

        var indexHtml = GetUiHostAssetContent("index.html");
        var clientJs = GetUiHostAssetContent("malda-ui-client.js");

        var runtimeCode = $@"

internal static class EmbeddedUiHostRuntime
{{
    private const string ProtocolVersion = ""1.0"";
    private static readonly object Gate = new();
    private static bool _started;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<System.Guid, System.Net.WebSockets.WebSocket>> SocketsBySession = new(System.StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> LastEnvelopeBySession = new(System.StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> SequenceBySession = new(System.StringComparer.Ordinal);

    private static readonly string IndexHtml = {ToVerbatimStringLiteral(indexHtml)};
    private static readonly string UiClientJs = {ToVerbatimStringLiteral(clientJs)};

    public static async System.Threading.Tasks.Task<bool> TryStartAsync()
    {{
        lock (Gate)
        {{
            if (_started)
                return true;
            _started = true;
        }}

        try
        {{
            var baseUrl = ResolveBaseUrl();
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(baseUrl);
            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet(""/health"", () => Microsoft.AspNetCore.Http.Results.Ok(new {{ ok = true, protocolVersion = ProtocolVersion }}));
            app.MapGet(""/"", () => Microsoft.AspNetCore.Http.Results.Text(IndexHtml, ""text/html; charset=utf-8""));
            app.MapGet(""/index.html"", () => Microsoft.AspNetCore.Http.Results.Text(IndexHtml, ""text/html; charset=utf-8""));
            app.MapGet(""/malda-ui-client.js"", () => Microsoft.AspNetCore.Http.Results.Text(UiClientJs, ""application/javascript; charset=utf-8""));

            app.Map(""/ui/ws/{{sessionId}}"", HandleWebSocketAsync);
            app.MapPost(""/ui/mount/{{sessionId}}"", (string sessionId, Microsoft.AspNetCore.Http.HttpContext context) => HandleEnvelopeAsync(sessionId, ""mount"", context));
            app.MapPost(""/ui/patch/{{sessionId}}"", (string sessionId, Microsoft.AspNetCore.Http.HttpContext context) => HandleEnvelopeAsync(sessionId, ""patch"", context));

            _ = app.RunAsync();
            await WaitForHealthAsync(baseUrl);
            return true;
        }}
        catch
        {{
            return false;
        }}
    }}

    private static string ResolveBaseUrl()
    {{
        var configured = System.Environment.GetEnvironmentVariable(""MALDA_UI_HOST_URL"");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return ""http://localhost:50114"";
    }}

    private static async System.Threading.Tasks.Task HandleWebSocketAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {{
        if (!context.WebSockets.IsWebSocketRequest)
        {{
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
            return;
        }}

        if (!AuthorizeRequest(context))
        {{
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
            return;
        }}

        var sessionId = context.Request.RouteValues.TryGetValue(""sessionId"", out var sessionValue) ? sessionValue?.ToString() ?? ""default"" : ""default"";
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var socketId = System.Guid.NewGuid();
        var sessionSockets = SocketsBySession.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<System.Guid, System.Net.WebSockets.WebSocket>());
        sessionSockets[socketId] = socket;

        await SendJsonAsync(socket, new
        {{
            type = ""connected"",
            sessionId,
            version = ProtocolVersion,
            envelopeId = System.Guid.NewGuid().ToString(""N""),
            sequence = 1,
            serverTimeUtc = System.DateTime.UtcNow.ToString(""O"")
        }});

        var buffer = new byte[8 * 1024];
        try
        {{
            while (socket.State == System.Net.WebSockets.WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {{
                var payload = await ReceiveTextMessageAsync(socket, buffer, context.RequestAborted);
                if (payload == null)
                    break;

                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                await BroadcastAsync(sessionId, new
                {{
                    type = ""event"",
                    version = ProtocolVersion,
                    sessionId,
                    sequence = NextServerSequence(sessionId),
                    envelopeId = System.Guid.NewGuid().ToString(""N""),
                    serverTimeUtc = System.DateTime.UtcNow.ToString(""O""),
                    payload = ParseOrRaw(payload)
                }});
            }}
        }}
        finally
        {{
            sessionSockets.TryRemove(socketId, out _);
            if (sessionSockets.IsEmpty)
            {{
                SocketsBySession.TryRemove(sessionId, out _);
            }}
        }}
    }}

    private static async System.Threading.Tasks.Task<Microsoft.AspNetCore.Http.IResult> HandleEnvelopeAsync(string sessionId, string envelopeType, Microsoft.AspNetCore.Http.HttpContext context)
    {{
        if (!AuthorizeRequest(context))
        {{
            return Microsoft.AspNetCore.Http.Results.Unauthorized();
        }}

        using var reader = new System.IO.StreamReader(context.Request.Body, System.Text.Encoding.UTF8);
        var payloadText = await reader.ReadToEndAsync();
        var payload = ParseOrRaw(payloadText);
        var envelope = new
        {{
            type = envelopeType,
            version = ProtocolVersion,
            sessionId,
            sequence = NextServerSequence(sessionId),
            envelopeId = System.Guid.NewGuid().ToString(""N""),
            serverTimeUtc = System.DateTime.UtcNow.ToString(""O""),
            payload
        }};
        LastEnvelopeBySession[sessionId] = envelope;
        await BroadcastAsync(sessionId, envelope);
        return Microsoft.AspNetCore.Http.Results.Ok(new {{ delivered = true, protocolVersion = ProtocolVersion }});
    }}

    private static bool AuthorizeRequest(Microsoft.AspNetCore.Http.HttpContext context)
    {{
        var authToken = System.Environment.GetEnvironmentVariable(""MALDA_UI_AUTH_TOKEN"");
        if (string.IsNullOrWhiteSpace(authToken))
            return true;

        if (!context.Request.Headers.TryGetValue(""X-Malda-UI-Auth"", out var token))
        {{
            token = context.Request.Query[""token""];
        }}

        return string.Equals(token.ToString(), authToken, System.StringComparison.Ordinal);
    }}

    private static object ParseOrRaw(string text)
    {{
        try
        {{
            return System.Text.Json.JsonSerializer.Deserialize<object>(text) ?? new {{ raw = text }};
        }}
        catch
        {{
            return new {{ raw = text }};
        }}
    }}

    private static int NextServerSequence(string sessionId)
    {{
        return SequenceBySession.AddOrUpdate(sessionId, 1, (_, current) => current + 1);
    }}

    private static async System.Threading.Tasks.Task BroadcastAsync(string sessionId, object message)
    {{
        if (!SocketsBySession.TryGetValue(sessionId, out var sockets) || sockets.IsEmpty)
            return;

        var deadSockets = new System.Collections.Generic.List<System.Guid>();
        foreach (var entry in sockets)
        {{
            var socket = entry.Value;
            if (socket.State != System.Net.WebSockets.WebSocketState.Open)
            {{
                deadSockets.Add(entry.Key);
                continue;
            }}

            try
            {{
                await SendJsonAsync(socket, message);
            }}
            catch
            {{
                deadSockets.Add(entry.Key);
            }}
        }}

        foreach (var deadSocket in deadSockets)
        {{
            sockets.TryRemove(deadSocket, out _);
        }}
    }}

    private static async System.Threading.Tasks.Task SendJsonAsync(System.Net.WebSockets.WebSocket socket, object message)
    {{
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
    }}

    private static async System.Threading.Tasks.Task<string?> ReceiveTextMessageAsync(System.Net.WebSockets.WebSocket socket, byte[] buffer, System.Threading.CancellationToken cancellationToken)
    {{
        using var stream = new System.IO.MemoryStream();
        while (true)
        {{
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                return null;
            if (result.MessageType != System.Net.WebSockets.WebSocketMessageType.Text)
                return null;

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }}

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }}

    private static async System.Threading.Tasks.Task WaitForHealthAsync(string baseUrl)
    {{
        using var client = new System.Net.Http.HttpClient();
        for (var i = 0; i < 10; i++)
        {{
            try
            {{
                var response = await client.GetAsync(baseUrl.TrimEnd('/') + ""/health"");
                if (response.IsSuccessStatusCode)
                    return;
            }}
            catch
            {{
                // Host still starting.
            }}
            await System.Threading.Tasks.Task.Delay(100);
        }}
    }}
}}";

        return patched + runtimeCode;
    }

    private static string GetUiHostAssetContent(string fileName)
    {
        var candidatePaths = new[]
        {
            Path.Combine(System.Environment.CurrentDirectory, "MaldaLang.UIHost", "wwwroot", fileName),
            Path.Combine(System.Environment.CurrentDirectory, "..", "MaldaLang.UIHost", "wwwroot", fileName)
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>MALDA UI Host</title></head><body><div id=\"app\"></div><script src=\"/malda-ui-client.js\"></script></body></html>"
            : "console.warn('Embedded MALDA UI client was not found at compile time.');";
    }

    private static string ToVerbatimStringLiteral(string value)
    {
        return "@\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string GenerateDllCsprojContent(string tempDir, string? MaldaLangDllPath, string assemblyName, bool includeLLamaSharp = false)
    {
        string projectReference;
        
        // Prefer DLL reference if available
        if (MaldaLangDllPath != null && File.Exists(MaldaLangDllPath))
        {
            var localDllPath = Path.Combine(tempDir, "malda.dll");
            projectReference = $@"    <ItemGroup>
      <Reference Include=""MaldaLang"">
        <HintPath>{localDllPath}</HintPath>
        <Private>True</Private>
        <CopyLocal>True</CopyLocal>
      </Reference>
    </ItemGroup>";
        }
        else
        {
            throw new Exception("malda.dll not found. Please ensure MaldaLang project is built and the DLL is available. " +
                "The DLL should be in the same directory as the compiler, or in MaldaLang/bin/Debug/net8.0 or MaldaLang/bin/Release/net8.0");
        }
        
        string packageReferences = @"  <ItemGroup>
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        if (includeLLamaSharp)
        {
            packageReferences = @"  <ItemGroup>
    <PackageReference Include=""LLamaSharp"" Version=""0.26.0"" />
    <PackageReference Include=""LLamaSharp.Backend.Cpu"" Version=""0.26.0"" />
    <PackageReference Include=""Markdig"" Version=""0.33.0"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        }
        
        return $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>GeneratedCode</RootNamespace>
    <AssemblyName>{assemblyName}</AssemblyName>
  </PropertyGroup>
  {projectReference}
  {packageReferences}
</Project>";
    }
}
