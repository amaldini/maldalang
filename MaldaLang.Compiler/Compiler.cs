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

public enum CompilationMode
{
    Interpreter,
    TranspileToCSharp,
    TranspileToDll,
    JavaScript,
    PWA,
    FullStack,
    /// <summary><c>--mode esm</c>: an ES module that exports the program's declarations.</summary>
    JavaScriptModule
}

public partial class Compiler
{
    private const string EmbeddedUiHostStartMarker = "RegisterDecoratedFunctions();";
    private static readonly Regex EmbedAliasPattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private bool _includeOnnxRuntime;

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

    /// <summary>
    /// True when the MALDA source needs Microsoft.ML.OnnxRuntime at publish time —
    /// e.g. <c>new OnnxModel(...)</c>. Without this, the managed ONNX assembly is
    /// missing from the published folder (malda.dll is referenced as a raw DLL).
    /// </summary>
    public static bool SourceRequiresOnnxRuntime(string source)
    {
        if (string.IsNullOrEmpty(source))
            return false;
        return source.Contains("OnnxModel", StringComparison.Ordinal);
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

        _includeOnnxRuntime = false;
        if (File.Exists(sourcePath))
        {
            try
            {
                var sourceText = File.ReadAllText(sourcePath);
                if (!includeLLamaSharp && SourceRequiresLLamaSharp(sourceText))
                    includeLLamaSharp = true;
                _includeOnnxRuntime = SourceRequiresOnnxRuntime(sourceText);
            }
            catch
            {
                // Keep caller-provided flags when the source cannot be probed.
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

        if (mode == CompilationMode.JavaScriptModule)
        {
            return CompileToJavaScriptModule(sourcePath, outputPath);
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

        paths += "\n" + TranspileFailureDebugHint;
        return paths;
    }

    /// <summary>
    /// Footer printed after GeneratedProgram / build_errors paths on transpile failure.
    /// </summary>
    internal const string TranspileFailureDebugHint =
        "Open the .malda line from the diagnostic when present; otherwise inspect GeneratedProgram.cs (unmapped #line default). See docs/debugging-transpile.md";

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

            ThrowIfJsImportOnHost(source, statements);
            var partitionResult = TargetPartitioner.Partition(statements);
            ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);
            
            var transpiler = new CSharpTranspiler(profilingOptions, typedTranspileLevel: typedTranspileLevel);
            return transpiler.Transpile(partitionResult.CSharpStatements, isLibrary: false, sourceFilePath);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (NotSupportedException ex) when (ex.Message == JsImportStatement.HostBackendMessage)
        {
            throw new Exception($"Transpilation error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    /// <summary>
    /// <c>import js</c> needs the JavaScript backend. A full-stack source keeps it for its client
    /// half (<see cref="TargetPartitioner"/> sends it to JavaScript only); any other program
    /// compiled to C# is refused up front instead of failing on the names it binds.
    /// </summary>
    private static void ThrowIfJsImportOnHost(string source, List<Statement> statements)
    {
        if (statements.Any(statement => statement is JsImportStatement) &&
            !FullStackSourceInspector.IsFullStackSource(source))
        {
            throw new NotSupportedException(JsImportStatement.HostBackendMessage);
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

            ThrowIfJsImportOnHost(source, statements);
            var partitionResult = TargetPartitioner.Partition(statements);
            ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);
            
            var transpiler = new CSharpTranspiler(profilingOptions, typedTranspileLevel: typedTranspileLevel);
            return transpiler.Transpile(partitionResult.CSharpStatements, isLibrary, sourceFilePath);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (NotSupportedException ex) when (ex.Message == JsImportStatement.HostBackendMessage)
        {
            throw new Exception($"Transpilation error: {ex.Message}");
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
}
