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
    <PackageReference Include=""PdfPig"" Version=""0.1.15"" />
    <PackageReference Include=""DocumentFormat.OpenXml"" Version=""3.5.1"" />
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
    <PackageReference Include=""PdfPig"" Version=""0.1.15"" />
    <PackageReference Include=""DocumentFormat.OpenXml"" Version=""3.5.1"" />
    <PackageReference Include=""Microsoft.Data.Sqlite"" Version=""10.0.3"" />
    <PackageReference Include=""Microsoft.Extensions.FileSystemGlobbing"" Version=""8.0.0"" />
    <PackageReference Include=""Spectre.Console"" Version=""0.49.1"" />
  </ItemGroup>";
        }
        
        // Disable single-file publishing when LLamaSharp is included to avoid conflicts
        // with multiple native DLLs (avx, avx2, avx512, noavx variants)
        string publishSingleFile = includeLLamaSharp || _includeOnnxRuntime ? "false" : "true";
        packageReferences = WithOnnxRuntimePackage(packageReferences);
        
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
}
