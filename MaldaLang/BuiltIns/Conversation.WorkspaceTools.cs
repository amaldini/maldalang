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
    private RuntimeValue? ExecuteWorkspaceTool(string canonicalName, ToolInstance tool, RuntimeValue arguments, ObjectInstance argsObj, string? resolvedFilePath, string? resolvedDirPath)
    {
        switch (canonicalName)
        {
            case "ask_user":
                try
                {
                    var questionVal = argsObj.Get("question", null);
                    if (questionVal == null || questionVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: question parameter required");
                    
                    var question = questionVal.AsString();
                    
                    // Try to get input from input provider if available
                    if (_inputProvider != null)
                    {
                        // Check for queued input first
                        if (_inputProvider.HasQueuedInput())
                        {
                            var queuedInput = _inputProvider.GetQueuedInput();
                            return RuntimeValue.String(queuedInput);
                        }
                        
                        // No queued input - use GetInputAsync to request input from UI
                        // Since ExecuteToolOperation is synchronous, we need to block on the async call
                        // This is safe because execution happens in a background thread (Task.Run)
                        try
                        {
                            var inputTask = _inputProvider.GetInputAsync(question);
                            var input = inputTask.GetAwaiter().GetResult();
                            return RuntimeValue.String(input ?? "");
                        }
                        catch (Exception asyncEx)
                        {
                            // If async input fails, fall back to exception-based approach for compatibility
                            Console.WriteLine(question);
                            Console.WriteLine("(Waiting for input...)");
                            throw new InputRequiredException(question);
                        }
                    }
                    
                    // Fallback to console input
                    Console.Write(question);
                    var consoleInput = Console.ReadLine() ?? "";
                    return RuntimeValue.String(consoleInput);
                }
                catch (InputRequiredException)
                {
                    // Re-throw to let execution service handle it
                    throw;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error asking user: {ex.Message}");
                }

            case "glob":
                return BuiltInTools.ExecuteGlob(tool, arguments);

            case "grep":
                try
                {
                    var patternVal = argsObj.Get("pattern", null);
                    var filePathVal = argsObj.Get("filePath", null) ?? argsObj.Get("file_path", null);
                    
                    if (patternVal == null || patternVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: pattern parameter required");
                    if (filePathVal == null || filePathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: filePath parameter required. Example: use filePath: \"MALDA_SPEC.md\" to search the language spec.");
                    
                    var pattern = patternVal.AsString();
                    var localFilePath = filePathVal.AsString();
                    
                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        var normalizedGrepPath = tool.NormalizePathForWorkingDirectory(localFilePath);
                        if (normalizedGrepPath == null)
                        {
                            return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                        }
                        localFilePath = normalizedGrepPath;
                    }
                    else if (!tool.IsPathAllowed(localFilePath))
                    {
                        return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                    }
                    
                    // If pattern contains "|" (alternation) it is intended as regex; default useRegex to true so it matches
                    var useRegexFromArgs = argsObj.Get("useRegex", null);
                    var useRegexExplicit = useRegexFromArgs != null && useRegexFromArgs.Type == ValueType.Boolean;
                    
                    // Resolve path relative to working directory (disk or embed:)
                    string localResolvedFilePath;
                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        localResolvedFilePath = tool.ResolvePathAgainstWorkingDirectory(localFilePath) ?? localFilePath;
                    }
                    else
                    {
                        localResolvedFilePath = localFilePath;
                    }
                    
                    // Extract optional parameters
                    var useRegex = false;
                    var caseInsensitive = false;
                    var includeLineNumbers = true;
                    var contextLines = 3;
                    var countOnly = false;
                    var recursive = true;
                    
                    try
                    {
                        var useRegexVal = argsObj.Get("useRegex", null);
                        if (useRegexVal != null && useRegexVal.Type == ValueType.Boolean)
                            useRegex = useRegexVal.AsBoolean();
                    }
                    catch { }
                    
                    // Pattern with "|" is alternation (e.g. "FUNCTIONS|function"); treat as regex so it matches
                    if (!useRegexExplicit && pattern.Contains("|"))
                        useRegex = true;
                    
                    try
                    {
                        var caseInsensitiveVal = argsObj.Get("caseInsensitive", null);
                        if (caseInsensitiveVal != null && caseInsensitiveVal.Type == ValueType.Boolean)
                            caseInsensitive = caseInsensitiveVal.AsBoolean();
                    }
                    catch { }
                    
                    try
                    {
                        var includeLineNumbersVal = argsObj.Get("includeLineNumbers", null);
                        if (includeLineNumbersVal != null && includeLineNumbersVal.Type == ValueType.Boolean)
                            includeLineNumbers = includeLineNumbersVal.AsBoolean();
                    }
                    catch { }
                    
                    try
                    {
                        var contextLinesVal = argsObj.Get("contextLines", null);
                        if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                            contextLines = contextLinesVal.AsInteger();
                    }
                    catch { }
                    
                    try
                    {
                        var countOnlyVal = argsObj.Get("countOnly", null);
                        if (countOnlyVal != null && countOnlyVal.Type == ValueType.Boolean)
                            countOnly = countOnlyVal.AsBoolean();
                    }
                    catch { }
                    
                    try
                    {
                        var recursiveVal = argsObj.Get("recursive", null);
                        if (recursiveVal != null && recursiveVal.Type == ValueType.Boolean)
                            recursive = recursiveVal.AsBoolean();
                    }
                    catch { }
                    
                    // Build arguments list for BuiltInGrep (9th arg = workingDirectory so grep returns relative paths in results)
                    var grepArgs = new List<RuntimeValue>
                    {
                        RuntimeValue.String(pattern),
                        RuntimeValue.String(localResolvedFilePath),
                        RuntimeValue.Boolean(useRegex),
                        RuntimeValue.Boolean(caseInsensitive),
                        RuntimeValue.Boolean(includeLineNumbers),
                        RuntimeValue.Integer(contextLines),
                        RuntimeValue.Boolean(countOnly),
                        RuntimeValue.Boolean(recursive),
                        RuntimeValue.String(tool.WorkingDirectory ?? "")
                    };
                    
                    return BuiltInFunctions.CallBuiltIn("grep", grepArgs, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing grep tool: {ex.Message}");
                }

            case "web_search":
                try
                {
                    var queryVal = argsObj.Get("query", null);
                    if (queryVal == null || queryVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: query parameter required");
                    var query = queryVal.AsString();
                    if (string.IsNullOrWhiteSpace(query))
                        return RuntimeValue.String("Error: query cannot be empty");
                    var webSearchArgs = new List<RuntimeValue> { RuntimeValue.String(query) };
                    try
                    {
                        var apiKeyVal = argsObj.Get("apiKey", null);
                        if (apiKeyVal != null && apiKeyVal.Type == ValueType.String && !string.IsNullOrWhiteSpace(apiKeyVal.AsString()))
                            webSearchArgs.Add(apiKeyVal);
                    }
                    catch { }
                    return BuiltInFunctions.CallBuiltIn("webSearch", webSearchArgs, null);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing web_search tool: {ex.Message}");
                }

            case "web_fetch":
                try
                {
                    if (tool.Capability != null)
                        arguments = CapStdLib.ApplyHttpTokenToWebFetchArgs(tool.Capability, arguments);
                    return BuiltInTools.ExecuteWebFetch(arguments);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing web_fetch tool: {ex.Message}");
                }

            case "run_command":
                try
                {
                    var commandVal = argsObj.Get("command", null);
                    if (commandVal == null || commandVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: command parameter required");
                    
                    var command = commandVal.AsString();
                    if (string.IsNullOrWhiteSpace(command))
                        return RuntimeValue.String("Error: command cannot be empty");

                    // Extract optional args array
                    RuntimeValue? argsArray = null;
                    try
                    {
                        var argsVal = argsObj.Get("args", null);
                        if (argsVal != null && argsVal.Type == ValueType.Array)
                            argsArray = argsVal;
                    }
                    catch { }
                    
                    // Extract optional working directory
                    string? cmdWorkingDirectory = null;
                    try
                    {
                        var workingDirVal = argsObj.Get("workingDirectory", null);
                        if (workingDirVal != null && workingDirVal.Type == ValueType.String)
                        {
                            cmdWorkingDirectory = workingDirVal.AsString();
                            
                            // Validate working directory if tool has restrictions
                            if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(cmdWorkingDirectory))
                            {
                                return RuntimeValue.String($"Error: Working directory '{cmdWorkingDirectory}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                            }
                        }
                    }
                    catch { }
                    
                    // Use tool's working directory as default if not provided
                    if (string.IsNullOrEmpty(cmdWorkingDirectory) && !string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        cmdWorkingDirectory = tool.WorkingDirectory;
                    }
                    
                    // Extract optional timeout
                    RuntimeValue? timeoutVal = null;
                    try
                    {
                        var timeoutMsVal = argsObj.Get("timeoutMs", null);
                        if (timeoutMsVal != null && timeoutMsVal.Type == ValueType.Integer)
                            timeoutVal = timeoutMsVal;
                    }
                    catch { }
                    
                    // Build arguments list for BuiltInRunCommand
                    var runCommandArgs = new List<RuntimeValue> { RuntimeValue.String(command) };
                    
                    if (argsArray != null)
                        runCommandArgs.Add(argsArray);
                    else
                        runCommandArgs.Add(RuntimeValue.Null());
                    
                    if (!string.IsNullOrEmpty(cmdWorkingDirectory))
                        runCommandArgs.Add(RuntimeValue.String(cmdWorkingDirectory));
                    else
                        runCommandArgs.Add(RuntimeValue.Null());
                    
                    if (timeoutVal != null)
                        runCommandArgs.Add(timeoutVal);
                    else
                        runCommandArgs.Add(RuntimeValue.Null());

                    // Parse args for approval display
                    List<string>? argStrings = null;
                    if (argsArray != null)
                    {
                        argStrings = new List<string>();
                        foreach (var arg in argsArray.AsArray())
                        {
                            if (arg.Type == ValueType.String)
                                argStrings.Add(arg.AsString());
                        }
                    }

                    if (tool.Capability != null)
                        CapStdLib.RequireCommandUnderShell(tool.Capability, command, argStrings);

                    var approval = CommandApprovalService.EnsureApprovedAsync(
                        _inputProvider, command, argStrings, cmdWorkingDirectory
                    ).GetAwaiter().GetResult();
                    if (!approval.Approved)
                        return RuntimeValue.String(approval.ErrorMessage ?? "Error: Command not approved");

                    RuntimeValue result;
                    using (CommandExecutionContext.EnterUserApprovedScope())
                    {
                        result = BuiltInFunctions.CallBuiltIn("runCommand", runCommandArgs, null);
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing run_command tool: {ex.Message}");
                }

            default:
                return null;
        }
    }
}
