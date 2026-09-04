// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static class BuiltInTools
{
    private const string GitRepoPathParamDescription =
        "Leave unset. Defaults to the agent working directory (same folder as read_file/write_file). Do not pass the process launch directory or a different checkout/worktree.";

    public static RuntimeValue CreateReadFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to read. Use a relative path (e.g. \"current.malda\", \"MALDA_SPEC.md\") when the tool has a working directory."));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var startLineProp = new JsonObject();
        startLineProp.Set("type", RuntimeValue.String("integer"));
        startLineProp.Set("description", RuntimeValue.String("Optional: Starting line number (1-indexed). If not provided, reads entire file."));
        properties.Set("startLine", RuntimeValue.Object(startLineProp));
        
        var endLineProp = new JsonObject();
        endLineProp.Set("type", RuntimeValue.String("integer"));
        endLineProp.Set("description", RuntimeValue.String("Optional: Ending line number (1-indexed, inclusive). If not provided and startLine is provided, reads to end of file."));
        properties.Set("endLine", RuntimeValue.Object(endLineProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("filePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "read_file",
            "Reads the contents of a file as a string. If startLine is provided, reads only the specified line range (1-indexed). If both startLine and endLine are provided, reads lines from startLine to endLine (inclusive).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateWriteFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to write"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var contentProp = new JsonObject();
        contentProp.Set("type", RuntimeValue.String("string"));
        contentProp.Set("description", RuntimeValue.String("Content to write to the file. Maximum length: 6,000 characters. Use ONLY for new small files (under ~200 lines). For existing or large files use edit_file or replace_in_file instead — large write_file JSON is truncated by the LLM and will fail."));
        contentProp.Set("maxLength", RuntimeValue.Integer(6000));
        properties.Set("content", RuntimeValue.Object(contentProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("content") 
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "write_file",
            "Writes content to a file (create or overwrite). For NEW small files only (under ~200 lines). For existing/large files use edit_file or replace_in_file — full write_file payloads are truncated by the LLM and rejected.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateReplaceInFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var oldTextProp = new JsonObject();
        oldTextProp.Set("type", RuntimeValue.String("string"));
        oldTextProp.Set("description", RuntimeValue.String("The exact text to find and replace (do NOT include context lines - just the substring to replace). Matching is robust to whitespace differences. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the replacement into multiple smaller chunks to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or newlines. Do not use excessive consecutive newlines (max 10)."));
        oldTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("oldText", RuntimeValue.Object(oldTextProp));
        
        var newTextProp = new JsonObject();
        newTextProp.Set("type", RuntimeValue.String("string"));
        newTextProp.Set("description", RuntimeValue.String("The replacement text. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the replacement into multiple smaller chunks to avoid API truncation. For large replacements, make multiple replace_in_file calls with smaller chunks."));
        newTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("newText", RuntimeValue.Object(newTextProp));
        
        var contextLinesProp = new JsonObject();
        contextLinesProp.Set("type", RuntimeValue.String("integer"));
        contextLinesProp.Set("description", RuntimeValue.String("Number of context lines before and after the oldText match to use for disambiguation (default: 3)"));
        contextLinesProp.Set("default", RuntimeValue.Integer(3));
        properties.Set("contextLines", RuntimeValue.Object(contextLinesProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("oldText"),
            RuntimeValue.String("newText")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "replace_in_file",
            "Replaces a SINGLE substring in a file. Tries an exact match first; if oldText appears more than once, the call fails unless fuzzy matching finds exactly one unambiguous occurrence (contextLines can disambiguate when only one match has unique surrounding lines). Whitespace-only differences (spaces, tabs, indentation) may match in fuzzy mode. For multiple replacements, use edit_file instead. IMPORTANT: For large replacements (oldText or newText > 2000 characters), split into multiple smaller replace_in_file calls to prevent API response truncation. Process chunks sequentially, working from top to bottom of the file. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or excessive newlines (max 10 consecutive newlines). If a replace fails, use a longer, unique oldText snippet.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateListDirectoryTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var dirPathProp = new JsonObject();
        dirPathProp.Set("type", RuntimeValue.String("string"));
        dirPathProp.Set("description", RuntimeValue.String("Path to the directory (use '.' for current directory)"));
        properties.Set("dirPath", RuntimeValue.Object(dirPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("dirPath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "list_directory",
            "Lists files and directories. Use this instead of powershell Get-ChildItem, cmd dir, or findstr. Returns an array of objects with name, type (file/directory), and path.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateDeleteFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to delete. Use a relative path when the tool has a working directory."));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("filePath") }));

        tool.Initialize(
            "delete_file",
            "Deletes a file. Paths stay inside the working directory. Returns { success: boolean, error?: string }. Succeeds when the file is already missing.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateCopyFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var srcPathProp = new JsonObject();
        srcPathProp.Set("type", RuntimeValue.String("string"));
        srcPathProp.Set("description", RuntimeValue.String("Path of the file to copy. Use a relative path when the tool has a working directory."));
        properties.Set("srcPath", RuntimeValue.Object(srcPathProp));

        var destPathProp = new JsonObject();
        destPathProp.Set("type", RuntimeValue.String("string"));
        destPathProp.Set("description", RuntimeValue.String("Destination path. Use a relative path when the tool has a working directory. Both srcPath and destPath must stay inside the working directory."));
        properties.Set("destPath", RuntimeValue.Object(destPathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("srcPath"),
            RuntimeValue.String("destPath")
        }));

        tool.Initialize(
            "copy_file",
            "Copies a file. Both srcPath and destPath must stay inside the working directory. Returns { success: boolean, error?: string }.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateEnsureDirTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();
        var dirPathProp = new JsonObject();
        dirPathProp.Set("type", RuntimeValue.String("string"));
        dirPathProp.Set("description", RuntimeValue.String("Directory to create (parents are created as needed). Use a relative path when the tool has a working directory."));
        properties.Set("dirPath", RuntimeValue.Object(dirPathProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("dirPath") }));

        tool.Initialize(
            "ensure_dir",
            "Creates a directory and any missing parents. Paths stay inside the working directory. Returns { success: boolean, path?: string, error?: string }.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateAskUserTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var questionProp = new JsonObject();
        questionProp.Set("type", RuntimeValue.String("string"));
        questionProp.Set("description", RuntimeValue.String("The question to ask the user"));
        properties.Set("question", RuntimeValue.Object(questionProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("question") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "ask_user",
            "Asks a question to the user and returns their response. Use this when you need clarification, confirmation, or additional information from the user.",
            RuntimeValue.Object(parameters),
            null,
            "" // No working directory needed for user input
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateWebSearchTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var queryProp = new JsonObject();
        queryProp.Set("type", RuntimeValue.String("string"));
        queryProp.Set("description", RuntimeValue.String("Search query for the web (e.g. 'MALDA programming language', 'current weather London')"));
        properties.Set("query", RuntimeValue.Object(queryProp));
        
        var apiKeyProp = new JsonObject();
        apiKeyProp.Set("type", RuntimeValue.String("string"));
        apiKeyProp.Set("description", RuntimeValue.String("Optional: Brave Search API key. If not provided, uses BRAVE_SEARCH_API_KEY environment variable."));
        properties.Set("apiKey", RuntimeValue.Object(apiKeyProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("query") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "web_search",
            "Searches the web using Brave Search and returns a list of results. Use this to find current information, documentation, or facts. Returns an object with 'ok' (boolean), 'results' (array of { title, url, description }), and 'moreResultsAvailable' (boolean). On failure returns { ok: false, error: string }.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateWebFetchTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var urlProp = new JsonObject();
        urlProp.Set("type", RuntimeValue.String("string"));
        urlProp.Set("description", RuntimeValue.String("HTTP or HTTPS URL to fetch. Other schemes (file://, ftp://, …) are rejected."));
        properties.Set("url", RuntimeValue.Object(urlProp));

        var maxBytesProp = new JsonObject();
        maxBytesProp.Set("type", RuntimeValue.String("integer"));
        maxBytesProp.Set("description", RuntimeValue.String("Maximum content length in characters (default 100000, hard cap 500000). Longer bodies are truncated and truncated is set true."));
        maxBytesProp.Set("default", RuntimeValue.Integer(100000));
        properties.Set("maxBytes", RuntimeValue.Object(maxBytesProp));

        var timeoutMsProp = new JsonObject();
        timeoutMsProp.Set("type", RuntimeValue.String("integer"));
        timeoutMsProp.Set("description", RuntimeValue.String("Request timeout in milliseconds (default 15000, hard cap 60000)."));
        timeoutMsProp.Set("default", RuntimeValue.Integer(15000));
        properties.Set("timeoutMs", RuntimeValue.Object(timeoutMsProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("url") }));

        tool.Initialize(
            "web_fetch",
            "Fetches an HTTP or HTTPS URL and returns the response body as text. Use for reading documentation or pages (not search). Parameters: url (required), maxBytes? (default 100000, cap 500000), timeoutMs? (default 15000, cap 60000). Returns { ok, status, url, content, truncated, error? }. JSON bodies are returned as a JSON string. Parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );

        return RuntimeValue.Object(tool);
    }

    /// <summary>
    /// Shared host implementation for <c>web_fetch</c> / <c>webFetch</c>.
    /// Conversation and <c>tool.execute</c> both call this.
    /// </summary>
    public static RuntimeValue ExecuteWebFetch(RuntimeValue arguments)
        => BuiltInFunctions.ExecuteWebFetch(arguments);
    
    public static RuntimeValue CreateGrepTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var patternProp = new JsonObject();
        patternProp.Set("type", RuntimeValue.String("string"));
        patternProp.Set("description", RuntimeValue.String("Search pattern. Use useRegex: true for alternation (e.g. \"FUNCTIONS|function\") or other regex; otherwise plain text."));
        properties.Set("pattern", RuntimeValue.Object(patternProp));
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Required. Relative path to the file or directory (e.g. \"MALDA_SPEC.md\", \"current.malda\"). When the tool has a working directory, use only relative paths."));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var useRegexProp = new JsonObject();
        useRegexProp.Set("type", RuntimeValue.String("boolean"));
        useRegexProp.Set("description", RuntimeValue.String("Whether pattern is a regex pattern or plain text (default: false)"));
        useRegexProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("useRegex", RuntimeValue.Object(useRegexProp));
        
        var caseInsensitiveProp = new JsonObject();
        caseInsensitiveProp.Set("type", RuntimeValue.String("boolean"));
        caseInsensitiveProp.Set("description", RuntimeValue.String("Case-insensitive matching (default: false)"));
        caseInsensitiveProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("caseInsensitive", RuntimeValue.Object(caseInsensitiveProp));
        
        var includeLineNumbersProp = new JsonObject();
        includeLineNumbersProp.Set("type", RuntimeValue.String("boolean"));
        includeLineNumbersProp.Set("description", RuntimeValue.String("Include line numbers in results (default: true)"));
        includeLineNumbersProp.Set("default", RuntimeValue.Boolean(true));
        properties.Set("includeLineNumbers", RuntimeValue.Object(includeLineNumbersProp));
        
        var contextLinesProp = new JsonObject();
        contextLinesProp.Set("type", RuntimeValue.String("integer"));
        contextLinesProp.Set("description", RuntimeValue.String("Number of context lines before and after matches (default: 3)"));
        contextLinesProp.Set("default", RuntimeValue.Integer(3));
        properties.Set("contextLines", RuntimeValue.Object(contextLinesProp));
        
        var countOnlyProp = new JsonObject();
        countOnlyProp.Set("type", RuntimeValue.String("boolean"));
        countOnlyProp.Set("description", RuntimeValue.String("Return only match count instead of full results (default: false)"));
        countOnlyProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("countOnly", RuntimeValue.Object(countOnlyProp));
        
        var recursiveProp = new JsonObject();
        recursiveProp.Set("type", RuntimeValue.String("boolean"));
        recursiveProp.Set("description", RuntimeValue.String("Search recursively in directories (default: true)"));
        recursiveProp.Set("default", RuntimeValue.Boolean(true));
        properties.Set("recursive", RuntimeValue.Object(recursiveProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("pattern"),
            RuntimeValue.String("filePath")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "grep",
            "Searches for a pattern in files. Supports both regex and plain text search with options for case-insensitive matching, line numbers, context lines, count-only mode, and recursive directory searching.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateGlobTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var patternProp = new JsonObject();
        patternProp.Set("type", RuntimeValue.String("string"));
        patternProp.Set("description", RuntimeValue.String("Glob pattern (e.g. \"**/*.cs\", \"*.malda\", \"src/**/app.malda\"). Supports *, **, ?, and brace expansion."));
        properties.Set("pattern", RuntimeValue.Object(patternProp));

        var dirPathProp = new JsonObject();
        dirPathProp.Set("type", RuntimeValue.String("string"));
        dirPathProp.Set("description", RuntimeValue.String("Directory to search from, relative to the working directory (default: \".\")"));
        dirPathProp.Set("default", RuntimeValue.String("."));
        properties.Set("dirPath", RuntimeValue.Object(dirPathProp));

        var maxResultsProp = new JsonObject();
        maxResultsProp.Set("type", RuntimeValue.String("integer"));
        maxResultsProp.Set("description", RuntimeValue.String("Maximum number of matches to return (default: 200, hard cap: 500)"));
        maxResultsProp.Set("default", RuntimeValue.Integer(GlobHelper.DefaultMaxResults));
        properties.Set("maxResults", RuntimeValue.Object(maxResultsProp));

        var includeDirectoriesProp = new JsonObject();
        includeDirectoriesProp.Set("type", RuntimeValue.String("boolean"));
        includeDirectoriesProp.Set("description", RuntimeValue.String("Include matching directories in results (default: false)"));
        includeDirectoriesProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("includeDirectories", RuntimeValue.Object(includeDirectoriesProp));

        var excludeDirsProp = new JsonObject();
        excludeDirsProp.Set("type", RuntimeValue.String("string"));
        excludeDirsProp.Set("description", RuntimeValue.String("Comma-separated extra directory names to exclude (in addition to .git, node_modules, bin, obj)"));
        excludeDirsProp.Set("default", RuntimeValue.String(""));
        properties.Set("excludeDirs", RuntimeValue.Object(excludeDirsProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));

        var required = new List<RuntimeValue> { RuntimeValue.String("pattern") };
        parameters.Set("required", RuntimeValue.Array(required));

        tool.Initialize(
            "glob",
            "Find files by glob pattern under a directory. Use for discovery (e.g. \"**/*.cs\"). " +
            "Prefer this over run_command/find/dir. Returns { items, count, truncated }; narrow the pattern if truncated is true. " +
            "Paths are relative to the working directory. Default excludes: .git, node_modules, bin, obj.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }

    /// <summary>
    /// Runs the built-in <c>glob</c> tool (same contract as Conversation / <c>tool.execute</c>).
    /// </summary>
    public static RuntimeValue ExecuteGlob(ToolInstance tool, RuntimeValue arguments)
    {
        try
        {
            if (arguments.Type != MaldaLang.Interpreter.ValueType.Object)
                return RuntimeValue.String("Error: Tool arguments must be an object");

            var argsObj = arguments.AsObject();
            var globPatternVal = argsObj.Get("pattern", null);
            if (globPatternVal == null || globPatternVal.Type != MaldaLang.Interpreter.ValueType.String)
                return RuntimeValue.String("Error: pattern parameter required");

            var globPattern = globPatternVal.AsString();
            var globDirPath = ".";

            var globDirPathVal = argsObj.Get("dirPath", null);
            if (globDirPathVal != null && globDirPathVal.Type == MaldaLang.Interpreter.ValueType.String)
                globDirPath = globDirPathVal.AsString();

            if (!string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                var normalizedGlobDir = tool.NormalizePathForWorkingDirectory(globDirPath);
                if (normalizedGlobDir == null)
                {
                    return RuntimeValue.String($"Error: Path '{globDirPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \".\", \"src\").");
                }
                globDirPath = normalizedGlobDir;
            }
            else if (!tool.IsPathAllowed(globDirPath))
            {
                return RuntimeValue.String($"Error: Path '{globDirPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
            }

            var globMaxResults = GlobHelper.DefaultMaxResults;
            try
            {
                var maxResultsVal = argsObj.Get("maxResults", null);
                if (maxResultsVal != null && maxResultsVal.Type == MaldaLang.Interpreter.ValueType.Integer)
                    globMaxResults = maxResultsVal.AsInteger();
            }
            catch { }

            var globIncludeDirectories = false;
            try
            {
                var includeDirsVal = argsObj.Get("includeDirectories", null);
                if (includeDirsVal != null && includeDirsVal.Type == MaldaLang.Interpreter.ValueType.Boolean)
                    globIncludeDirectories = includeDirsVal.AsBoolean();
            }
            catch { }

            var globExcludeDirs = "";
            try
            {
                var excludeDirsVal = argsObj.Get("excludeDirs", null);
                if (excludeDirsVal != null && excludeDirsVal.Type == MaldaLang.Interpreter.ValueType.String)
                    globExcludeDirs = excludeDirsVal.AsString();
            }
            catch { }

            var globArgs = new List<RuntimeValue>
            {
                RuntimeValue.String(globPattern),
                RuntimeValue.String(globDirPath),
                RuntimeValue.Integer(globMaxResults),
                RuntimeValue.Boolean(globIncludeDirectories),
                RuntimeValue.String(globExcludeDirs),
                RuntimeValue.String(tool.WorkingDirectory ?? "")
            };

            return BuiltInFunctions.CallBuiltIn("glob", globArgs, null);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing glob tool: {ex.Message}");
        }
    }
    
    public static RuntimeValue CreateInsertAtLineTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var lineNumberProp = new JsonObject();
        lineNumberProp.Set("type", RuntimeValue.String("integer"));
        lineNumberProp.Set("description", RuntimeValue.String("Line number where to insert (1-indexed). Line 0 inserts at start, line > file length appends at end"));
        properties.Set("lineNumber", RuntimeValue.Object(lineNumberProp));
        
        var contentProp = new JsonObject();
        contentProp.Set("type", RuntimeValue.String("string"));
        contentProp.Set("description", RuntimeValue.String("Content to insert (can be multi-line). Maximum length: 50,000 characters."));
        contentProp.Set("maxLength", RuntimeValue.Integer(50000));
        properties.Set("content", RuntimeValue.Object(contentProp));
        
        var insertAfterProp = new JsonObject();
        insertAfterProp.Set("type", RuntimeValue.String("boolean"));
        insertAfterProp.Set("description", RuntimeValue.String("If true, inserts after the specified line; if false, inserts before (default: false)"));
        insertAfterProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("insertAfter", RuntimeValue.Object(insertAfterProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"),
            RuntimeValue.String("lineNumber"),
            RuntimeValue.String("content")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "insertAtLine",
            "Inserts content at a specific line number in a file. Supports inserting before or after a line, with lenient edge case handling (line 0 = start, line > file length = append).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateEditFileTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Path to the file to edit"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        // Define the edit object schema
        var editItemProperties = new JsonObject();
        
        var oldTextProp = new JsonObject();
        oldTextProp.Set("type", RuntimeValue.String("string"));
        oldTextProp.Set("description", RuntimeValue.String("The exact text to find and replace (do NOT include context lines - just the substring to replace). Matching is robust to whitespace differences. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the edits into multiple smaller edit_file calls to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or newlines. Do not use excessive consecutive newlines (max 10)."));
        oldTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        editItemProperties.Set("oldText", RuntimeValue.Object(oldTextProp));
        
        var newTextProp = new JsonObject();
        newTextProp.Set("type", RuntimeValue.String("string"));
        newTextProp.Set("description", RuntimeValue.String("The replacement text. Maximum length: 50,000 characters. IMPORTANT: If oldText or newText exceeds ~2000 characters, split the edits into multiple smaller edit_file calls to avoid API truncation."));
        newTextProp.Set("maxLength", RuntimeValue.Integer(50000));
        editItemProperties.Set("newText", RuntimeValue.Object(newTextProp));
        
        var contextLinesProp = new JsonObject();
        contextLinesProp.Set("type", RuntimeValue.String("integer"));
        contextLinesProp.Set("description", RuntimeValue.String("Number of context lines before and after the oldText match to use for disambiguation (default: 3)"));
        contextLinesProp.Set("default", RuntimeValue.Integer(3));
        editItemProperties.Set("contextLines", RuntimeValue.Object(contextLinesProp));
        
        var editItemRequired = new List<RuntimeValue> 
        { 
            RuntimeValue.String("oldText"),
            RuntimeValue.String("newText")
        };
        
        var editItemSchema = new JsonObject();
        editItemSchema.Set("type", RuntimeValue.String("object"));
        editItemSchema.Set("properties", RuntimeValue.Object(editItemProperties));
        editItemSchema.Set("required", RuntimeValue.Array(editItemRequired));
        
        var editsProp = new JsonObject();
        editsProp.Set("type", RuntimeValue.String("array"));
        editsProp.Set("description", RuntimeValue.String("Array of edits to apply. Each edit object should have 'oldText' (string, required), 'newText' (string, required), and optionally 'contextLines' (integer, default: 3). Edits are applied sequentially in order."));
        editsProp.Set("items", RuntimeValue.Object(editItemSchema));
        properties.Set("edits", RuntimeValue.Object(editsProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("filePath"), 
            RuntimeValue.String("edits")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "edit_file",
            "Applies MULTIPLE text replacements to a file in a single operation. Use this tool when you need to make TWO OR MORE edits to the same file. For a single replacement, use replace_in_file instead (simpler API). Each edit uses the same strict matching rules as replace_in_file (exact match first; fails on ambiguous multiple matches). Returns {success: boolean, applied: integer, totalEdits: integer, error?: string, failedEdit?: integer}; success is true only when every edit was applied — on failure no changes are written (transactional). Edits are applied sequentially in memory, then written once. IMPORTANT: If any oldText or newText exceeds ~2000 characters, consider splitting into multiple edit_file calls to avoid API truncation. CRITICAL: oldText must contain actual code/text content, NOT just whitespace or excessive newlines (max 10 consecutive newlines). Always check success in the tool result before assuming edits were applied.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitStatusTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_status",
            "Gets git status: modified, staged, and untracked files. Use this after creating new files — untracked files appear here, not in git_diff.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitAddTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filesProp = new JsonObject();
        filesProp.Set("type", RuntimeValue.String("string"));
        filesProp.Set("description", RuntimeValue.String("Files to stage: use '.' for all changes in the working directory, or basename paths relative to the agent workdir (e.g. snake.html PRD.md). Do not use repo-root paths."));
        properties.Set("files", RuntimeValue.Object(filesProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("files") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_add",
            "Adds files to the git staging area. Use '.' to add all files, or specify specific file paths.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitCommitTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var messageProp = new JsonObject();
        messageProp.Set("type", RuntimeValue.String("string"));
        messageProp.Set("description", RuntimeValue.String("Commit message describing the changes"));
        properties.Set("message", RuntimeValue.Object(messageProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("message") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_commit",
            "Creates a commit with the staged changes. Requires a commit message.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitLogTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var countProp = new JsonObject();
        countProp.Set("type", RuntimeValue.String("integer"));
        countProp.Set("description", RuntimeValue.String("Optional: Number of commits to show (default: 10)"));
        countProp.Set("default", RuntimeValue.Integer(10));
        properties.Set("count", RuntimeValue.Object(countProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_log",
            "Shows the commit history. Returns commit messages, authors, dates, and commit hashes.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitDiffTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var filePathProp = new JsonObject();
        filePathProp.Set("type", RuntimeValue.String("string"));
        filePathProp.Set("description", RuntimeValue.String("Optional: Specific file path to show diff for (if not provided, shows all changes)"));
        properties.Set("filePath", RuntimeValue.Object(filePathProp));
        
        var stagedProp = new JsonObject();
        stagedProp.Set("type", RuntimeValue.String("boolean"));
        stagedProp.Set("description", RuntimeValue.String("Optional: Show staged changes instead of unstaged (default: false)"));
        stagedProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("staged", RuntimeValue.Object(stagedProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_diff",
            "Shows diffs for tracked files only (working tree vs index, or index vs last commit). New untracked files are not included — use git_status instead. Empty output does not mean no new files exist.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitBranchTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var actionProp = new JsonObject();
        actionProp.Set("type", RuntimeValue.String("string"));
        actionProp.Set("description", RuntimeValue.String("Action to perform: 'list' to show all branches, 'create' to create a new branch"));
        actionProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue> 
        { 
            RuntimeValue.String("list"), 
            RuntimeValue.String("create")
        }));
        properties.Set("action", RuntimeValue.Object(actionProp));
        
        var branchNameProp = new JsonObject();
        branchNameProp.Set("type", RuntimeValue.String("string"));
        branchNameProp.Set("description", RuntimeValue.String("Optional: Branch name (required for 'create' action)"));
        properties.Set("branchName", RuntimeValue.Object(branchNameProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("action") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_branch",
            "Manages git branches. Can list all branches or create a new branch. Branch deletion is disabled for safety.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitCheckoutTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var branchNameProp = new JsonObject();
        branchNameProp.Set("type", RuntimeValue.String("string"));
        branchNameProp.Set("description", RuntimeValue.String("Branch name to checkout"));
        properties.Set("branchName", RuntimeValue.Object(branchNameProp));
        
        var createProp = new JsonObject();
        createProp.Set("type", RuntimeValue.String("boolean"));
        createProp.Set("description", RuntimeValue.String("Optional: Create the branch if it doesn't exist (default: false)"));
        createProp.Set("default", RuntimeValue.Boolean(false));
        properties.Set("create", RuntimeValue.Object(createProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("branchName") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_checkout",
            "Switches to a different branch. Can optionally create the branch if it doesn't exist.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitPushTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var remoteProp = new JsonObject();
        remoteProp.Set("type", RuntimeValue.String("string"));
        remoteProp.Set("description", RuntimeValue.String("Optional: Remote name (default: 'origin')"));
        remoteProp.Set("default", RuntimeValue.String("origin"));
        properties.Set("remote", RuntimeValue.Object(remoteProp));
        
        var branchProp = new JsonObject();
        branchProp.Set("type", RuntimeValue.String("string"));
        branchProp.Set("description", RuntimeValue.String("Optional: Branch name to push (defaults to current branch)"));
        properties.Set("branch", RuntimeValue.Object(branchProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_push",
            "Pushes commits to a remote repository. Pushes the current branch to origin by default.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGitPullTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        var remoteProp = new JsonObject();
        remoteProp.Set("type", RuntimeValue.String("string"));
        remoteProp.Set("description", RuntimeValue.String("Optional: Remote name (default: 'origin')"));
        remoteProp.Set("default", RuntimeValue.String("origin"));
        properties.Set("remote", RuntimeValue.Object(remoteProp));
        
        var branchProp = new JsonObject();
        branchProp.Set("type", RuntimeValue.String("string"));
        branchProp.Set("description", RuntimeValue.String("Optional: Branch name to pull (defaults to current branch)"));
        properties.Set("branch", RuntimeValue.Object(branchProp));
        
        var repoPathProp = new JsonObject();
        repoPathProp.Set("type", RuntimeValue.String("string"));
        repoPathProp.Set("description", RuntimeValue.String(GitRepoPathParamDescription));
        properties.Set("repoPath", RuntimeValue.Object(repoPathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "git_pull",
            "Pulls changes from a remote repository. Pulls from origin for the current branch by default.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateRunCommandTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var commandProp = new JsonObject();
        commandProp.Set("type", RuntimeValue.String("string"));
        commandProp.Set("description", RuntimeValue.String("Command to execute (e.g., 'dotnet', 'npm', 'python', 'echo'). The command name without arguments."));
        properties.Set("command", RuntimeValue.Object(commandProp));
        
        var argsProp = new JsonObject();
        argsProp.Set("type", RuntimeValue.String("array"));
        argsProp.Set("description", RuntimeValue.String("Optional: Array of command arguments as strings (e.g., ['build', '--configuration', 'Release'])"));
        var argsItems = new JsonObject();
        argsItems.Set("type", RuntimeValue.String("string"));
        argsProp.Set("items", RuntimeValue.Object(argsItems));
        properties.Set("args", RuntimeValue.Object(argsProp));
        
        var workingDirectoryProp = new JsonObject();
        workingDirectoryProp.Set("type", RuntimeValue.String("string"));
        workingDirectoryProp.Set("description", RuntimeValue.String("Optional: Working directory for command execution. If not provided, uses the tool's working directory or current directory."));
        properties.Set("workingDirectory", RuntimeValue.Object(workingDirectoryProp));
        
        var timeoutMsProp = new JsonObject();
        timeoutMsProp.Set("type", RuntimeValue.String("integer"));
        timeoutMsProp.Set("description", RuntimeValue.String("Optional: Timeout in milliseconds. If the command doesn't complete within this time, it will be killed and return an error. If not provided, waits indefinitely."));
        properties.Set("timeoutMs", RuntimeValue.Object(timeoutMsProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("command") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "run_command",
            "Executes a command-line program and returns exitCode, stdout, and stderr. Use direct executables (dotnet, npm, python) — not shell wrappers as the command name. Shell commands (powershell, cmd, bash) may require user confirmation before running.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateRunMALDATool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var sourceOrFilePathProp = new JsonObject();
        sourceOrFilePathProp.Set("type", RuntimeValue.String("string"));
        sourceOrFilePathProp.Set("description", RuntimeValue.String("MALDA source code or path to a .malda file. If the string looks like a file path (contains path separators or ends with .malda), it will be read from disk. Otherwise, it will be treated as source code to execute directly."));
        properties.Set("sourceOrFilePath", RuntimeValue.Object(sourceOrFilePathProp));
        
        var inputProp = new JsonObject();
        inputProp.Set("type", RuntimeValue.String("string"));
        inputProp.Set("description", RuntimeValue.String("Optional: Standard input for the MALDA program (will be available via input() calls)."));
        properties.Set("input", RuntimeValue.Object(inputProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("sourceOrFilePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "run_malda",
            "Executes MALDA code from a file path or source string and returns the output and any errors. Use this to test MALDA code after making edits. Returns an object with 'success' (boolean), 'output' (string), 'error' (string for parse errors), and optional 'runtimeError' (string).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateCompileMALDATool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var sourcePathProp = new JsonObject();
        sourcePathProp.Set("type", RuntimeValue.String("string"));
        sourcePathProp.Set("description", RuntimeValue.String("Path to the MALDA source file (.malda) to compile."));
        properties.Set("sourcePath", RuntimeValue.Object(sourcePathProp));
        
        var outputPathProp = new JsonObject();
        outputPathProp.Set("type", RuntimeValue.String("string"));
        outputPathProp.Set("description", RuntimeValue.String("Optional: Output path for the compiled executable. If not provided, defaults to the source file name with .exe extension."));
        properties.Set("outputPath", RuntimeValue.Object(outputPathProp));
        
        var modeProp = new JsonObject();
        modeProp.Set("type", RuntimeValue.String("string"));
        modeProp.Set("description", RuntimeValue.String("Optional: Compilation mode. Must be 'interpreter' (default) or 'transpile'. 'interpreter' mode embeds the MALDA source and uses the interpreter at runtime. 'transpile' mode transpiles MALDA to C# and compiles the C# code."));
        modeProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue> 
        { 
            RuntimeValue.String("interpreter"), 
            RuntimeValue.String("transpile") 
        }));
        properties.Set("mode", RuntimeValue.Object(modeProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("sourcePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "compile_malda",
            "Compiles MALDA source code to an executable and returns structured build feedback. Use this to compile MALDA files after making edits and get compiler errors. Returns an object with 'success' (boolean), 'outputPath' (string or null), 'error' (string), and 'errors' (array of error objects with 'message', 'line', 'column'). Supports both 'interpreter' mode (embeds MALDA source) and 'transpile' mode (transpiles to C#).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGetSymbolsTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var filePathOrSourceProp = new JsonObject();
        filePathOrSourceProp.Set("type", RuntimeValue.String("string"));
        filePathOrSourceProp.Set("description", RuntimeValue.String("Path to a .malda file or MALDA source code string. If the string looks like a file path (contains path separators or ends with .malda), the file will be read from disk and its content will be parsed. Otherwise, it will be treated as source code to parse directly."));
        properties.Set("filePathOrSource", RuntimeValue.Object(filePathOrSourceProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("filePathOrSource") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "get_symbols",
            "Parses MALDA code and extracts structured symbol information (classes, functions, actors) with line numbers and signatures. Returns parse errors if code is invalid. Returns an object with 'classes' (array), 'functions' (array), 'actors' (array), and 'parseErrors' (array).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateGetParseErrorsTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var sourceOrFilePathProp = new JsonObject();
        sourceOrFilePathProp.Set("type", RuntimeValue.String("string"));
        sourceOrFilePathProp.Set("description", RuntimeValue.String("Path to a .malda file or MALDA source code string. If the string looks like a file path (contains path separators or ends with .malda), the file will be read from disk and its content will be parsed. Otherwise, it will be treated as source code to parse directly."));
        properties.Set("sourceOrFilePath", RuntimeValue.Object(sourceOrFilePathProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("sourceOrFilePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "get_parse_errors",
            "Parses MALDA code and returns only parse errors (line, column, message). Accepts file path or source string. Use to validate syntax without running or compiling.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateCheckMaldaTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var sourceOrFilePathProp = new JsonObject();
        sourceOrFilePathProp.Set("type", RuntimeValue.String("string"));
        sourceOrFilePathProp.Set("description", RuntimeValue.String("Path to a .malda file (relative to the working directory) or MALDA source code. If the string contains path separators or ends with .malda, the file is read and diagnosed. Otherwise it is treated as inline source (file label \"<eval>\")."));
        properties.Set("sourceOrFilePath", RuntimeValue.Object(sourceOrFilePathProp));

        var typeModeProp = new JsonObject();
        typeModeProp.Set("type", RuntimeValue.String("string"));
        typeModeProp.Set("description", RuntimeValue.String("Optional type-checking mode. 'default' matches the IDE (type mismatches are errors). 'strict' enables the full CLI suite. 'lenient' reports type mismatches as warnings."));
        typeModeProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("default"),
            RuntimeValue.String("strict"),
            RuntimeValue.String("lenient")
        }));
        properties.Set("typeMode", RuntimeValue.Object(typeModeProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("sourceOrFilePath") }));

        tool.Initialize(
            "check_malda",
            "Diagnose MALDA code with the same LanguageService diagnostics as 'malda check --json' (parse, types, schema, interpolation). Accepts a file path or inline source. Does not execute the program. Returns { ok, executed, file, errorCount, warningCount, infoCount, error?, diagnostics }.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );

        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateSubmitPlanTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planProp = new JsonObject();
        planProp.Set("type", RuntimeValue.String("object"));
        planProp.Set("description", RuntimeValue.String("Plan object with 'steps' array (each step: { id: string, description: string, dependsOn?: string[] }) or pass 'steps' directly as array. Optional 'taskSummary' (string)."));
        properties.Set("plan", RuntimeValue.Object(planProp));
        var stepsProp = new JsonObject();
        stepsProp.Set("type", RuntimeValue.String("array"));
        stepsProp.Set("description", RuntimeValue.String("Alternative to 'plan': array of steps [{ id, description, dependsOn? }, ...]."));
        properties.Set("steps", RuntimeValue.Object(stepsProp));
        var taskSummaryProp = new JsonObject();
        taskSummaryProp.Set("type", RuntimeValue.String("string"));
        taskSummaryProp.Set("description", RuntimeValue.String("Optional: short summary of the task."));
        properties.Set("taskSummary", RuntimeValue.Object(taskSummaryProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>()));
        tool.Initialize(
            "submit_plan",
            "Submit a structured plan before or during execution. Use when you have broken down the task into steps. Pass either a 'plan' object with 'steps' (and optional 'taskSummary') or a 'steps' array. Each step must have 'id' (string) and 'description' (string); optional 'dependsOn' (array of step ids). Returns { accepted: true, planId, stepCount } or { accepted: false, error }.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateUpdatePlanTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planIdProp = new JsonObject();
        planIdProp.Set("type", RuntimeValue.String("string"));
        planIdProp.Set("description", RuntimeValue.String("Id of the stored plan to update (from submit_plan)."));
        properties.Set("planId", RuntimeValue.Object(planIdProp));
        var stepsProp = new JsonObject();
        stepsProp.Set("type", RuntimeValue.String("array"));
        stepsProp.Set("description", RuntimeValue.String("Optional replacement steps [{ id, description, dependsOn? }, ...]. Re-validated; surviving ids keep their status; new ids start pending; omitted ids are dropped."));
        properties.Set("steps", RuntimeValue.Object(stepsProp));
        var taskSummaryProp = new JsonObject();
        taskSummaryProp.Set("type", RuntimeValue.String("string"));
        taskSummaryProp.Set("description", RuntimeValue.String("Optional: replace the stored task summary."));
        properties.Set("taskSummary", RuntimeValue.Object(taskSummaryProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("planId") }));
        tool.Initialize(
            "update_plan",
            "Update a stored structured plan. Requires planId. Optional steps are re-validated (surviving step ids keep their status; new ids are pending). Optional taskSummary replaces the stored summary. Returns { accepted: true, planId, stepCount, steps } or { accepted: false, error }. Not parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateMarkStepTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planIdProp = new JsonObject();
        planIdProp.Set("type", RuntimeValue.String("string"));
        planIdProp.Set("description", RuntimeValue.String("Id of the stored plan (from submit_plan)."));
        properties.Set("planId", RuntimeValue.Object(planIdProp));
        var idProp = new JsonObject();
        idProp.Set("type", RuntimeValue.String("string"));
        idProp.Set("description", RuntimeValue.String("Step id to update."));
        properties.Set("id", RuntimeValue.Object(idProp));
        var statusProp = new JsonObject();
        statusProp.Set("type", RuntimeValue.String("string"));
        statusProp.Set("description", RuntimeValue.String("New step status: pending, in_progress, done, or blocked."));
        statusProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("pending"),
            RuntimeValue.String("in_progress"),
            RuntimeValue.String("done"),
            RuntimeValue.String("blocked")
        }));
        properties.Set("status", RuntimeValue.Object(statusProp));
        var noteProp = new JsonObject();
        noteProp.Set("type", RuntimeValue.String("string"));
        noteProp.Set("description", RuntimeValue.String("Optional note stored on the step."));
        properties.Set("note", RuntimeValue.Object(noteProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("planId"),
            RuntimeValue.String("id"),
            RuntimeValue.String("status")
        }));
        tool.Initialize(
            "mark_step",
            "Set the status of a stored plan step. Requires planId, id, and status (pending | in_progress | done | blocked). Optional note is stored on the step. Returns { accepted: true, planId, id, status } or { accepted: false, error }. Not parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }
    
    public static RuntimeValue CreateCreateMcpAgentScriptTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var agentNameProp = new JsonObject();
        agentNameProp.Set("type", RuntimeValue.String("string"));
        agentNameProp.Set("description", RuntimeValue.String("Name of the agent (e.g., 'DataAnalyst', 'CodeReviewer'). Must be a valid identifier."));
        properties.Set("agentName", RuntimeValue.Object(agentNameProp));
        
        var agentRoleProp = new JsonObject();
        agentRoleProp.Set("type", RuntimeValue.String("string"));
        agentRoleProp.Set("description", RuntimeValue.String("Role description for the agent (e.g., 'Data Analysis Specialist', 'Code Review Expert')."));
        properties.Set("agentRole", RuntimeValue.Object(agentRoleProp));
        
        var agentInstructionsProp = new JsonObject();
        agentInstructionsProp.Set("type", RuntimeValue.String("string"));
        agentInstructionsProp.Set("description", RuntimeValue.String("Detailed instructions for the agent's behavior and capabilities. This will be used as the agent's system instructions."));
        properties.Set("agentInstructions", RuntimeValue.Object(agentInstructionsProp));
        
        var toolsProp = new JsonObject();
        toolsProp.Set("type", RuntimeValue.String("array"));
        toolsProp.Set("description", RuntimeValue.String("Array of tool definitions. Each tool must be an object with 'name' (string, required), 'description' (string, required), and optional 'schema' (string, JSON schema as string). Example: [{\"name\": \"analyze_data\", \"description\": \"Analyzes data and returns insights\"}]"));
        var toolItemSchema = new JsonObject();
        toolItemSchema.Set("type", RuntimeValue.String("object"));
        var toolItemProperties = new JsonObject();
        var toolNameProp = new JsonObject();
        toolNameProp.Set("type", RuntimeValue.String("string"));
        toolItemProperties.Set("name", RuntimeValue.Object(toolNameProp));
        var toolDescProp = new JsonObject();
        toolDescProp.Set("type", RuntimeValue.String("string"));
        toolItemProperties.Set("description", RuntimeValue.Object(toolDescProp));
        var toolSchemaProp = new JsonObject();
        toolSchemaProp.Set("type", RuntimeValue.String("string"));
        toolItemProperties.Set("schema", RuntimeValue.Object(toolSchemaProp));
        toolItemSchema.Set("properties", RuntimeValue.Object(toolItemProperties));
        var toolRequired = new List<RuntimeValue> { RuntimeValue.String("name"), RuntimeValue.String("description") };
        toolItemSchema.Set("required", RuntimeValue.Array(toolRequired));
        toolsProp.Set("items", RuntimeValue.Object(toolItemSchema));
        properties.Set("tools", RuntimeValue.Object(toolsProp));
        
        var outputPathProp = new JsonObject();
        outputPathProp.Set("type", RuntimeValue.String("string"));
        outputPathProp.Set("description", RuntimeValue.String("Path where the generated MALDA script should be saved (e.g., 'agents/data_analyst_mcp.malda'). Directory will be created if it doesn't exist."));
        properties.Set("outputPath", RuntimeValue.Object(outputPathProp));
        
        var modelProp = new JsonObject();
        modelProp.Set("type", RuntimeValue.String("string"));
        modelProp.Set("description", RuntimeValue.String("Optional: LLM model to use for the agent (e.g., 'openai/gpt-4', 'anthropic/claude-3.5-sonnet'). Defaults to 'openai/gpt-4'."));
        properties.Set("model", RuntimeValue.Object(modelProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> 
        { 
            RuntimeValue.String("agentName"), 
            RuntimeValue.String("agentRole"),
            RuntimeValue.String("agentInstructions"),
            RuntimeValue.String("tools"),
            RuntimeValue.String("outputPath")
        };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "create_mcp_agent_script",
            "Generates a MALDA script that creates an MCP server for a specialized agent. The script will include @MCPTool decorated functions that expose the agent's capabilities as MCP tools. Use this when you need to create a new specialized subagent that can be used as an MCP server. Returns an object with 'success' (boolean), 'outputPath' (string, full path to generated file), 'scriptContent' (string, the generated script), and 'error' (string, error message if failed).",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        
        return RuntimeValue.Object(tool);
    }
}