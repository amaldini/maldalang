// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.IO;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// DevAgent tools for scoped code-structure memory (analyzeFile / findCodeRelationships).
/// </summary>
public class DevAgentCodeMemoryToolInstance : ToolInstance
{
    private readonly GraphMemoryInstance _memory;
    private readonly Interpreter _interpreter;
    private readonly string _workingDirectory;
    private readonly bool _isFindRelationships;

    private DevAgentCodeMemoryToolInstance(
        GraphMemoryInstance memory,
        Interpreter interpreter,
        string workingDirectory,
        string name,
        string description,
        RuntimeValue parameters,
        bool isFindRelationships)
    {
        _memory = memory;
        _interpreter = interpreter;
        _workingDirectory = workingDirectory;
        _isFindRelationships = isFindRelationships;
        Initialize(name, description, parameters, null, workingDirectory);
    }

    public static DevAgentCodeMemoryToolInstance CreateIndexCodeFileTool(
        GraphMemoryInstance memory,
        Interpreter interpreter,
        string workingDirectory)
    {
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var pathProp = new JsonObject();
        pathProp.Set("type", RuntimeValue.String("string"));
        pathProp.Set("description", RuntimeValue.String("Relative file path under the agent working directory to index into code memory."));
        properties.Set("path", RuntimeValue.Object(pathProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("path") }));

        return new DevAgentCodeMemoryToolInstance(
            memory,
            interpreter,
            workingDirectory,
            "index_code_file",
            "Index a source file's classes and functions into GraphMemory code structure (analyzeFile).",
            RuntimeValue.Object(parameters),
            isFindRelationships: false);
    }

    public static DevAgentCodeMemoryToolInstance CreateFindCodeRelationshipsTool(
        GraphMemoryInstance memory,
        Interpreter interpreter,
        string workingDirectory)
    {
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var elementProp = new JsonObject();
        elementProp.Set("type", RuntimeValue.String("string"));
        elementProp.Set("description", RuntimeValue.String("Code element id, e.g. ClassName.methodName or file path key used by analyzeFile."));
        properties.Set("elementId", RuntimeValue.Object(elementProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("elementId") }));

        return new DevAgentCodeMemoryToolInstance(
            memory,
            interpreter,
            workingDirectory,
            "find_code_relationships",
            "Find graph relationships for a previously indexed code element.",
            RuntimeValue.Object(parameters),
            isFindRelationships: true);
    }

    public RuntimeValue ExecuteCodeMemoryTool(RuntimeValue arguments)
    {
        if (arguments.Type != ValueType.Object)
            return RuntimeValue.String("Error: Tool arguments must be an object");

        var argsObj = arguments.AsObject();
        if (_isFindRelationships)
            return ExecuteFindRelationships(argsObj);

        var pathVal = argsObj.Get("path", null);
        if (pathVal == null || pathVal.Type != ValueType.String || string.IsNullOrWhiteSpace(pathVal.AsString()))
            return RuntimeValue.String("Error: index_code_file requires a non-empty 'path' string");

        var normalized = NormalizePathForWorkingDirectory(pathVal.AsString().Trim());
        if (normalized == null)
            return RuntimeValue.String("Error: path is outside the agent working directory");

        var fullPath = Path.GetFullPath(Path.Combine(Path.GetFullPath(_workingDirectory), normalized));
        if (!File.Exists(fullPath))
            return RuntimeValue.String($"Error: file not found: {normalized}");

        try
        {
            _memory.CallMethod("analyzeFile", new List<RuntimeValue> { RuntimeValue.String(fullPath) }, _interpreter);
            return RuntimeValue.String($"Indexed code structure for {normalized}");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error indexing file: {ex.Message}");
        }
    }

    private RuntimeValue ExecuteFindRelationships(ObjectInstance argsObj)
    {
        var elementVal = argsObj.Get("elementId", null);
        if (elementVal == null || elementVal.Type != ValueType.String || string.IsNullOrWhiteSpace(elementVal.AsString()))
            return RuntimeValue.String("Error: find_code_relationships requires a non-empty 'elementId' string");

        try
        {
            var results = _memory.CallMethod(
                "findCodeRelationships",
                new List<RuntimeValue> { elementVal },
                _interpreter);

            if (results.Type != ValueType.Array || results.AsArray().Count == 0)
                return RuntimeValue.String("No code relationships found for that element.");

            return RuntimeValue.String($"Found {results.AsArray().Count} relationship(s).");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
}
