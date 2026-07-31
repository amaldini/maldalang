// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public sealed class RetrieverInstance : ObjectInstance
{
    private readonly VectorDBInstance _vectorDb;
    private readonly int _topK;
    private readonly double _minScore;

    public RetrieverInstance(VectorDBInstance vectorDb, int topK, double minScore)
        : base(null)
    {
        _vectorDb = vectorDb;
        _topK = topK;
        _minScore = minScore;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "get" or "getFormatted")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        return base.Get(name, accessingClass);
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter interpreter)
    {
        return methodName switch
        {
            "get" => CallGet(arguments, interpreter, formatted: false),
            "getFormatted" => CallGet(arguments, interpreter, formatted: true),
            _ => throw new RuntimeException($"Unknown retriever method '{methodName}'.")
        };
    }

    private RuntimeValue CallGet(List<RuntimeValue> arguments, Interpreter interpreter, bool formatted)
    {
        if (arguments.Count < 1 || arguments[0].Type != ValueType.String)
            throw new RuntimeException("retriever.get() expects a string query.");

        var query = arguments[0].AsString();
        var hits = _vectorDb.CallMethod(
            "searchSimilar",
            new List<RuntimeValue> { RuntimeValue.String(query), RuntimeValue.Integer(_topK) },
            interpreter);

        var documents = ConvertHitsToDocuments(hits);
        if (formatted)
            return AiPipelineHelpers.FormatRetrievedDocs(new List<RuntimeValue> { RuntimeValue.Array(documents) });

        return RuntimeValue.Array(documents);
    }

    private List<RuntimeValue> ConvertHitsToDocuments(RuntimeValue hits)
    {
        var documents = new List<RuntimeValue>();
        if (hits.Type != ValueType.Array)
            return documents;

        foreach (var hit in hits.AsArray())
        {
            if (hit.Type != ValueType.Object || hit.AsObject() is not JsonObject hitObj)
                continue;

            var similarityValue = hitObj.Get("similarity");
            if (similarityValue.Type == ValueType.Float && similarityValue.AsFloat() < _minScore)
                continue;
            if (similarityValue.Type == ValueType.Integer && similarityValue.AsInteger() < _minScore)
                continue;

            var data = hitObj.Get("data");
            if (TryConvertStoredDataToDocument(data, similarityValue, out var document))
            {
                documents.Add(RuntimeValue.Object(document));
                continue;
            }

            var content = data.Type == ValueType.String ? data.AsString() : data.ToString();
            var metadata = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
            if (similarityValue.Type == ValueType.Float || similarityValue.Type == ValueType.Integer)
                metadata["score"] = similarityValue;

            documents.Add(RuntimeValue.Object(new DocumentInstance(content, metadata)));
        }

        return documents;
    }

    private static bool TryConvertStoredDataToDocument(
        RuntimeValue data,
        RuntimeValue similarityValue,
        out DocumentInstance document)
    {
        document = null!;
        if (data.Type != ValueType.Object)
            return false;

        string content;
        Dictionary<string, RuntimeValue> metadata = new(StringComparer.Ordinal);

        if (data.AsObject() is DocumentInstance doc)
        {
            content = doc.Content;
            foreach (var entry in doc.MetadataEntries)
                metadata[entry.Key] = entry.Value;
        }
        else if (data.AsObject() is JsonObject jsonObj)
        {
            var contentValue = jsonObj.Get("content");
            if (contentValue.Type != ValueType.String)
                return false;
            content = contentValue.AsString();
            CopyMetadataFromValue(jsonObj.Get("metadata"), metadata);
        }
        else
        {
            return false;
        }

        if (similarityValue.Type == ValueType.Float || similarityValue.Type == ValueType.Integer)
            metadata["score"] = similarityValue;

        document = new DocumentInstance(content, metadata);
        return true;
    }

    private static void CopyMetadataFromValue(RuntimeValue metadataValue, Dictionary<string, RuntimeValue> metadata)
    {
        if (metadataValue.Type != ValueType.Object)
            return;

        if (metadataValue.AsObject() is DictionaryInstance metadataDict)
        {
            foreach (var key in metadataDict.GetKeys())
            {
                if (metadataDict.TryGetEntry(key, out var entryValue))
                    metadata[key] = entryValue;
            }
            return;
        }

        if (metadataValue.AsObject() is JsonObject metadataJson)
        {
            foreach (var kvp in metadataJson.GetProperties())
                metadata[kvp.Key] = kvp.Value;
        }
    }
}
