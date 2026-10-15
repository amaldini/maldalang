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
    private RuntimeValue? ExecuteFileTool(string canonicalName, ToolInstance tool, RuntimeValue arguments, ObjectInstance argsObj, string? resolvedFilePath, string? resolvedDirPath)
    {
        switch (canonicalName)
        {
            case "read_file":
                if (resolvedFilePath == null)
                    return RuntimeValue.String("Error: filePath parameter required");
                
                var readArgs = new List<RuntimeValue> { RuntimeValue.String(resolvedFilePath) };
                
                // Check for optional line range parameters
                try
                {
                    var startLineVal = argsObj.Get("startLine", null);
                    if (startLineVal != null && startLineVal.Type == ValueType.Integer)
                    {
                        readArgs.Add(startLineVal);
                        
                        var endLineVal = argsObj.Get("endLine", null);
                        if (endLineVal != null && endLineVal.Type == ValueType.Integer)
                        {
                            readArgs.Add(endLineVal);
                        }
                    }
                }
                catch { }
                
                return BuiltInFunctions.CallBuiltIn("readFile", readArgs, null);

            case "write_file":
                if (resolvedFilePath == null)
                    return RuntimeValue.String("Error: filePath parameter required");
                try
                {
                    var contentVal = argsObj.Get("content", null);
                    if (contentVal == null || contentVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: content parameter required");
                    
                    var content = contentVal.AsString();
                    const int maxContentLength = 50000;
                    if (content.Length > maxContentLength)
                    {
                        return RuntimeValue.String($"Error: Content length ({content.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the content into smaller chunks or use replace_in_file for partial updates.");
                    }
                    
                    return BuiltInFunctions.CallBuiltIn("writeFile", new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(resolvedFilePath), 
                        contentVal 
                    }, null);
                }
                catch
                {
                    return RuntimeValue.String("Error: content parameter required");
                }

            case "replace_in_file":
                if (resolvedFilePath == null)
                    return RuntimeValue.String("Error: filePath parameter required");
                try
                {
                    var oldTextVal = argsObj.Get("oldText", null);
                    var newTextVal = argsObj.Get("newText", null);
                    var contextLinesVal = argsObj.Get("contextLines", null);
                    
                    if (oldTextVal == null || oldTextVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: oldText parameter required");
                    if (newTextVal == null || newTextVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: newText parameter required");
                    
                    var oldText = oldTextVal.AsString();
                    var newText = newTextVal.AsString();
                    
                    // Validate against LLM hallucinations: detect excessive consecutive newlines
                    // This catches cases where LLM generates excessive \u000A sequences that get converted to \n
                    const int maxConsecutiveNewlines = 10;
                    var consecutiveNewlines = 0;
                    var maxConsecutiveFound = 0;
                    for (int i = 0; i < oldText.Length; i++)
                    {
                        // This will catch both \n (LF), \r (CR), and \r\n (CRLF)
                        // All Unicode escape sequences like \u000A have already been converted to \n by JSON parser
                        if (oldText[i] == '\n' || oldText[i] == '\r')
                        {
                            consecutiveNewlines++;
                            maxConsecutiveFound = Math.Max(maxConsecutiveFound, consecutiveNewlines);
                        }
                        else if (oldText[i] != ' ' && oldText[i] != '\t')
                        {
                            // Reset counter on non-whitespace character
                            consecutiveNewlines = 0;
                        }
                    }
                    
                    if (maxConsecutiveFound > maxConsecutiveNewlines)
                    {
                        return RuntimeValue.String($"Error: oldText contains {maxConsecutiveFound} consecutive newlines, which exceeds the maximum allowed ({maxConsecutiveNewlines}). " +
                            $"This appears to be an error. oldText should contain actual code/text to replace, not just whitespace. " +
                            $"Please provide the actual text content to replace, not just empty lines.");
                    }
                    
                    // Check if oldText is mostly whitespace (another sign of hallucination)
                    var nonWhitespaceChars = 0;
                    foreach (var c in oldText)
                    {
                        if (!char.IsWhiteSpace(c))
                            nonWhitespaceChars++;
                    }
                    if (oldText.Length > 100 && nonWhitespaceChars < oldText.Length * 0.1)
                    {
                        return RuntimeValue.String($"Error: oldText is mostly whitespace ({nonWhitespaceChars} non-whitespace characters out of {oldText.Length} total). " +
                            $"oldText should contain actual code/text content to replace. Please provide the actual text, not just whitespace or newlines.");
                    }
                    
                    // Validate content lengths
                    const int maxContentLength = 50000;
                    if (oldText.Length > maxContentLength)
                    {
                        return RuntimeValue.String($"Error: oldText length ({oldText.Length} characters) exceeds maximum allowed length ({maxContentLength} characters).");
                    }
                    if (newText.Length > maxContentLength)
                    {
                        return RuntimeValue.String($"Error: newText length ({newText.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the replacement into smaller chunks.");
                    }
                    
                    var args = new List<RuntimeValue>
                    {
                        RuntimeValue.String(resolvedFilePath),
                        oldTextVal,
                        newTextVal
                    };
                    
                    if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                        args.Add(contextLinesVal);
                    else
                        args.Add(RuntimeValue.Integer(3));
                    
                    var result = BuiltInFunctions.CallBuiltIn("replaceInFile", args, null);
                    
                    // If the result is false, provide a descriptive error message for the LLM
                    if (result.Type == ValueType.Boolean && !result.AsBoolean())
                    {
                        // Check if file exists to provide more specific error
                        if (!System.IO.File.Exists(resolvedFilePath))
                        {
                            return RuntimeValue.String($"Error: File not found: '{resolvedFilePath}'. Cannot perform replacement.");
                        }
                        
                        // Provide helpful error message explaining why replacement failed
                        var oldTextPreview = oldText.Length > 50 ? oldText.Substring(0, 50) + "..." : oldText;
                        // Escape special characters for display
                        var escapedOldText = oldTextPreview.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                        
                        return RuntimeValue.String($"Error: The text to replace was not found in the file '{resolvedFilePath}'. " +
                            $"The oldText '{escapedOldText}' does not exist in the file (after whitespace normalization). " +
                            $"Please verify that the text exists in the file or check for encoding/whitespace differences.");
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error: {ex.Message}");
                }

            case "edit_file":
                if (resolvedFilePath == null)
                    return RuntimeValue.String("Error: filePath parameter required");
                try
                {
                    var editsVal = argsObj.Get("edits", null);
                    if (editsVal == null || editsVal.Type != ValueType.Array)
                        return RuntimeValue.String("Error: edits parameter required and must be an array");
                    
                    var editsArray = editsVal.AsArray();
                    if (editsArray.Count == 0)
                    {
                        // Empty edits array - return success with applied=0
                        var resultObj = new JsonObject();
                        resultObj.Set("success", RuntimeValue.Boolean(true));
                        resultObj.Set("applied", RuntimeValue.Integer(0));
                        return RuntimeValue.Object(resultObj);
                    }
                    
                    // Convert edits array to the format expected by editFile()
                    var editList = new List<RuntimeValue>();
                    foreach (var editValue in editsArray)
                    {
                        if (editValue.Type != ValueType.Object)
                            continue;
                        
                        var editObj = editValue.AsObject();
                        RuntimeValue? oldTextVal = null;
                        RuntimeValue? newTextVal = null;
                        RuntimeValue? contextLinesVal = null;
                        
                        try { oldTextVal = editObj.Get("oldText", null); } catch { }
                        try { newTextVal = editObj.Get("newText", null); } catch { }
                        try { contextLinesVal = editObj.Get("contextLines", null); } catch { }
                        
                        if (oldTextVal == null || oldTextVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: Each edit must have 'oldText' (string) property");
                        if (newTextVal == null || newTextVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: Each edit must have 'newText' (string) property");
                        
                        var oldText = oldTextVal.AsString();
                        var newText = newTextVal.AsString();
                        
                        // Apply same validation as replace_in_file
                        const int maxConsecutiveNewlines = 10;
                        var consecutiveNewlines = 0;
                        var maxConsecutiveFound = 0;
                        for (int i = 0; i < oldText.Length; i++)
                        {
                            if (oldText[i] == '\n' || oldText[i] == '\r')
                            {
                                consecutiveNewlines++;
                                maxConsecutiveFound = Math.Max(maxConsecutiveFound, consecutiveNewlines);
                            }
                            else if (oldText[i] != ' ' && oldText[i] != '\t')
                            {
                                consecutiveNewlines = 0;
                            }
                        }
                        
                        if (maxConsecutiveFound > maxConsecutiveNewlines)
                        {
                            return RuntimeValue.String($"Error: oldText in one of the edits contains {maxConsecutiveFound} consecutive newlines, which exceeds the maximum allowed ({maxConsecutiveNewlines}). oldText should contain actual code/text to replace, not just whitespace.");
                        }
                        
                        // Check if oldText is mostly whitespace
                        var nonWhitespaceChars = 0;
                        foreach (var c in oldText)
                        {
                            if (!char.IsWhiteSpace(c))
                                nonWhitespaceChars++;
                        }
                        if (oldText.Length > 100 && nonWhitespaceChars < oldText.Length * 0.1)
                        {
                            return RuntimeValue.String($"Error: oldText in one of the edits is mostly whitespace ({nonWhitespaceChars} non-whitespace characters out of {oldText.Length} total). oldText should contain actual code/text content to replace.");
                        }
                        
                        // Validate content lengths
                        const int maxContentLength = 50000;
                        if (oldText.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: oldText length ({oldText.Length} characters) in one of the edits exceeds maximum allowed length ({maxContentLength} characters).");
                        }
                        if (newText.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: newText length ({newText.Length} characters) in one of the edits exceeds maximum allowed length ({maxContentLength} characters). Please split the edits into smaller chunks.");
                        }
                        
                        // Create edit object
                        var editObjResult = new JsonObject();
                        editObjResult.Set("oldText", oldTextVal);
                        editObjResult.Set("newText", newTextVal);
                        if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                            editObjResult.Set("contextLines", contextLinesVal);
                        else
                            editObjResult.Set("contextLines", RuntimeValue.Integer(3));
                        
                        editList.Add(RuntimeValue.Object(editObjResult));
                    }
                    
                    // Call editFile built-in function
                    var editFileArgs = new List<RuntimeValue>
                    {
                        RuntimeValue.String(resolvedFilePath),
                        RuntimeValue.Array(editList)
                    };
                    
                    var editFileResult = BuiltInFunctions.CallBuiltIn("editFile", editFileArgs, null);
                    
                    // editFile returns {success: boolean, applied: integer}
                    return editFileResult;
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing edit_file tool: {ex.Message}");
                }

            case "list_directory":
                if (resolvedDirPath == null)
                    return RuntimeValue.String("Error: dirPath parameter required");
                return BuiltInFunctions.CallBuiltIn("listDirectory", new List<RuntimeValue> { RuntimeValue.String(resolvedDirPath) }, null);

            case "delete_file":
                if (resolvedFilePath == null)
                    return RuntimeValue.String("Error: filePath parameter required");
                try
                {
                    var deleteResult = BuiltInFunctions.CallBuiltIn(
                        "deleteFile",
                        new List<RuntimeValue> { RuntimeValue.String(resolvedFilePath) },
                        null);
                    var deleteOk = deleteResult.Type == ValueType.Boolean && deleteResult.AsBoolean();
                    var deleteObj = new JsonObject();
                    deleteObj.Set("success", RuntimeValue.Boolean(deleteOk));
                    if (!deleteOk)
                        deleteObj.Set("error", RuntimeValue.String("deleteFile failed"));
                    return RuntimeValue.Object(deleteObj);
                }
                catch (Exception ex)
                {
                    var deleteErr = new JsonObject();
                    deleteErr.Set("success", RuntimeValue.Boolean(false));
                    deleteErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(deleteErr);
                }

            case "copy_file":
                try
                {
                    var srcPathVal = argsObj.Get("srcPath", null);
                    var destPathVal = argsObj.Get("destPath", null);
                    if (srcPathVal == null || srcPathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: srcPath parameter required");
                    if (destPathVal == null || destPathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: destPath parameter required");

                    var srcPath = srcPathVal.AsString();
                    var destPath = destPathVal.AsString();

                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        var normalizedSrc = tool.NormalizePathForWorkingDirectory(srcPath);
                        if (normalizedSrc == null)
                        {
                            return RuntimeValue.String($"Error: Path '{srcPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                        }
                        srcPath = normalizedSrc;

                        var normalizedDest = tool.NormalizePathForWorkingDirectory(destPath);
                        if (normalizedDest == null)
                        {
                            return RuntimeValue.String($"Error: Path '{destPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                        }
                        destPath = normalizedDest;
                    }
                    else if (!tool.IsPathAllowed(srcPath))
                    {
                        return RuntimeValue.String($"Error: Path '{srcPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                    }
                    else if (!tool.IsPathAllowed(destPath))
                    {
                        return RuntimeValue.String($"Error: Path '{destPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                    }

                    var resolvedSrcPath = !string.IsNullOrEmpty(tool.WorkingDirectory)
                        ? tool.ResolvePathAgainstWorkingDirectory(srcPath) ?? srcPath
                        : srcPath;
                    var resolvedDestPath = !string.IsNullOrEmpty(tool.WorkingDirectory)
                        ? tool.ResolvePathAgainstWorkingDirectory(destPath) ?? destPath
                        : destPath;

                    var copyResult = BuiltInFunctions.CallBuiltIn(
                        "copyFile",
                        new List<RuntimeValue>
                        {
                            RuntimeValue.String(resolvedSrcPath),
                            RuntimeValue.String(resolvedDestPath)
                        },
                        null);
                    var copyOk = copyResult.Type == ValueType.Boolean && copyResult.AsBoolean();
                    var copyObj = new JsonObject();
                    copyObj.Set("success", RuntimeValue.Boolean(copyOk));
                    if (!copyOk)
                        copyObj.Set("error", RuntimeValue.String("copyFile failed"));
                    return RuntimeValue.Object(copyObj);
                }
                catch (Exception ex)
                {
                    var copyErr = new JsonObject();
                    copyErr.Set("success", RuntimeValue.Boolean(false));
                    copyErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(copyErr);
                }

            case "ensure_dir":
                if (resolvedDirPath == null)
                    return RuntimeValue.String("Error: dirPath parameter required");
                try
                {
                    BuiltInFunctions.CallBuiltIn(
                        "ensureDir",
                        new List<RuntimeValue> { RuntimeValue.String(resolvedDirPath) },
                        null);
                    var ensureObj = new JsonObject();
                    ensureObj.Set("success", RuntimeValue.Boolean(true));
                    ensureObj.Set("path", RuntimeValue.String(resolvedDirPath));
                    return RuntimeValue.Object(ensureObj);
                }
                catch (Exception ex)
                {
                    var ensureErr = new JsonObject();
                    ensureErr.Set("success", RuntimeValue.Boolean(false));
                    ensureErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(ensureErr);
                }

            case "insert_at_line":
            case "insertAtLine":
                try
                {
                    var filePathVal = argsObj.Get("filePath", null);
                    var lineNumberVal = argsObj.Get("lineNumber", null);
                    var contentVal = argsObj.Get("content", null);
                    
                    if (filePathVal == null || filePathVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: filePath parameter required");
                    if (lineNumberVal == null || lineNumberVal.Type != ValueType.Integer)
                        return RuntimeValue.String("Error: lineNumber parameter required");
                    if (contentVal == null || contentVal.Type != ValueType.String)
                        return RuntimeValue.String("Error: content parameter required");
                    
                    var localFilePath = filePathVal.AsString();
                    var lineNumber = lineNumberVal.AsInteger();
                    var content = contentVal.AsString();
                    
                    // Validate content length
                    const int maxContentLength = 50000;
                    if (content.Length > maxContentLength)
                    {
                        return RuntimeValue.String($"Error: Content length ({content.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the content into smaller chunks.");
                    }
                    
                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        var normalizedLocalPath = tool.NormalizePathForWorkingDirectory(localFilePath);
                        if (normalizedLocalPath == null)
                        {
                            return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                        }
                        localFilePath = normalizedLocalPath;
                    }
                    else if (!tool.IsPathAllowed(localFilePath))
                    {
                        return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                    }
                    
                    // Resolve path relative to working directory
                    string localResolvedFilePath;
                    if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                    {
                        localResolvedFilePath = Path.Combine(tool.WorkingDirectory, localFilePath);
                    }
                    else
                    {
                        localResolvedFilePath = localFilePath;
                    }
                    
                    // Extract optional insertAfter parameter
                    var insertAfter = false;
                    try
                    {
                        var insertAfterVal = argsObj.Get("insertAfter", null);
                        if (insertAfterVal != null && insertAfterVal.Type == ValueType.Boolean)
                            insertAfter = insertAfterVal.AsBoolean();
                    }
                    catch { }
                    
                    // Build arguments list for BuiltInInsertAtLine
                    var insertArgs = new List<RuntimeValue>
                    {
                        RuntimeValue.String(localResolvedFilePath),
                        RuntimeValue.Integer(lineNumber),
                        RuntimeValue.String(content),
                        RuntimeValue.Boolean(insertAfter)
                    };
                    
                    var result = BuiltInFunctions.CallBuiltIn("insertAtLine", insertArgs, null);
                    
                    // Return success message or error
                    if (result.Type == ValueType.Boolean && result.AsBoolean())
                    {
                        return RuntimeValue.String("Success: Content inserted at line " + lineNumber);
                    }
                    else
                    {
                        return RuntimeValue.String("Error: Failed to insert content. File may not exist or there was an error writing to the file.");
                    }
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error executing insertAtLine tool: {ex.Message}");
                }

            default:
                return null;
        }
    }
}
