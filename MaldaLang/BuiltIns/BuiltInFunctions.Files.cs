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
    private static RuntimeValue BuiltInReadFile(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("readFile() expects 1 to 3 arguments");
        string filePath;
        if (args[0].Type == MaldaLang.Interpreter.ValueType.String)
            filePath = args[0].AsString();
        else if (CapStdLib.TryGetToken(args[0], out var readCap) && readCap.Kind == CapabilityToken.KindFileRead)
            filePath = readCap.Path;
        else
            throw new Exception("readFile() expects (string, int?, int?)");

        try
        {
            string content;
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedded = EmbeddedFolderStore.ReadText(filePath);
                if (embedded == null)
                    return RuntimeValue.Null();
                content = embedded;
            }
            else
            {
                if (!File.Exists(filePath))
                    return RuntimeValue.Null();
                content = File.ReadAllText(filePath);
            }
            
            // If line range is specified, extract only those lines
            if (args.Count >= 2)
            {
                var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                
                int startLine;
                int endLine;
                
                if (args.Count == 2)
                {
                    // Only one line argument provided
                    if (args[1].Type == MaldaLang.Interpreter.ValueType.Integer)
                    {
                        var lineArg = args[1].AsInteger();
                        if (lineArg < 0)
                        {
                            // Negative value means "read last N lines" (e.g., -30 = last 30 lines)
                            endLine = lines.Length;
                            startLine = Math.Max(1, lines.Length + lineArg + 1);
                        }
                        else
                        {
                            // Positive value means "read from this line to end"
                            startLine = Math.Max(1, lineArg);
                            endLine = lines.Length;
                        }
                    }
                    else
                    {
                        // Non-integer, default to reading entire file
                        startLine = 1;
                        endLine = lines.Length;
                    }
                }
                else
                {
                    // Two line arguments provided (startLine and endLine)
                    startLine = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : 1;
                    endLine = args[2].Type == MaldaLang.Interpreter.ValueType.Integer 
                        ? args[2].AsInteger() 
                        : -1; // -1 means to end of file
                    
                    if (startLine < 1)
                        startLine = 1;
                    
                    // Handle negative endLine values: negative values count from the end
                    // -1 means to end of file, -30 means 30 lines from the end
                    if (endLine < 0)
                    {
                        // Convert negative index to positive (e.g., -30 in 100-line file = line 71)
                        endLine = lines.Length + endLine + 1;
                        // If negative offset was too large, clamp to startLine (can't end before start)
                        if (endLine < startLine)
                            endLine = startLine;
                        // If still invalid (shouldn't happen, but safety check), default to end of file
                        if (endLine < 1)
                            endLine = lines.Length;
                    }
                    else if (endLine == 0 || endLine > lines.Length)
                    {
                        // Zero or beyond file length, default to end of file
                        endLine = lines.Length;
                    }
                }
                
                if (startLine > lines.Length)
                    return RuntimeValue.String(""); // Empty if start is beyond file
                
                // Extract lines (1-indexed, so subtract 1 for array index)
                var selectedLines = new List<string>();
                for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
                {
                    selectedLines.Add(lines[i]);
                }
                
                return RuntimeValue.String(string.Join("\n", selectedLines));
            }
            
            return RuntimeValue.String(content);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }

    /// <summary>
    /// Reads all lines from a text file as a MALDA array of strings (UTF-8). Faster than readFile()+split() for large files.
    /// </summary>
    private static RuntimeValue BuiltInReadTextFileLines(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("readTextFileLines() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("readTextFileLines() expects a string path");

        try
        {
            var filePath = args[0].AsString();
            if (!File.Exists(filePath))
                return RuntimeValue.Null();

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var list = new List<RuntimeValue>(lines.Length);
            foreach (var line in lines)
                list.Add(RuntimeValue.String(line));

            return RuntimeValue.Array(list);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }
    
    private static string CoerceWriteFilePath(RuntimeValue value)
    {
        if (value.Type == ValueType.String)
            return value.AsString();
        if (CapStdLib.TryGetToken(value, out var token))
        {
            if (token.Kind != CapabilityToken.KindFileWrite)
                throw new Exception($"writeFile() capability kind is '{token.Kind}', expected '{CapabilityToken.KindFileWrite}'");
            return token.Path;
        }

        return value.ToString();
    }

    private static string CoerceWriteFileContent(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.String => value.AsString(),
            ValueType.Object or ValueType.Array => CallBuiltIn("toJSON", new List<RuntimeValue> { value }, null).AsString(),
            _ => value.ToString()
        };
    }

    private static RuntimeValue BuiltInWriteFile(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("writeFile() expects 2 arguments");
        var filePath = CoerceWriteFilePath(args[0]);
        RejectEmbedWrite(filePath, "writeFile");
        
        try
        {
            var content = CoerceWriteFileContent(args[1]);
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
                // Ignore read failures for tracing; file write may still succeed.
            }
            
            File.WriteAllText(filePath, content);
            
            // Trace file edit (best-effort)
            try
            {
                string? afterContent = null;
                try
                {
                    afterContent = File.ReadAllText(filePath);
                }
                catch
                {
                }
                
                TraceManager.Record(
                    TraceEventType.FileEdit,
                    new
                    {
                        path = filePath,
                        operation = "overwrite",
                        beforeContent,
                        afterContent,
                        toolName = "writeFile",
                        gitStatusBefore = (object?)null,
                        gitStatusAfter = (object?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInCopyFile(List<RuntimeValue> args)
    {
        BuiltInArity.Require("copyFile", args, 2, 2, "src, dest");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("copyFile() expects (string, string)");

        var src = args[0].AsString();
        var dest = args[1].AsString();
        if (EmbeddedFolderStore.IsEmbedPath(src))
            throw new Exception($"copyFile() cannot read from embedded path '{src}'");
        RejectEmbedWrite(dest, "copyFile");

        try
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dest))
                return RuntimeValue.Boolean(false);
            File.Copy(src, dest, overwrite: true);
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInHasFile(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasFile() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasFile() expects a string argument");
        
        try
        {
            var filePath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
                return RuntimeValue.Boolean(EmbeddedFolderStore.HasFile(filePath));
            return RuntimeValue.Boolean(File.Exists(filePath));
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInDeleteFile(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("deleteFile() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("deleteFile() expects a string argument");
        try
        {
            var filePath = args[0].AsString();
            RejectEmbedWrite(filePath, "deleteFile");
            if (string.IsNullOrEmpty(filePath)) return RuntimeValue.Boolean(false);
            if (!File.Exists(filePath)) return RuntimeValue.Boolean(true);
            File.Delete(filePath);
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInWriteFileBase64(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("writeFileBase64() expects 2 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("writeFileBase64() expects (path, base64Content)");
        try
        {
            var filePath = args[0].AsString();
            RejectEmbedWrite(filePath, "writeFileBase64");
            var base64 = args[1].AsString();
            var bytes = Convert.FromBase64String(base64 ?? "");
            File.WriteAllBytes(filePath, bytes);
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInReadFileBase64(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("readFileBase64() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("readFileBase64() expects a string argument");
        try
        {
            var filePath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedded = EmbeddedFolderStore.ReadBytes(filePath);
                if (embedded == null) return RuntimeValue.Null();
                return RuntimeValue.String(Convert.ToBase64String(embedded));
            }
            if (!File.Exists(filePath)) return RuntimeValue.Null();
            var bytes = File.ReadAllBytes(filePath);
            return RuntimeValue.String(Convert.ToBase64String(bytes));
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }
    
    private static RuntimeValue BuiltInHasDirectory(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasDirectory() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasDirectory() expects a string argument");
        
        try
        {
            var dirPath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(dirPath))
                return RuntimeValue.Boolean(EmbeddedFolderStore.HasDirectory(dirPath));
            return RuntimeValue.Boolean(Directory.Exists(dirPath));
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInEnsureDir(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("ensureDir() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("ensureDir() expects a string argument");
        var dirPath = args[0].AsString();
        RejectEmbedWrite(dirPath, "ensureDir");
        if (string.IsNullOrEmpty(dirPath)) return RuntimeValue.Null();
        try
        {
            Directory.CreateDirectory(dirPath);
            return RuntimeValue.Null();
        }
        catch (Exception ex)
        {
            throw new Exception($"ensureDir() failed: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInListDirectory(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("listDirectory() expects 1 argument");
        string dirPath;
        if (args[0].Type == MaldaLang.Interpreter.ValueType.String)
            dirPath = args[0].AsString();
        else if (CapStdLib.TryGetToken(args[0], out var listCap) && listCap.Kind == CapabilityToken.KindDirList)
            dirPath = listCap.Path;
        else
            throw new Exception("listDirectory() expects a string argument");

        try
        {

            if (EmbeddedFolderStore.IsEmbedPath(dirPath))
            {
                var embedItems = new List<RuntimeValue>();
                foreach (var entry in EmbeddedFolderStore.List(dirPath))
                {
                    var itemObj = new JsonObject();
                    itemObj.Set("name", RuntimeValue.String(entry.Name));
                    itemObj.Set("type", RuntimeValue.String(entry.IsDirectory ? "directory" : "file"));
                    itemObj.Set("path", RuntimeValue.String(entry.Path));
                    embedItems.Add(RuntimeValue.Object(itemObj));
                }
                return RuntimeValue.Array(embedItems);
            }
            
            // Handle empty string - treat as current directory
            if (string.IsNullOrWhiteSpace(dirPath))
                dirPath = ".";
            
            // Resolve the directory path to an absolute path first
            // This handles relative paths like "." correctly
            var absoluteDirPath = Path.GetFullPath(dirPath);
            
            // Check if the resolved absolute path exists and is actually a directory
            if (!Directory.Exists(absoluteDirPath))
                return RuntimeValue.Array(new List<RuntimeValue>());
            
            var items = new List<RuntimeValue>();
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(absoluteDirPath))
                {
                    try
                    {
                        var name = Path.GetFileName(entry);
                        
                        // Skip if name is null (shouldn't happen, but be safe)
                        if (string.IsNullOrEmpty(name))
                            continue;
                        
                        // Ensure we always return an absolute path
                        // Directory.GetFileSystemEntries() should return full paths when given an absolute path,
                        // but we normalize it to ensure it's a proper absolute path
                        string fullPath;
                        if (Path.IsPathRooted(entry))
                        {
                            // Entry is already rooted (absolute), just normalize it
                            fullPath = Path.GetFullPath(entry);
                        }
                        else
                        {
                            // Entry is relative, combine with absolute directory path
                            fullPath = Path.GetFullPath(Path.Combine(absoluteDirPath, entry));
                        }
                        
                        // Check if it's a directory or file
                        var isDirectory = Directory.Exists(fullPath);
                        
                        var itemObj = new JsonObject();
                        itemObj.Set("name", RuntimeValue.String(name));
                        itemObj.Set("type", RuntimeValue.String(isDirectory ? "directory" : "file"));
                        itemObj.Set("path", RuntimeValue.String(fullPath));
                        items.Add(RuntimeValue.Object(itemObj));
                    }
                    catch
                    {
                        // Skip individual entries that cause errors (e.g., permission issues)
                        continue;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Directory exists but we don't have permission to read it
                throw new Exception($"listDirectory() failed: Access denied to directory '{absoluteDirPath}'");
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted between check and enumeration
                return RuntimeValue.Array(new List<RuntimeValue>());
            }
            
            return RuntimeValue.Array(items);
        }
        catch (Exception ex)
        {
            // Re-throw with more context for debugging
            throw new Exception($"listDirectory() failed: {SafeDirectoryWalk.ShortMessage(ex.Message)}", ex);
        }
    }

    private static RuntimeValue BuiltInReplaceInFile(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4) 
            throw new Exception("replaceInFile() expects 3 or 4 arguments");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.String || args[2].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("replaceInFile() expects (string, string, string, int?)");

        var filePath = args[0].AsString();
        RejectEmbedWrite(filePath, "replaceInFile");
        
        try
        {
            var oldText = args[1].AsString();
            var newText = args[2].AsString();
            var contextLines = args.Count == 4 && args[3].Type == MaldaLang.Interpreter.ValueType.Integer 
                ? args[3].AsInteger() 
                : 3;
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
            }
            
            var success = FileOperations.ReplaceInFile(filePath, oldText, newText, contextLines);
            
            // Trace file edit (best-effort)
            if (success)
            {
                try
                {
                    string? afterContent = null;
                    try
                    {
                        afterContent = File.ReadAllText(filePath);
                    }
                    catch
                    {
                    }
                    
                    TraceManager.Record(
                        TraceEventType.FileEdit,
                        new
                        {
                            path = filePath,
                            operation = "replace_range",
                            beforeContent,
                            afterContent,
                            toolName = "replaceInFile",
                            gitStatusBefore = (object?)null,
                            gitStatusAfter = (object?)null
                        });
                }
                catch
                {
                }
            }
            
            return RuntimeValue.Boolean(success);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInEditFile(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("editFile() expects 2 arguments");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.Array)
            throw new Exception("editFile() expects (string, array)");
        
        try
        {
            var filePath = args[0].AsString();
            var edits = args[1].AsArray();
            
            var editList = new List<FileOperations.FileEdit>();
            foreach (var editValue in edits)
            {
                if (editValue.Type != MaldaLang.Interpreter.ValueType.Object)
                    continue;
                
                var editObj = editValue.AsObject();
                RuntimeValue? oldTextVal = null;
                RuntimeValue? newTextVal = null;
                RuntimeValue? contextLinesVal = null;
                
                try { oldTextVal = editObj.Get("oldText", null); } catch { }
                try { newTextVal = editObj.Get("newText", null); } catch { }
                try { contextLinesVal = editObj.Get("contextLines", null); } catch { }
                
                var oldText = oldTextVal?.AsString() ?? "";
                var newText = newTextVal?.AsString() ?? "";
                var contextLines = contextLinesVal?.AsInteger() ?? 3;
                
                editList.Add(new FileOperations.FileEdit
                {
                    OldText = oldText,
                    NewText = newText,
                    ContextLines = contextLines
                });
            }
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
            }
            
            var result = FileOperations.EditFile(filePath, editList);
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(result.Success));
            resultObj.Set("applied", RuntimeValue.Integer(result.Applied));
            resultObj.Set("totalEdits", RuntimeValue.Integer(result.TotalEdits));
            if (result.FailedEditIndex > 0)
                resultObj.Set("failedEdit", RuntimeValue.Integer(result.FailedEditIndex));
            if (!string.IsNullOrEmpty(result.Error))
                resultObj.Set("error", RuntimeValue.String(result.Error));
            
            // Trace file edit (best-effort)
            if (result.Success)
            {
                try
                {
                    string? afterContent = null;
                    try
                    {
                        afterContent = File.ReadAllText(filePath);
                    }
                    catch
                    {
                    }
                    
                    TraceManager.Record(
                        TraceEventType.FileEdit,
                        new
                        {
                            path = filePath,
                            operation = "edit",
                            beforeContent,
                            afterContent,
                            toolName = "editFile",
                            gitStatusBefore = (object?)null,
                            gitStatusAfter = (object?)null
                        });
                }
                catch
                {
                }
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch
        {
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(false));
            resultObj.Set("applied", RuntimeValue.Integer(0));
            return RuntimeValue.Object(resultObj);
        }
    }
    
    private static RuntimeValue BuiltInCreateReadFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createReadFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateReadFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateWriteFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createWriteFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateWriteFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateReplaceInFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createReplaceInFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateReplaceInFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateListDirectoryTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createListDirectoryTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateListDirectoryTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateDeleteFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createDeleteFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateDeleteFileTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateCopyFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCopyFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateCopyFileTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateEnsureDirTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createEnsureDirTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateEnsureDirTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateAskUserTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createAskUserTool", args, 0, 0);
        // ask_user tool doesn't need a working directory parameter
        return BuiltInTools.CreateAskUserTool();
    }
    
    private static RuntimeValue BuiltInCreateInsertAtLineTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createInsertAtLineTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateInsertAtLineTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateEditFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createEditFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateEditFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInInsertAtLine(List<RuntimeValue> args)
    {
        if (args.Count < 3) throw new Exception("insertAtLine() expects at least 3 arguments: (filePath, lineNumber, content, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.Integer || args[2].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("insertAtLine() expects (string filePath, integer lineNumber, string content, ...)");
        
        var filePath = args[0].AsString();
        var lineNumber = args[1].AsInteger();
        var content = args[2].AsString();
        var insertAfter = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[3].AsBoolean() : false;
        
        try
        {
            if (!File.Exists(filePath))
            {
                return RuntimeValue.Boolean(false);
            }
            
            // Read file content to detect line endings
            var fileContent = File.ReadAllText(filePath);
            string lineEnding;
            
            // Detect line ending (CRLF or LF)
            if (fileContent.Contains("\r\n"))
            {
                lineEnding = "\r\n";
            }
            else if (fileContent.Contains("\n"))
            {
                lineEnding = "\n";
            }
            else
            {
                // No line breaks found, default to system line ending
                lineEnding = System.Environment.NewLine;
            }
            
            // Split into lines, preserving empty lines
            var lines = new List<string>();
            if (fileContent.Length > 0)
            {
                // Handle different line ending styles
                var normalizedContent = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");
                var splitLines = normalizedContent.Split('\n');
                
                // If file ends with newline, preserve it
                bool endsWithNewline = fileContent.EndsWith("\r\n") || fileContent.EndsWith("\n");
                
                foreach (var line in splitLines)
                {
                    lines.Add(line);
                }
                
                // Remove last empty line if file didn't end with newline
                if (!endsWithNewline && lines.Count > 0 && lines[lines.Count - 1] == "")
                {
                    lines.RemoveAt(lines.Count - 1);
                }
            }
            
            // Handle edge cases for line number
            int insertIndex;
            if (lineNumber <= 0)
            {
                // Line 0 or negative: insert at start
                insertIndex = 0;
            }
            else if (lineNumber > lines.Count)
            {
                // Line beyond file length: append at end
                insertIndex = lines.Count;
            }
            else
            {
                // Normal case: convert 1-indexed to 0-indexed
                insertIndex = lineNumber - 1;
                
                // Apply insertAfter logic
                if (insertAfter)
                {
                    insertIndex = lineNumber; // Insert after the line (before line N+1)
                }
            }
            
            // Split content by newlines to handle multi-line content
            var contentToInsert = new List<string>();
            if (!string.IsNullOrEmpty(content))
            {
                var normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n");
                var contentLines = normalizedContent.Split('\n');
                foreach (var line in contentLines)
                {
                    contentToInsert.Add(line);
                }
            }
            else
            {
                // Empty content - insert empty line
                contentToInsert.Add("");
            }
            
            // Insert all lines
            lines.InsertRange(insertIndex, contentToInsert);
            
            // Reconstruct file content with original line endings
            var newContent = string.Join(lineEnding, lines);
            
            // Write back to file
            File.WriteAllText(filePath, newContent);
            
            // Trace file edit (best-effort)
            try
            {
                string? afterContent = null;
                try
                {
                    afterContent = File.ReadAllText(filePath);
                }
                catch
                {
                }
                
                TraceManager.Record(
                    TraceEventType.FileEdit,
                    new
                    {
                        path = filePath,
                        operation = "insert",
                        beforeContent = fileContent,
                        afterContent,
                        toolName = "insertAtLine",
                        gitStatusBefore = (object?)null,
                        gitStatusAfter = (object?)null
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Boolean(true);
        }
        catch (Exception ex)
        {
            // Return false on any error
            return RuntimeValue.Boolean(false);
        }
    }
}
