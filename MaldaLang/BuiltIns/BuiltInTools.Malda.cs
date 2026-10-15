// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static partial class BuiltInTools
{
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
            "Executes MALDA code with the interpreter and returns output and errors. Use this to test interpreter programs after edits. game.*, dom.*, and three.* are JavaScript-only and fail here — verify those with compile_malda mode \"js\". Returns an object with 'success' (boolean), 'output' (string), 'error' (string for parse errors), and optional 'runtimeError' (string).",
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
        modeProp.Set("description", RuntimeValue.String("Optional: Compilation mode. 'interpreter' (default) embeds the MALDA source. 'transpile' emits C# and compiles it. 'js' emits browser JavaScript (required for game.*, dom.*, and three.*). Does not start a browser."));
        modeProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue> 
        { 
            RuntimeValue.String("interpreter"), 
            RuntimeValue.String("transpile"),
            RuntimeValue.String("js")
        }));
        properties.Set("mode", RuntimeValue.Object(modeProp));
        
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue> { RuntimeValue.String("sourcePath") };
        parameters.Set("required", RuntimeValue.Array(required));
        
        tool.Initialize(
            "compile_malda",
            "Compiles MALDA source and returns structured build feedback. Returns an object with 'success' (boolean), 'outputPath' (string or null), 'error' (string), and 'errors' (array of error objects with 'message', 'line', 'column'). Modes: 'interpreter' (embed source), 'transpile' (C# executable), and 'js' (browser JavaScript for game.*, dom.*, and three.*). 'js' does not launch malda play.",
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

    public static RuntimeValue CreateValidateJsonTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();

        var schemaProp = new JsonObject();
        schemaProp.Set("description", RuntimeValue.String("Registered schema or sum-type name (string), or an inline schema object. Same as validate(schema, value)."));
        properties.Set("schema", RuntimeValue.Object(schemaProp));

        var valueProp = new JsonObject();
        valueProp.Set("description", RuntimeValue.String("Value to check against the schema. Any JSON-shaped value is accepted."));
        properties.Set("value", RuntimeValue.Object(valueProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("schema"),
            RuntimeValue.String("value")
        }));

        tool.Initialize(
            "validate_json",
            "Validate a value against a registered schema or sum-type name, or an inline schema object. Same result as validate(schema, value): { ok, data } or { ok: false, error }. Parallel-safe; does not write files.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );

        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateTestMaldaTool(string workingDirectory = "")
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();

        var pathProp = new JsonObject();
        pathProp.Set("type", RuntimeValue.String("string"));
        pathProp.Set("description", RuntimeValue.String("File or directory to run (*.test.malda / *.spec.malda). Defaults to '.' relative to the working directory. Must stay inside the working directory."));
        properties.Set("path", RuntimeValue.Object(pathProp));

        var filterProp = new JsonObject();
        filterProp.Set("type", RuntimeValue.String("string"));
        filterProp.Set("description", RuntimeValue.String("Optional substring filter passed as --filter to malda test."));
        properties.Set("filter", RuntimeValue.Object(filterProp));

        var listOnlyProp = new JsonObject();
        listOnlyProp.Set("type", RuntimeValue.String("boolean"));
        listOnlyProp.Set("description", RuntimeValue.String("If true, list discovered tests without running them (--list)."));
        properties.Set("listOnly", RuntimeValue.Object(listOnlyProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>()));

        tool.Initialize(
            "test_malda",
            "Run MALDA tests via the same host path as 'malda test' (TestCommandRunner). Parameters: path? (file or directory, default '.'), filter?, listOnly?. Returns { success, exitCode, output, report? }. Not parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            workingDirectory
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
