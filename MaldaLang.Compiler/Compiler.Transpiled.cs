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
                    csharpCode = AddUiHostStartToTranspiledCode(csharpCode);
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

        if (includeUiHost)
        {
            StageUiHostDll(tempDir, MaldaLangDllPath);
        }

        StageEmbeddedFolders(tempDir, embedFolders ?? Array.Empty<EmbeddedFolderSpec>());
        StageTranspilePackReferences(tempDir, out var extraProjectReferences);

        // Generate .csproj file
        var csprojPath = Path.Combine(tempDir, "MaldaLang.Executable.csproj");
        var csprojContent = GenerateTranspiledCsprojContent(tempDir, MaldaLangDllPath, includeLLamaSharp, includeUiHost, extraProjectReferences);
        File.WriteAllText(csprojPath, csprojContent);
    }

    private const string UiHostDllName = "MaldaLang.UIHost.dll";

    /// <summary>Copies the UI host (built beside malda.dll, which references it) into the transpile project.</summary>
    private static void StageUiHostDll(string tempDir, string? maldaDllPath)
    {
        var source = maldaDllPath == null ? null : Path.Combine(Path.GetDirectoryName(maldaDllPath) ?? ".", UiHostDllName);
        if (source == null || !File.Exists(source))
        {
            throw new Exception($"{UiHostDllName} not found next to malda.dll ({maldaDllPath ?? "malda.dll not found"}). " +
                "Programs that use ui.mount / ui.render need it; rebuild MaldaLang.Compiler so the CLI output folder has both.");
        }

        File.Copy(source, Path.Combine(tempDir, UiHostDllName), true);
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
        
        var uiHostFrameworkReference = includeUiHost
            ? $@"  <ItemGroup>
    <FrameworkReference Include=""Microsoft.AspNetCore.App"" />
    <Reference Include=""MaldaLang.UIHost"">
      <HintPath>{Path.Combine(tempDir, UiHostDllName)}</HintPath>
      <Private>True</Private>
    </Reference>
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

    private string WithOnnxRuntimePackage(string packageReferences)
    {
        if (!_includeOnnxRuntime)
            return packageReferences;
        return packageReferences.Replace(
            "  <ItemGroup>",
            "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.ML.OnnxRuntime\" Version=\"1.20.1\" />",
            StringComparison.Ordinal);
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

        packageReferences = WithOnnxRuntimePackage(packageReferences);
        
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
