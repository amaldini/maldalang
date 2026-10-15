// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static partial class BuiltInTools
{
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

    public static RuntimeValue CreateWebFetchTool(CapabilityToken? httpCap = null)
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();

        var properties = new JsonObject();

        var urlProp = new JsonObject();
        urlProp.Set("type", RuntimeValue.String("string"));
        urlProp.Set("description", RuntimeValue.String(
            httpCap == null
                ? "HTTP or HTTPS URL to fetch. Other schemes (file://, ftp://, …) are rejected."
                : "HTTP or HTTPS URL to fetch. Must stay under the host-minted httpGet origin (relative paths are joined)."));
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
            httpCap == null
                ? "Fetches an HTTP or HTTPS URL and returns the response body as text. Use for reading documentation or pages (not search). Parameters: url (required), maxBytes? (default 100000, cap 500000), timeoutMs? (default 15000, cap 60000). Returns { ok, status, url, content, truncated, error? }. JSON bodies are returned as a JSON string. Parallel-safe."
                : "Fetches an HTTP or HTTPS URL under a host-minted httpGet capability. The model only passes a URL or path; URLs outside the origin are rejected before the request. Parameters: url (required), maxBytes? (default 100000, cap 500000), timeoutMs? (default 15000, cap 60000). Returns { ok, status, url, content, truncated, error? }.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        tool.Capability = httpCap;

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
            return RuntimeValue.String($"Error executing glob tool: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
        }
    }

    public static RuntimeValue CreateRunCommandTool(string workingDirectory = "", CapabilityToken? shellCap = null)
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        
        var properties = new JsonObject();
        
        var commandProp = new JsonObject();
        commandProp.Set("type", RuntimeValue.String("string"));
        commandProp.Set("description", RuntimeValue.String(
            shellCap == null
                ? "Command to execute (e.g., 'dotnet', 'npm', 'python', 'echo'). The command name without arguments."
                : "Command to execute. Must match the host-minted shell capability prefix (the model cannot change the executable)."));
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
            shellCap == null
                ? "Executes a command-line program and returns exitCode, stdout, and stderr. Use direct executables (dotnet, npm, python) — not shell wrappers as the command name. Shell commands (powershell, cmd, bash) may require user confirmation before running."
                : "Executes a command-line program under a host-minted shell capability. The model may only append arguments; a different executable, `..`, or an absolute path is rejected before the process starts.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
        );
        tool.Capability = shellCap;
        
        return RuntimeValue.Object(tool);
    }
}
