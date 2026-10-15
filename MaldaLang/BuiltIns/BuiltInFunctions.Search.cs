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
    private static RuntimeValue BuiltInCreateGrepTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGrepTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateGrepTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateGlobTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGlobTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateGlobTool(workingDir);
    }

    private static RuntimeValue BuiltInGlob(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("glob() expects at least 1 argument: (pattern, dirPath?, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("glob() expects (string pattern, ...)");

        var pattern = args[0].AsString();
        var dirPath = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String
            ? args[1].AsString()
            : ".";
        var maxResults = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Integer
            ? args[2].AsInteger()
            : GlobHelper.DefaultMaxResults;
        var includeDirectories = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean
            && args[3].AsBoolean();
        var excludeDirs = args.Count > 4 && args[4].Type == MaldaLang.Interpreter.ValueType.String
            ? args[4].AsString()
            : "";
        var workingDirectory = args.Count > 5 && args[5].Type == MaldaLang.Interpreter.ValueType.String
            ? args[5].AsString()
            : "";

        try
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                dirPath = ".";

            string searchRoot;
            if (!string.IsNullOrEmpty(workingDirectory))
                searchRoot = Path.GetFullPath(Path.Combine(Path.GetFullPath(workingDirectory), dirPath));
            else
                searchRoot = Path.GetFullPath(dirPath);

            var matchResult = GlobHelper.Match(
                searchRoot,
                pattern,
                maxResults,
                includeDirectories,
                excludeDirs,
                string.IsNullOrEmpty(workingDirectory) ? null : Path.GetFullPath(workingDirectory));

            var items = new List<RuntimeValue>();
            foreach (var item in matchResult.Items)
            {
                var itemObj = new JsonObject();
                itemObj.Set("name", RuntimeValue.String(item.Name));
                itemObj.Set("type", RuntimeValue.String(item.Type));
                itemObj.Set("path", RuntimeValue.String(item.Path));
                items.Add(RuntimeValue.Object(itemObj));
            }

            var result = new JsonObject();
            result.Set("items", RuntimeValue.Array(items));
            result.Set("count", RuntimeValue.Integer(matchResult.Count));
            result.Set("truncated", RuntimeValue.Boolean(matchResult.Truncated));
            return RuntimeValue.Object(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error in glob: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
        }
    }
    
    private static RuntimeValue BuiltInGrep(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("grep() expects at least 2 arguments: (pattern, filePath, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("grep() expects (string pattern, string filePath, ...)");
        
        var pattern = args[0].AsString();
        var filePath = args[1].AsString();
        
        // Extract optional parameters
        var useRegex = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[2].AsBoolean() : false;
        var caseInsensitive = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[3].AsBoolean() : false;
        var includeLineNumbers = args.Count > 4 && args[4].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[4].AsBoolean() : true;
        var contextLines = args.Count > 5 && args[5].Type == MaldaLang.Interpreter.ValueType.Integer ? args[5].AsInteger() : 3;
        var countOnly = args.Count > 6 && args[6].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[6].AsBoolean() : false;
        var recursive = args.Count > 7 && args[7].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[7].AsBoolean() : true;
        var workingDirectory = args.Count > 8 && args[8].Type == MaldaLang.Interpreter.ValueType.String ? args[8].AsString() : "";
        
        try
        {
            var matches = new List<RuntimeValue>();
            var filesSearched = 0;
            var totalMatches = 0;

            // Prepare regex or string matching
            System.Text.RegularExpressions.Regex? regex = null;
            string? searchPattern = null;
            
            if (useRegex)
            {
                try
                {
                    var options = RegexOptions.None;
                    if (caseInsensitive)
                    {
                        options |= RegexOptions.IgnoreCase;
                    }
                    regex = new Regex(pattern, options);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error: Invalid regex pattern: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
                }
            }
            else
            {
                searchPattern = caseInsensitive ? pattern.ToLowerInvariant() : pattern;
            }

            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedFiles = EmbeddedFolderStore.EnumerateFiles(filePath, recursive);
                if (embedFiles.Count == 0 &&
                    !EmbeddedFolderStore.HasFile(filePath) &&
                    !EmbeddedFolderStore.HasDirectory(filePath))
                {
                    return RuntimeValue.String($"Error: Path '{filePath}' does not exist");
                }

                foreach (var file in embedFiles)
                {
                    filesSearched++;
                    var text = EmbeddedFolderStore.ReadText(file);
                    if (text == null)
                    {
                        continue;
                    }

                    var pathForResult = file;
                    if (!string.IsNullOrEmpty(workingDirectory) &&
                        EmbeddedFolderStore.IsEmbedPath(workingDirectory) &&
                        file.StartsWith(workingDirectory.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        pathForResult = file.Substring(workingDirectory.TrimEnd('/').Length + 1);
                    }

                    var fileMatches = SearchLines(
                        text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None),
                        pathForResult,
                        pattern,
                        regex,
                        searchPattern,
                        useRegex,
                        caseInsensitive,
                        includeLineNumbers,
                        contextLines);

                    if (countOnly)
                    {
                        totalMatches += fileMatches.Count;
                    }
                    else
                    {
                        matches.AddRange(fileMatches);
                    }
                }

                if (countOnly)
                {
                    var embedResult = new JsonObject();
                    embedResult.Set("count", RuntimeValue.Integer(totalMatches));
                    embedResult.Set("filesSearched", RuntimeValue.Integer(filesSearched));
                    return RuntimeValue.Object(embedResult);
                }

                return RuntimeValue.Array(matches);
            }
            
            // Determine if filePath is a file or directory
            var isDirectory = Directory.Exists(filePath);
            var isFile = File.Exists(filePath);
            
            if (!isFile && !isDirectory)
            {
                return RuntimeValue.String($"Error: Path '{filePath}' does not exist");
            }
            
            // Get list of files to search
            var filesToSearch = new List<string>();
            
            if (isFile)
            {
                filesToSearch.Add(filePath);
            }
            else if (isDirectory)
            {
                if (recursive)
                {
                    // Recursively get all files
                    try
                    {
                        filesToSearch.AddRange(SafeDirectoryWalk.EnumerateFiles(filePath));
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error accessing directory: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
                    }
                }
                else
                {
                    // Only files in the directory itself
                    try
                    {
                        filesToSearch.AddRange(Directory.GetFiles(filePath, "*", SearchOption.TopDirectoryOnly));
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error accessing directory: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
                    }
                }
            }
            
            // Search each file
            foreach (var file in filesToSearch)
            {
                try
                {
                    filesSearched++;
                    var fileMatches = SearchFile(file, pattern, regex, searchPattern, useRegex, caseInsensitive, includeLineNumbers, contextLines, workingDirectory);
                    
                    if (countOnly)
                    {
                        totalMatches += fileMatches.Count;
                    }
                    else
                    {
                        matches.AddRange(fileMatches);
                    }
                }
                catch (Exception ex)
                when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    // Skip files we can't read (permissions, locked, etc.)
                    continue;
                }
            }
            
            if (countOnly)
            {
                var result = new JsonObject();
                result.Set("count", RuntimeValue.Integer(totalMatches));
                result.Set("filesSearched", RuntimeValue.Integer(filesSearched));
                return RuntimeValue.Object(result);
            }
            else
            {
                return RuntimeValue.Array(matches);
            }
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error in grep: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
        }
    }
    
    private static List<RuntimeValue> SearchFile(
        string filePath,
        string originalPattern,
        Regex? regex,
        string? searchPattern,
        bool useRegex,
        bool caseInsensitive,
        bool includeLineNumbers,
        int contextLines,
        string workingDirectory = "")
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            var absolutePath = Path.GetFullPath(filePath);
            var pathForResult = absolutePath;
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                try
                {
                    var baseDir = Path.GetFullPath(workingDirectory);
                    pathForResult = Path.GetRelativePath(baseDir, absolutePath);
                }
                catch
                {
                    /* keep absolute path if GetRelativePath fails */
                }
            }

            return SearchLines(
                lines,
                pathForResult,
                originalPattern,
                regex,
                searchPattern,
                useRegex,
                caseInsensitive,
                includeLineNumbers,
                contextLines);
        }
        catch (Exception)
        {
            return new List<RuntimeValue>();
        }
    }

    private static List<RuntimeValue> SearchLines(
        string[] lines,
        string pathForResult,
        string originalPattern,
        Regex? regex,
        string? searchPattern,
        bool useRegex,
        bool caseInsensitive,
        bool includeLineNumbers,
        int contextLines)
    {
        var matches = new List<RuntimeValue>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1; // 1-indexed
            bool isMatch = false;

            if (useRegex && regex != null)
            {
                isMatch = regex.IsMatch(line);
            }
            else if (searchPattern != null)
            {
                var lineToSearch = caseInsensitive ? line.ToLowerInvariant() : line;
                isMatch = lineToSearch.Contains(searchPattern);
            }

            if (!isMatch)
            {
                continue;
            }

            var matchObj = new JsonObject();
            matchObj.Set("filePath", RuntimeValue.String(pathForResult));

            if (includeLineNumbers)
            {
                matchObj.Set("lineNumber", RuntimeValue.Integer(lineNumber));
            }

            matchObj.Set("content", RuntimeValue.String(line));

            if (contextLines > 0)
            {
                var contextBefore = new List<RuntimeValue>();
                var contextAfter = new List<RuntimeValue>();

                for (int j = Math.Max(0, i - contextLines); j < i; j++)
                {
                    contextBefore.Add(RuntimeValue.String(lines[j]));
                }

                for (int j = i + 1; j < Math.Min(lines.Length, i + 1 + contextLines); j++)
                {
                    contextAfter.Add(RuntimeValue.String(lines[j]));
                }

                if (contextBefore.Count > 0)
                {
                    matchObj.Set("contextBefore", RuntimeValue.Array(contextBefore));
                }

                if (contextAfter.Count > 0)
                {
                    matchObj.Set("contextAfter", RuntimeValue.Array(contextAfter));
                }
            }

            matches.Add(RuntimeValue.Object(matchObj));
        }

        return matches;
    }
}
