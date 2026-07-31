// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Agent tools for explicit progress memory: remember_progress and recall_progress.
/// </summary>
public class MemoryProgressToolInstance : ToolInstance
{
    private readonly GraphMemoryInstance _memory;
    private readonly Interpreter _interpreter;
    private readonly bool _isRecallTool;
    private readonly string? _memoryScope;

    private MemoryProgressToolInstance(
        GraphMemoryInstance memory,
        Interpreter interpreter,
        string? memoryScope,
        string name,
        string description,
        RuntimeValue parameters,
        bool isRecallTool)
    {
        _memory = memory;
        _interpreter = interpreter;
        _memoryScope = memoryScope;
        _isRecallTool = isRecallTool;
        Initialize(name, description, parameters, null);
    }

    public static MemoryProgressToolInstance CreateRememberTool(GraphMemoryInstance memory, Interpreter interpreter, string? memoryScope = null)
    {
        var parameters = new JsonObject();
        var properties = new JsonObject();

        var noteProp = new JsonObject();
        noteProp.Set("type", RuntimeValue.String("string"));
        noteProp.Set("description", RuntimeValue.String("Short progress note to persist across iterations (decisions, files touched, blockers)."));
        properties.Set("note", RuntimeValue.Object(noteProp));

        var typeProp = new JsonObject();
        typeProp.Set("type", RuntimeValue.String("string"));
        typeProp.Set("description", RuntimeValue.String("Optional category: progress, decision, file, error."));
        properties.Set("type", RuntimeValue.Object(typeProp));

        var phaseProp = new JsonObject();
        phaseProp.Set("type", RuntimeValue.String("string"));
        phaseProp.Set("description", RuntimeValue.String("Optional PRD phase or feature name this note belongs to."));
        properties.Set("phase", RuntimeValue.Object(phaseProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("note") }));

        return new MemoryProgressToolInstance(
            memory,
            interpreter,
            memoryScope,
            "remember_progress",
            "Save a concise progress note to GraphMemory for future iterations. Use after meaningful decisions or when finishing a sub-step.",
            RuntimeValue.Object(parameters),
            isRecallTool: false);
    }

    public static MemoryProgressToolInstance CreateRecallTool(GraphMemoryInstance memory, Interpreter interpreter, string? memoryScope = null)
    {
        var parameters = new JsonObject();
        var properties = new JsonObject();

        var queryProp = new JsonObject();
        queryProp.Set("type", RuntimeValue.String("string"));
        queryProp.Set("description", RuntimeValue.String("Optional search query. Defaults to project progress and recent notes."));
        properties.Set("query", RuntimeValue.Object(queryProp));

        var maxResultsProp = new JsonObject();
        maxResultsProp.Set("type", RuntimeValue.String("integer"));
        maxResultsProp.Set("description", RuntimeValue.String("Maximum semantic matches to return (default 5)."));
        properties.Set("maxResults", RuntimeValue.Object(maxResultsProp));

        var phaseProp = new JsonObject();
        phaseProp.Set("type", RuntimeValue.String("string"));
        phaseProp.Set("description", RuntimeValue.String("Optional filter by PRD phase/feature name."));
        properties.Set("phase", RuntimeValue.Object(phaseProp));

        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));

        return new MemoryProgressToolInstance(
            memory,
            interpreter,
            memoryScope,
            "recall_progress",
            "Retrieve relevant progress notes from GraphMemory (recent notes plus semantic matches).",
            RuntimeValue.Object(parameters),
            isRecallTool: true);
    }

    public RuntimeValue ExecuteMemoryTool(RuntimeValue arguments)
    {
        if (arguments.Type != ValueType.Object)
            return RuntimeValue.String("Error: Tool arguments must be an object");

        var argsObj = arguments.AsObject();
        return _isRecallTool ? ExecuteRecall(argsObj) : ExecuteRemember(argsObj);
    }

    private RuntimeValue ExecuteRemember(ObjectInstance argsObj)
    {
        var noteVal = argsObj.Get("note", null);
        if (noteVal == null || noteVal.Type != ValueType.String || string.IsNullOrWhiteSpace(noteVal.AsString()))
            return RuntimeValue.String("Error: remember_progress requires a non-empty 'note' string");

        var note = noteVal.AsString().Trim();
        var memType = GetOptionalString(argsObj, "type") ?? "progress";
        var phase = GetOptionalString(argsObj, "phase");

        var metadata = new JsonObject();
        metadata.Set("type", RuntimeValue.String(memType));
        metadata.Set("source", RuntimeValue.String("agent_tool"));
        if (!string.IsNullOrWhiteSpace(phase))
            metadata.Set("phase", RuntimeValue.String(phase));
        if (!string.IsNullOrWhiteSpace(_memoryScope))
            metadata.Set("scope", RuntimeValue.String(_memoryScope));

        var nodeId = _memory.CallMethod(
            "remember",
            new List<RuntimeValue> { RuntimeValue.String(note), RuntimeValue.Null(), RuntimeValue.Object(metadata) },
            _interpreter);

        return RuntimeValue.String($"Saved progress note ({nodeId}).");
    }

    private RuntimeValue ExecuteRecall(ObjectInstance argsObj)
    {
        var query = GetOptionalString(argsObj, "query") ?? "project progress files changed decisions";
        var maxResults = 5;
        var maxVal = argsObj.Get("maxResults", null);
        if (maxVal != null && maxVal.Type == ValueType.Integer)
            maxResults = Math.Max(1, maxVal.AsInteger());

        var options = new JsonObject();
        options.Set("recentCount", RuntimeValue.Integer(5));
        options.Set("hybrid", RuntimeValue.Boolean(true));
        options.Set("minScore", RuntimeValue.Float(0.5));
        options.Set("synapse", RuntimeValue.Boolean(true));
        options.Set("hybridLexical", RuntimeValue.Boolean(true));
        options.Set("lexicalWeight", RuntimeValue.Float(0.25));
        options.Set("includeTypes", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("progress"),
            RuntimeValue.String("decision"),
            RuntimeValue.String("file"),
            RuntimeValue.String("error"),
            RuntimeValue.String("semantic")
        }));
        var phase = GetOptionalString(argsObj, "phase");
        if (!string.IsNullOrWhiteSpace(phase))
            options.Set("phase", RuntimeValue.String(phase));
        if (!string.IsNullOrWhiteSpace(_memoryScope))
            options.Set("scope", RuntimeValue.String(_memoryScope));

        var results = _memory.CallMethod(
            "query",
            new List<RuntimeValue>
            {
                RuntimeValue.String(query),
                RuntimeValue.Integer(maxResults),
                RuntimeValue.Object(options)
            },
            _interpreter);

        if (results.Type != ValueType.Array || results.AsArray().Count == 0)
            return RuntimeValue.String("No matching progress notes found.");

        var lines = new List<string>();
        foreach (var mem in results.AsArray())
            lines.Add("- " + GraphMemoryInstance.FormatMemoryLine(mem));

        return RuntimeValue.String(string.Join("\n", lines));
    }

    private static string? GetOptionalString(ObjectInstance argsObj, string key)
    {
        var val = argsObj.Get(key, null);
        if (val == null || val.Type != ValueType.String)
            return null;
        var text = val.AsString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
