// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInRunMALDA(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("runMALDA() expects at least 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("runMALDA() sourceOrFilePath cannot be empty");
        
        // Extract optional input parameter
        string? input = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            input = args[1].AsString();
        }
        
        // Detect if argument is a file path
        bool isFilePath = false;
        string source = sourceOrFilePath;
        
        // Check if it looks like a file path
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) || 
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            // Try to resolve as file path
            try
            {
                string filePath;
                if (Path.IsPathRooted(sourceOrFilePath))
                {
                    filePath = Path.GetFullPath(sourceOrFilePath);
                }
                else
                {
                    filePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                }
                
                // Check for path traversal attempts
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("success", RuntimeValue.Boolean(false));
                    errorObj.Set("output", RuntimeValue.String(""));
                    errorObj.Set("error", RuntimeValue.String($"Error: Path contains suspicious characters (path traversal attempt)"));
                    return RuntimeValue.Object(errorObj);
                }
                
                if (File.Exists(filePath))
                {
                    isFilePath = true;
                    source = File.ReadAllText(filePath);
                }
            }
            catch (Exception ex)
            {
                var errorObj = new JsonObject();
                errorObj.Set("success", RuntimeValue.Boolean(false));
                errorObj.Set("output", RuntimeValue.String(""));
                errorObj.Set("error", RuntimeValue.String($"Error reading file: {ex.Message}"));
                return RuntimeValue.Object(errorObj);
            }
        }
        
        // Capture output
        var output = new StringBuilder();
        TextWriter? originalOut = null;
        TextReader? originalIn = null;
        StringWriter? outputWriter = null;
        StringReader? inputReader = null;
        
        try
        {
            // Capture stdout
            originalOut = Console.Out;
            outputWriter = new StringWriter(output);
            Console.SetOut(outputWriter);
            
            // Set up stdin if input provided
            if (!string.IsNullOrEmpty(input))
            {
                originalIn = Console.In;
                inputReader = new StringReader(input);
                Console.SetIn(inputReader);
            }
            
            // Parse and execute
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            
            var parser = new Parser(tokens);
            var statements = parser.Parse();
            
            // Check for parse errors
            if (parser.Errors.Count > 0)
            {
                var firstError = parser.Errors[0];
                var errorObj = new JsonObject();
                errorObj.Set("success", RuntimeValue.Boolean(false));
                errorObj.Set("output", RuntimeValue.String(output.ToString()));
                errorObj.Set("error", RuntimeValue.String(firstError.Message));
                return RuntimeValue.Object(errorObj);
            }
            
            // Create interpreter and execute
            var interpreter = new Interpreter();
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            
            // Success
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(true));
            resultObj.Set("output", RuntimeValue.String(output.ToString()));
            resultObj.Set("error", RuntimeValue.String(""));
            
            // Trace successful runMALDA execution
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = true,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError = (string?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(ex.Message));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = ex.Message,
                        runtimeError = (string?)null
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (RuntimeException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(""));
            var runtimeError = DescribeBrowserOnlyRunFailure(ex.Message);
            errorObj.Set("runtimeError", RuntimeValue.String(runtimeError));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(""));
            var described = DescribeBrowserOnlyRunFailure(ex.Message);
            var runtimeError = string.Equals(described, ex.Message, StringComparison.Ordinal)
                ? $"Error: {ex.Message}"
                : described;
            errorObj.Set("runtimeError", RuntimeValue.String(runtimeError));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        finally
        {
            // Restore original streams
            if (originalOut != null)
                Console.SetOut(originalOut);
            if (originalIn != null)
                Console.SetIn(originalIn);
            outputWriter?.Dispose();
            inputReader?.Dispose();
        }
    }
    
    private static RuntimeValue BuiltInCreateRunMALDATool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createRunMALDATool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateRunMALDATool(workingDir);
    }
    
    private static RuntimeValue BuiltInCompileMALDA(List<RuntimeValue> args)
    {
        BuiltInArity.Require("compileMALDA", args, 1, 4, "sourcePath, outputPath?, mode?, embedFolder?");
        if (args[0].Type != ValueType.String)
            throw new Exception("compileMALDA() sourcePath must be a string");
        
        var sourcePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new Exception("compileMALDA() sourcePath cannot be empty");
        
        // Extract optional outputPath parameter
        string? outputPath = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            outputPath = args[1].AsString();
        }
        else if (args.Count > 1 && args[1].Type != ValueType.Null)
        {
            throw new Exception("compileMALDA() outputPath must be a string when provided");
        }
        
        // Extract optional mode parameter.
        // Enum names match MaldaLang.Compiler.CompilationMode (loaded by reflection).
        string modeEnumName = "Interpreter";
        string modeLabel = "interpreter";
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            var modeStr = args[2].AsString().ToLowerInvariant();
            if (modeStr == "transpile" || modeStr == "transpiletocsharp")
            {
                modeEnumName = "TranspileToCSharp";
                modeLabel = "transpile";
            }
            else if (modeStr == "js" || modeStr == "javascript")
            {
                modeEnumName = "JavaScript";
                modeLabel = "js";
            }
            else if (modeStr != "interpreter")
            {
                throw new Exception($"compileMALDA() mode must be 'interpreter', 'transpile', or 'js', got '{modeStr}'");
            }
        }
        else if (args.Count > 2 && args[2].Type != ValueType.Null)
        {
            throw new Exception("compileMALDA() mode must be a string when provided");
        }

        // Optional embed folder: "path" or "path=alias" (same as CLI --embed-folder)
        string[]? embedFolderArgs = null;
        if (args.Count > 3)
        {
            if (args[3].Type != ValueType.String)
                throw new Exception("compileMALDA() embedFolder must be a string (path or path=alias)");
            var embedFolder = args[3].AsString();
            if (!string.IsNullOrWhiteSpace(embedFolder))
                embedFolderArgs = new[] { embedFolder };
        }
        
        // Resolve file path
        string filePath;
        if (Path.IsPathRooted(sourcePath))
        {
            filePath = Path.GetFullPath(sourcePath);
        }
        else
        {
            filePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourcePath));
        }
        
        // Check for path traversal attempts
        if (sourcePath.Contains("..") || sourcePath.Contains("~"))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
            var errorsArray = new List<RuntimeValue>();
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            return RuntimeValue.Object(errorObj);
        }
        
        // Check if file exists
        if (!File.Exists(filePath))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Error: Source file not found: {filePath}"));
            var errorsArray = new List<RuntimeValue>();
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            return RuntimeValue.Object(errorObj);
        }
        
        // Determine output path
        string finalOutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            finalOutputPath = Path.ChangeExtension(filePath, modeEnumName == "JavaScript" ? ".js" : ".exe");
        }
        else
        {
            if (Path.IsPathRooted(outputPath))
            {
                finalOutputPath = Path.GetFullPath(outputPath);
            }
            else
            {
                finalOutputPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, outputPath));
            }
        }
        
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
        
        try
        {
            // Call the compiler using reflection to avoid circular dependency
            var compilerAssembly = System.Reflection.Assembly.LoadFrom(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MaldaLang.Compiler.dll"));
            var compilerType = compilerAssembly.GetType("MaldaLang.Compiler.Compiler");
            var compilationModeType = compilerAssembly.GetType("MaldaLang.Compiler.CompilationMode");
            
            if (compilerType == null || compilationModeType == null)
            {
                throw new Exception("Compiler assembly not found. Please ensure MaldaLang.Compiler.dll is available.");
            }
            
            var compiler = Activator.CreateInstance(compilerType);
            var modeEnum = Enum.Parse(compilationModeType, modeEnumName);

            // Prefer the overload that accepts --embed-folder args so portable ASK builds
            // can pack a directory without shelling out to the CLI.
            var compileMethod = compilerType.GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Compile")
                        return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 9
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(string)
                        && parameters[2].ParameterType == compilationModeType
                        && parameters[8].ParameterType == typeof(string[]);
                });

            if (compileMethod == null)
            {
                compileMethod = compilerType.GetMethod("Compile", new[]
                {
                    typeof(string),
                    typeof(string),
                    compilationModeType,
                    typeof(bool)
                });
            }

            if (compileMethod == null)
            {
                throw new Exception("Compiler.Compile method not found.");
            }

            object? result;
            if (compileMethod.GetParameters().Length == 9)
            {
                result = compileMethod.Invoke(compiler, new object?[]
                {
                    filePath,
                    finalOutputPath,
                    modeEnum,
                    false, // includeLLamaSharp
                    false, // includeUiHost
                    null,  // profilingOptions
                    1,     // typedTranspileLevel
                    false, // includeOptionalPacks
                    embedFolderArgs
                });
            }
            else
            {
                if (embedFolderArgs != null && embedFolderArgs.Length > 0)
                {
                    throw new Exception("compileMALDA() embedFolder requires a compiler that supports --embed-folder.");
                }
                result = compileMethod.Invoke(compiler, new object[] { filePath, finalOutputPath, modeEnum, false });
            }
            
            // Access result properties using reflection
            var resultType = result?.GetType();
            var successProp = resultType?.GetProperty("Success");
            var outputPathProp = resultType?.GetProperty("OutputPath");
            var errorMessageProp = resultType?.GetProperty("ErrorMessage");
            
            var success = (bool)(successProp?.GetValue(result) ?? false);
            var outputPathValue = outputPathProp?.GetValue(result) as string;
            var errorMessage = errorMessageProp?.GetValue(result) as string;
            
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(success));
            resultObj.Set("outputPath", success && outputPathValue != null 
                ? RuntimeValue.String(outputPathValue) 
                : RuntimeValue.Null());
            
            // Build errors array
            var errorsArray = new List<RuntimeValue>();
            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                // Try to parse errors from the compiler
                // The compiler may return parse errors or compilation errors
                // We'll create a structured error entry
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(errorMessage));
                errorEntry.Set("line", RuntimeValue.Integer(0));
                errorEntry.Set("column", RuntimeValue.Integer(0));
                errorsArray.Add(RuntimeValue.Object(errorEntry));
                
                resultObj.Set("error", RuntimeValue.String(errorMessage));
            }
            else
            {
                resultObj.Set("error", RuntimeValue.String(""));
            }
            
            resultObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            // Trace compileMALDA result
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath = outputPathValue,
                        mode = modeLabel,
                        success,
                        error = success ? "" : errorMessage,
                        errors = errorsArray.Select<RuntimeValue, object>(rv =>
                        {
                            if (rv.Type == ValueType.Object && rv.AsObject() is JsonObject obj)
                            {
                                var msgVal = obj.Get("message");
                                var lineVal = obj.Get("line");
                                var columnVal = obj.Get("column");

                                var msg = msgVal.Type == ValueType.String ? msgVal.AsString() : "";
                                var line = lineVal.Type == ValueType.Integer ? lineVal.AsInteger() : 0;
                                var column = columnVal.Type == ValueType.Integer ? columnVal.AsInteger() : 0;

                                return new
                                {
                                    message = msg,
                                    file = (string?)null,
                                    line,
                                    column
                                };
                            }
                            return new
                            {
                                message = (string?)null,
                                file = (string?)null,
                                line = (long)0,
                                column = (long)0
                            } as object;
                        }).ToList()
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (ParseException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Syntax error: {ex.Message}"));
            
            var errorsArray = new List<RuntimeValue>();
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            errorsArray.Add(RuntimeValue.Object(errorEntry));
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath,
                        mode = modeLabel,
                        success = false,
                        error = $"Syntax error: {ex.Message}",
                        errors = new[]
                        {
                            new
                            {
                                message = ex.Message,
                                file = (string?)null,
                                line = (long)ex.Line,
                                column = (long)ex.Column
                            }
                        }
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Compilation error: {ex.Message}"));
            
            var errorsArray = new List<RuntimeValue>();
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            errorsArray.Add(RuntimeValue.Object(errorEntry));
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath,
                        mode = modeLabel,
                        success = false,
                        error = $"Compilation error: {ex.Message}",
                        errors = new[]
                        {
                            new
                            {
                                message = ex.Message,
                                file = (string?)null,
                                line = (long)0,
                                column = (long)0
                            }
                        }
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
    }
}
