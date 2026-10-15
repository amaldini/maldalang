// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private RuntimeValue? ExecuteMaldaTool(string canonicalName, ToolInstance tool, RuntimeValue arguments, ObjectInstance argsObj, string? resolvedFilePath, string? resolvedDirPath)
    {
        switch (canonicalName)
        {
            case "run_malda":
                try
                {
                    var sourceOrFilePathVal = argsObj.Get("sourceOrFilePath", null);
                    if (sourceOrFilePathVal == null || sourceOrFilePathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: sourceOrFilePath parameter required");
                    
                    var sourceOrFilePath = sourceOrFilePathVal.AsString();
                    if (string.IsNullOrWhiteSpace(sourceOrFilePath))
                        return RuntimeValue.String("Error: sourceOrFilePath cannot be empty");
                    
                    // Extract optional input parameter
                    RuntimeValue? inputVal = null;
                    try
                    {
                        var inputParam = argsObj.Get("input", null);
                        if (inputParam != null && inputParam.Type == ValueType.String)
                            inputVal = inputParam;
                    }
                    catch { }
                    
                    // Build arguments list for BuiltInRunMALDA
                    var runMaldaArgs = new List<RuntimeValue> { RuntimeValue.String(sourceOrFilePath) };
                    
                    if (inputVal != null)
                        runMaldaArgs.Add(inputVal);
                    
                    var result = BuiltInFunctions.CallBuiltIn("runMALDA", runMaldaArgs, null);
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing run_malda tool: {ex.Message}");
                }

            case "compile_malda":
                try
                {
                    var sourcePathVal = argsObj.Get("sourcePath", null);
                    if (sourcePathVal == null || sourcePathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: sourcePath parameter required");
                    
                    var sourcePath = sourcePathVal.AsString();
                    if (string.IsNullOrWhiteSpace(sourcePath))
                        return RuntimeValue.String("Error: sourcePath cannot be empty");
                    
                    // Build arguments list for BuiltInCompileMALDA.
                    // outputPath is the second argument, so a mode-only call must pass null
                    // rather than shifting mode into the output path slot.
                    var compileSplArgs = new List<RuntimeValue> { RuntimeValue.String(sourcePath) };

                    RuntimeValue? outputPathParam = null;
                    RuntimeValue? modeParam = null;
                    try
                    {
                        var outputPathValue = argsObj.Get("outputPath", null);
                        if (outputPathValue != null && outputPathValue.Type == ValueType.String)
                            outputPathParam = outputPathValue;
                    }
                    catch { }

                    try
                    {
                        var modeValue = argsObj.Get("mode", null);
                        if (modeValue != null && modeValue.Type == ValueType.String)
                            modeParam = modeValue;
                    }
                    catch { }

                    if (outputPathParam != null || modeParam != null)
                        compileSplArgs.Add(outputPathParam ?? RuntimeValue.Null());
                    if (modeParam != null)
                        compileSplArgs.Add(modeParam);
                    
                    var result = BuiltInFunctions.CallBuiltIn("compileMALDA", compileSplArgs, null);
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing compile_malda tool: {ex.Message}");
                }

            case "get_symbols":
                try
                {
                    var filePathOrSourceVal = argsObj.Get("filePathOrSource", null);
                    if (filePathOrSourceVal == null || filePathOrSourceVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: filePathOrSource parameter required");
                    
                    var filePathOrSource = filePathOrSourceVal.AsString();
                    if (string.IsNullOrWhiteSpace(filePathOrSource))
                        return RuntimeValue.String("Error: filePathOrSource cannot be empty");
                    
                    string sourceCode = filePathOrSource;
                    
                    // If it looks like a file path, read the file first
                    if ((filePathOrSource.Contains(Path.DirectorySeparatorChar) || 
                         filePathOrSource.Contains(Path.AltDirectorySeparatorChar) ||
                         filePathOrSource.EndsWith(".malda", StringComparison.OrdinalIgnoreCase)))
                    {
                        string getSymbolslFilePath;
                        if (Path.IsPathRooted(filePathOrSource))
                        {
                            getSymbolslFilePath = Path.GetFullPath(filePathOrSource);
                        }
                        else if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            getSymbolslFilePath = Path.Combine(tool.WorkingDirectory, filePathOrSource);
                        }
                        else
                        {
                            getSymbolslFilePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, filePathOrSource));
                        }
                        
                        // Validate path if it's a file path
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(getSymbolslFilePath))
                        {
                            return RuntimeValue.String($"Error: Path '{getSymbolslFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                        }
                        
                        // Check for path traversal attempts
                        if (filePathOrSource.Contains("..") || filePathOrSource.Contains("~"))
                        {
                            return RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)");
                        }
                        
                        // Read the file content
                        if (File.Exists(getSymbolslFilePath))
                        {
                            sourceCode = File.ReadAllText(getSymbolslFilePath);
                        }
                        else
                        {
                            return RuntimeValue.String($"Error: File not found: {getSymbolslFilePath}");
                        }
                    }
                    
                    // Build arguments list for BuiltInGetSymbols (pass source code, not file path)
                    var getSymbolsArgs = new List<RuntimeValue> { RuntimeValue.String(sourceCode) };
                    
                    var result = BuiltInFunctions.CallBuiltIn("getSymbols", getSymbolsArgs, null);
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing get_symbols tool: {ex.Message}");
                }

            case "get_parse_errors":
                try
                {
                    var sourceOrFilePathVal = argsObj.Get("sourceOrFilePath", null);
                    if (sourceOrFilePathVal == null || sourceOrFilePathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: sourceOrFilePath parameter required");
                    
                    var sourceOrFilePath = sourceOrFilePathVal.AsString();
                    if (string.IsNullOrWhiteSpace(sourceOrFilePath))
                        return RuntimeValue.String("Error: sourceOrFilePath cannot be empty");
                    
                    string sourceCode = sourceOrFilePath;
                    
                    if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
                         sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
                         sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
                    {
                        string resolvedPath;
                        if (Path.IsPathRooted(sourceOrFilePath))
                        {
                            resolvedPath = Path.GetFullPath(sourceOrFilePath);
                        }
                        else if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            resolvedPath = Path.Combine(tool.WorkingDirectory, sourceOrFilePath);
                        }
                        else
                        {
                            resolvedPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                        }
                        
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(resolvedPath))
                        {
                            return RuntimeValue.String($"Error: Path '{resolvedPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                        }
                        
                        if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                        {
                            return RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)");
                        }
                        
                        if (File.Exists(resolvedPath))
                        {
                            sourceCode = File.ReadAllText(resolvedPath);
                        }
                        else
                        {
                            return RuntimeValue.String($"Error: File not found: {resolvedPath}");
                        }
                    }
                    
                    var getParseErrorsArgs = new List<RuntimeValue> { RuntimeValue.String(sourceCode) };
                    var result = BuiltInFunctions.CallBuiltIn("getParseErrors", getParseErrorsArgs, null);
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing get_parse_errors tool: {ex.Message}");
                }

            case "check_malda":
                try
                {
                    var checkSourceVal = argsObj.Get("sourceOrFilePath", null);
                    if (checkSourceVal == null || checkSourceVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: sourceOrFilePath parameter required");

                    var checkSource = checkSourceVal.AsString();
                    if (string.IsNullOrWhiteSpace(checkSource))
                        return RuntimeValue.String("Error: sourceOrFilePath cannot be empty");

                    string? typeMode = null;
                    var typeModeVal = argsObj.Get("typeMode", null);
                    if (typeModeVal != null && typeModeVal.Type == ValueType.String)
                        typeMode = typeModeVal.AsString();

                    return BuiltInFunctions.CheckMaldaSource(checkSource, typeMode, tool.WorkingDirectory);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing check_malda tool: {ex.Message}");
                }

            case "validate_json":
                try
                {
                    var hasSchema = false;
                    var hasValue = false;
                    RuntimeValue? schemaVal = null;
                    RuntimeValue? valueVal = null;
                    if (argsObj is JsonObject jsonArgs)
                    {
                        var props = jsonArgs.GetProperties();
                        hasSchema = props.ContainsKey("schema");
                        hasValue = props.ContainsKey("value");
                        if (hasSchema)
                            schemaVal = jsonArgs.Get("schema", null);
                        if (hasValue)
                            valueVal = jsonArgs.Get("value", null);
                    }
                    else
                    {
                        schemaVal = argsObj.Get("schema", null);
                        valueVal = argsObj.Get("value", null);
                        hasSchema = schemaVal != null && schemaVal.Type != ValueType.Null;
                        hasValue = valueVal != null;
                    }

                    if (!hasSchema || schemaVal == null || schemaVal.Type == ValueType.Null)
                    {
                        var missingSchema = new JsonObject();
                        missingSchema.Set("ok", RuntimeValue.Boolean(false));
                        missingSchema.Set("error", RuntimeValue.String("schema parameter required"));
                        return RuntimeValue.Object(missingSchema);
                    }

                    if (!hasValue || valueVal == null)
                    {
                        var missingValue = new JsonObject();
                        missingValue.Set("ok", RuntimeValue.Boolean(false));
                        missingValue.Set("error", RuntimeValue.String("value parameter required"));
                        return RuntimeValue.Object(missingValue);
                    }

                    return BuiltInFunctions.CallBuiltIn(
                        "validate",
                        new List<RuntimeValue> { schemaVal, valueVal },
                        null);
                }
                catch (Exception ex)
                {
                    var validateErr = new JsonObject();
                    validateErr.Set("ok", RuntimeValue.Boolean(false));
                    validateErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(validateErr);
                }

            case "test_malda":
                try
                {
                    var testPathVal = argsObj.Get("path", null);
                    var testPath = testPathVal != null && testPathVal.Type == ValueType.String
                        && !string.IsNullOrWhiteSpace(testPathVal.AsString())
                        ? testPathVal.AsString()
                        : ".";

                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        var normalizedTestPath = tool.NormalizePathForWorkingDirectory(testPath);
                        if (normalizedTestPath == null)
                        {
                            return RuntimeValue.String($"Error: Path '{testPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path.");
                        }

                        testPath = tool.ResolvePathAgainstWorkingDirectory(normalizedTestPath) ?? normalizedTestPath;
                    }
                    else if (!tool.IsPathAllowed(testPath))
                    {
                        return RuntimeValue.String($"Error: Path '{testPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                    }

                    string? testFilter = null;
                    var testFilterVal = argsObj.Get("filter", null);
                    if (testFilterVal != null && testFilterVal.Type == ValueType.String)
                        testFilter = testFilterVal.AsString();

                    var listOnly = false;
                    var listOnlyVal = argsObj.Get("listOnly", null);
                    if (listOnlyVal != null && listOnlyVal.Type == ValueType.Boolean)
                        listOnly = listOnlyVal.AsBoolean();

                    return BuiltInFunctions.RunTestMalda(testPath, testFilter, listOnly);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing test_malda tool: {ex.Message}");
                }

            case "create_mcp_agent_script":
                try
                {
                    var agentNameVal = argsObj.Get("agentName", null);
                    var agentRoleVal = argsObj.Get("agentRole", null);
                    var agentInstructionsVal = argsObj.Get("agentInstructions", null);
                    var toolsVal = argsObj.Get("tools", null);
                    var outputPathVal = argsObj.Get("outputPath", null);
                    if (agentNameVal == null || agentNameVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: agentName parameter required");
                    if (agentRoleVal == null || agentRoleVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: agentRole parameter required");
                    if (agentInstructionsVal == null || agentInstructionsVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: agentInstructions parameter required");
                    if (toolsVal == null || toolsVal.Type != ValueType.Array)
                        return RuntimeValue.String("Error: tools parameter required");
                    if (outputPathVal == null || outputPathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: outputPath parameter required");

                    var outputPath = outputPathVal.AsString();
                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        var normalizedOut = tool.NormalizePathForWorkingDirectory(outputPath);
                        if (normalizedOut == null)
                        {
                            return RuntimeValue.String($"Error: Path '{outputPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path.");
                        }
                        outputPath = tool.ResolvePathAgainstWorkingDirectory(normalizedOut) ?? normalizedOut;
                    }

                    var mcpArgs = new List<RuntimeValue>
                    {
                        agentNameVal,
                        agentRoleVal,
                        agentInstructionsVal,
                        toolsVal,
                        RuntimeValue.String(outputPath)
                    };
                    try
                    {
                        var modelVal = argsObj.Get("model", null);
                        if (modelVal != null && modelVal.Type == ValueType.String)
                            mcpArgs.Add(modelVal);
                    }
                    catch { }

                    return BuiltInFunctions.CallBuiltIn("createMcpAgentScript", mcpArgs, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing create_mcp_agent_script tool: {ex.Message}");
                }

            default:
                return null;
        }
    }
}
