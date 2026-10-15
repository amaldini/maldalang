// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;

public partial class GraphMemoryInstance
{
    private RuntimeValue CallRemember(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new RuntimeException("remember() expects at least 1 argument (fact, context?)");
        
        EnsureInitialized();
        
        var fact = args[0];
        RuntimeValue? context = args.Count > 1 && args[1].Type != ValueType.Object ? args[1] : null;
        JsonObject? metadataObj = null;
        if (args.Count >= 3 && args[2].Type == ValueType.Object)
            metadataObj = CoerceToJsonObject(args[2]);
        else if (args.Count >= 2 && args[1].Type == ValueType.Object)
            metadataObj = CoerceToJsonObject(args[1]);
        
        var descriptionBuilder = new System.Text.StringBuilder(BuildNodeDescription(fact, context));
        if (metadataObj != null)
            AppendMetadataToDescription(descriptionBuilder, metadataObj);
        var description = descriptionBuilder.ToString();
        
        var duplicateId = TryFindDuplicateNodeId(description, fact);
        if (duplicateId != null)
        {
            UpdateExistingMemory(duplicateId, fact, context, metadataObj, description);
            return RuntimeValue.String(duplicateId);
        }
        
        var nodeId = $"node_{_nodeIdCounter++}";
        var nodeData = BuildNodeData(fact, context, metadataObj);
        StoreNewMemory(nodeId, fact, nodeData, description);
        LinkSimilarMemories(nodeId, description);
        TryDetectSupersedes(nodeId, description, metadataObj);
        
        return RuntimeValue.String(nodeId);
    }
    
    private JsonObject BuildNodeData(RuntimeValue fact, RuntimeValue? context, JsonObject? metadataObj)
    {
        var nodeData = new JsonObject();
        nodeData.Set("fact", fact);
        if (context != null)
            nodeData.Set("context", context);
        var now = DateTime.UtcNow.ToString("O");
        nodeData.Set("timestamp", RuntimeValue.String(now));
        if (metadataObj != null)
            ApplyMetadataFields(nodeData, metadataObj);
        
        if (nodeData.Get("accessCount", null)?.Type == ValueType.Null)
            nodeData.Set("accessCount", RuntimeValue.Integer(0));
        if (nodeData.Get("lastAccessed", null)?.Type == ValueType.Null)
            nodeData.Set("lastAccessed", RuntimeValue.String(now));
        if (nodeData.Get("importance", null)?.Type == ValueType.Null)
            nodeData.Set("importance", RuntimeValue.Float(0.5));
        if (nodeData.Get("dualIndexMigrated", null)?.Type == ValueType.Null)
            nodeData.Set("dualIndexMigrated", RuntimeValue.Boolean(true));
        return nodeData;
    }
    
    private void StoreNewMemory(string nodeId, RuntimeValue fact, JsonObject nodeData, string description)
    {
        nodeData.Set("nodeId", RuntimeValue.String(nodeId));
        _knowledgeGraph!.CallMethod("addNode", new List<RuntimeValue>
        {
            RuntimeValue.String(nodeId),
            RuntimeValue.Object(nodeData)
        }, _interpreter!);
        
        IndexNodeVector(nodeId, fact, description);
        _nodeMetadata[nodeId] = RuntimeValue.Object(nodeData);
        IndexBm25Document(nodeId, description);
    }
    
    private void IndexNodeVector(string nodeId, RuntimeValue fact, string description)
    {
        AddVectorIndexEntry(nodeId, fact, description, "body", 0.9);
        var headText = fact.Type == ValueType.String ? fact.AsString() : description;
        AddVectorIndexEntry(nodeId, fact, headText, "head", 1.0);
    }
    
    private void AddVectorIndexEntry(string nodeId, RuntimeValue fact, string text, string vectorKind, double vectorWeight)
    {
        var indexData = new JsonObject();
        indexData.Set("nodeId", RuntimeValue.String(nodeId));
        indexData.Set("description", RuntimeValue.String(text));
        indexData.Set("fact", fact);
        indexData.Set("vectorKind", RuntimeValue.String(vectorKind));
        indexData.Set("vectorWeight", RuntimeValue.Float(vectorWeight));
        
        var embedding = CalculateEmbedding(text);
        _nodeIndex!.CallMethod("add", new List<RuntimeValue>
        {
            RuntimeValue.Array(embedding),
            RuntimeValue.Object(indexData)
        }, _interpreter!);
    }
    
    private string? TryFindDuplicateNodeId(string description, RuntimeValue fact)
    {
        if (_nodeIndex == null || _interpreter == null || _nodeMetadata.Count == 0)
            return null;
        
        RuntimeValue searchResults;
        try
        {
            searchResults = _nodeIndex.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(description),
                RuntimeValue.Integer(1)
            }, _interpreter);
        }
        catch
        {
            return null;
        }
        
        if (searchResults.Type != ValueType.Array || searchResults.AsArray().Count == 0)
            return null;
        
        if (!TryExtractSearchHit(searchResults.AsArray()[0], out var existingId, out var similarity))
            return null;
        
        if (similarity < DefaultDedupSimilarity || !_nodeMetadata.ContainsKey(existingId))
            return null;
        
        if (!IsDuplicateFact(fact, existingId) && !string.Equals(description, GetStoredDescription(existingId), StringComparison.Ordinal))
            return null;
        
        return existingId;
    }
    
    private bool IsDuplicateFact(RuntimeValue fact, string existingId)
    {
        if (fact.Type != ValueType.String || !_nodeMetadata.TryGetValue(existingId, out var existingValue))
            return false;
        
        if (existingValue.Type != ValueType.Object || existingValue.AsObject() is not JsonObject existingObj)
            return false;
        
        var existingFact = existingObj.Get("fact");
        if (existingFact.Type != ValueType.String)
            return false;
        
        return string.Equals(fact.AsString().Trim(), existingFact.AsString().Trim(), StringComparison.OrdinalIgnoreCase);
    }
    
    private string GetStoredDescription(string nodeId)
    {
        if (!_nodeMetadata.TryGetValue(nodeId, out var nodeValue)
            || nodeValue.Type != ValueType.Object
            || nodeValue.AsObject() is not JsonObject nodeObj)
            return "";
        
        var fact = nodeObj.Get("fact");
        var context = nodeObj.Get("context", null);
        var builder = new System.Text.StringBuilder(BuildNodeDescription(fact, context.Type != ValueType.Null ? context : null));
        AppendMetadataToDescription(builder, nodeObj);
        return builder.ToString();
    }
    
    private void UpdateExistingMemory(string nodeId, RuntimeValue fact, RuntimeValue? context, JsonObject? metadataObj, string description)
    {
        var nodeData = BuildNodeData(fact, context, metadataObj);
        nodeData.Set("nodeId", RuntimeValue.String(nodeId));
        _knowledgeGraph!.CallMethod("setNodeData", new List<RuntimeValue>
        {
            RuntimeValue.String(nodeId),
            RuntimeValue.Object(nodeData)
        }, _interpreter!);
        
        _nodeIndex!.RemoveEntriesForNodeId(nodeId);
        IndexNodeVector(nodeId, fact, description);
        _nodeMetadata[nodeId] = RuntimeValue.Object(nodeData);
        IndexBm25Document(nodeId, description);
        TryDetectSupersedes(nodeId, description, metadataObj);
    }
    
    private RuntimeValue CallForget(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("forget() expects 1 string argument (nodeId)");
        
        EnsureInitialized();
        return RuntimeValue.Boolean(RemoveNodeById(args[0].AsString()));
    }
    
    private bool RemoveNodeById(string nodeId)
    {
        if (!_nodeMetadata.ContainsKey(nodeId))
            return false;
        
        if (_knowledgeGraph!.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(nodeId) }, _interpreter!).AsBoolean())
            _knowledgeGraph.CallMethod("removeNode", new List<RuntimeValue> { RuntimeValue.String(nodeId) }, _interpreter!);
        
        _nodeIndex!.RemoveEntriesForNodeId(nodeId);
        _bm25Index.RemoveDocument(nodeId);
        _nodeMetadata.Remove(nodeId);
        return true;
    }

    private void IndexBm25Document(string nodeId, string description) =>
        _bm25Index.IndexDocument(nodeId, description);

    private void RebuildBm25Index()
    {
        _bm25Index.Clear();
        foreach (var kvp in _nodeMetadata)
            IndexBm25Document(kvp.Key, GetStoredDescription(kvp.Key));
    }

    private void LinkSimilarMemories(string newNodeId, string description)
    {
        if (_nodeMetadata.Count <= 1 || _nodeIndex == null || _knowledgeGraph == null || _interpreter == null)
            return;
        
        RuntimeValue searchResults;
        try
        {
            searchResults = _nodeIndex.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(description),
                RuntimeValue.Integer(DefaultMaxSimilarLinks + 1)
            }, _interpreter);
        }
        catch
        {
            return;
        }
        
        if (searchResults.Type != ValueType.Array)
            return;
        
        int linked = 0;
        foreach (var result in searchResults.AsArray())
        {
            if (linked >= DefaultMaxSimilarLinks)
                break;
            
            if (!TryExtractSearchHit(result, out var existingId, out var similarity))
                continue;
            
            if (existingId == newNodeId || !_nodeMetadata.ContainsKey(existingId))
                continue;
            
            if (similarity < DefaultMinSimilarity)
                continue;
            
            AddRelatedEdgeIfMissing(newNodeId, existingId, similarity);
            AddRelatedEdgeIfMissing(existingId, newNodeId, similarity);
            linked++;
        }
    }
    
    private static bool TryExtractSearchHit(RuntimeValue result, out string nodeId, out double similarity)
    {
        nodeId = "";
        similarity = 0.0;

        if (result.Type != ValueType.Object)
            return false;

        var resultObj = result.AsObject();
        if (TryGetObjectProperty(resultObj, "similarity", out var similarityVal))
        {
            if (similarityVal.Type == ValueType.Float)
                similarity = similarityVal.AsFloat();
            else if (similarityVal.Type == ValueType.Integer)
                similarity = similarityVal.AsInteger();
        }

        if (!TryGetObjectProperty(resultObj, "data", out var data) || data.Type != ValueType.Object)
            return false;

        if (!TryGetObjectProperty(data.AsObject(), "nodeId", out var nodeIdVal) || nodeIdVal.Type != ValueType.String)
            return false;

        nodeId = nodeIdVal.AsString();
        return !string.IsNullOrEmpty(nodeId);
    }

    private static bool TryGetObjectProperty(ObjectInstance? obj, string name, out RuntimeValue value)
    {
        value = RuntimeValue.Null();
        if (obj == null)
            return false;
        if (obj is JsonObject jsonObj)
        {
            if (!jsonObj.GetProperties().ContainsKey(name))
                return false;
            value = jsonObj.Get(name);
            return true;
        }
        if (obj is DictionaryInstance dict)
            return dict.TryGetEntry(name, out value);
        if (obj.TryGet(name, out var field) && field != null)
        {
            value = field;
            return true;
        }
        return false;
    }
    
    private void AddRelatedEdgeIfMissing(string fromId, string toId, double weight)
    {
        if (_knowledgeGraph == null || _interpreter == null)
            return;
        
        if (!_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(fromId) }, _interpreter).AsBoolean()
            || !_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(toId) }, _interpreter).AsBoolean())
            return;
        
        if (_knowledgeGraph.CallMethod("hasEdge", new List<RuntimeValue>
            {
                RuntimeValue.String(fromId),
                RuntimeValue.String(toId)
            }, _interpreter).AsBoolean())
            return;
        
        var edgeProps = new DictionaryInstance();
        edgeProps.SetEntry("type", RuntimeValue.String(RelatedEdgeType));
        _knowledgeGraph.CallMethod("addEdge", new List<RuntimeValue>
        {
            RuntimeValue.String(fromId),
            RuntimeValue.String(toId),
            RuntimeValue.Float(weight),
            RuntimeValue.Object(edgeProps)
        }, _interpreter);
    }
    
    private bool HasIncomingEdgeType(string nodeId, string edgeType)
    {
        if (_knowledgeGraph == null || _interpreter == null)
            return false;
        var edges = _knowledgeGraph.CallMethod("edges", new List<RuntimeValue>(), _interpreter);
        if (edges.Type != ValueType.Array)
            return false;
        
        foreach (var edge in edges.AsArray())
        {
            if (edge.Type != ValueType.Object || edge.AsObject() is not JsonObject edgeJson)
                continue;
            var toVal = edgeJson.Get("to", null);
            if (toVal == null || toVal.Type != ValueType.String || !string.Equals(toVal.AsString(), nodeId, StringComparison.Ordinal))
                continue;
            var edgeTypeVal = edgeJson.Get("type", null);
            if (edgeTypeVal != null && edgeTypeVal.Type == ValueType.String
                && string.Equals(edgeTypeVal.AsString(), edgeType, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
    
    private void BumpNodeAccess(string nodeId)
    {
        if (!_nodeMetadata.TryGetValue(nodeId, out var nodeValue)
            || nodeValue.Type != ValueType.Object
            || nodeValue.AsObject() is not JsonObject nodeObj)
            return;
        
        var accessCount = GetNodeAccessCount(nodeObj) + 1;
        nodeObj.Set("accessCount", RuntimeValue.Integer(accessCount));
        nodeObj.Set("lastAccessed", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
        var boosted = Math.Clamp(ComputeImportance(nodeObj), 0.0, 1.0);
        nodeObj.Set("importance", RuntimeValue.Float(boosted));
        _nodeMetadata[nodeId] = RuntimeValue.Object(nodeObj);
        _knowledgeGraph?.CallMethod("setNodeData", new List<RuntimeValue>
        {
            RuntimeValue.String(nodeId),
            RuntimeValue.Object(nodeObj)
        }, _interpreter!);
    }
    
    private void TryDetectSupersedes(string newNodeId, string description, JsonObject? metadataObj)
    {
        if (_nodeIndex == null || _interpreter == null || _knowledgeGraph == null)
            return;
        
        var newType = metadataObj != null ? GetMetadataString(metadataObj, "type") : null;
        if (!string.Equals(newType, "semantic", StringComparison.OrdinalIgnoreCase))
            return;
        
        var newScope = metadataObj != null ? GetMetadataString(metadataObj, "scope") : null;
        RuntimeValue searchResults;
        try
        {
            searchResults = _nodeIndex.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(description),
                RuntimeValue.Integer(6)
            }, _interpreter);
        }
        catch
        {
            return;
        }
        
        if (searchResults.Type != ValueType.Array)
            return;
        
        foreach (var hit in searchResults.AsArray())
        {
            if (!TryExtractSearchHit(hit, out var existingId, out var similarity))
                continue;
            if (existingId == newNodeId || similarity < SupersedesSimilarityThreshold)
                continue;
            if (!_nodeMetadata.TryGetValue(existingId, out var existingValue)
                || existingValue.Type != ValueType.Object
                || existingValue.AsObject() is not JsonObject existingObj)
                continue;
            var existingType = existingObj.Get("type", null);
            if (existingType == null || existingType.Type != ValueType.String
                || !string.Equals(existingType.AsString(), "semantic", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(newScope)
                && !string.Equals(GetNodeScope(existingObj), newScope, StringComparison.OrdinalIgnoreCase))
                continue;
            
            AddTypedEdgeIfMissing(newNodeId, existingId, SupersedesEdgeType, similarity);
            var loweredImportance = Math.Clamp(GetNodeImportance(existingObj) * 0.7, 0.0, 1.0);
            existingObj.Set("importance", RuntimeValue.Float(loweredImportance));
            _nodeMetadata[existingId] = RuntimeValue.Object(existingObj);
            _knowledgeGraph.CallMethod("setNodeData", new List<RuntimeValue>
            {
                RuntimeValue.String(existingId),
                RuntimeValue.Object(existingObj)
            }, _interpreter);
            break;
        }
    }
    
    private void AddTypedEdgeIfMissing(string fromId, string toId, string edgeType, double weight)
    {
        if (_knowledgeGraph == null || _interpreter == null)
            return;
        if (!_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(fromId) }, _interpreter).AsBoolean()
            || !_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(toId) }, _interpreter).AsBoolean())
            return;
        if (_knowledgeGraph.CallMethod("hasEdge", new List<RuntimeValue>
            {
                RuntimeValue.String(fromId),
                RuntimeValue.String(toId)
            }, _interpreter).AsBoolean())
            return;
        
        var edgeProps = new DictionaryInstance();
        edgeProps.SetEntry("type", RuntimeValue.String(edgeType));
        _knowledgeGraph.CallMethod("addEdge", new List<RuntimeValue>
        {
            RuntimeValue.String(fromId),
            RuntimeValue.String(toId),
            RuntimeValue.Float(weight),
            RuntimeValue.Object(edgeProps)
        }, _interpreter);
    }
}
