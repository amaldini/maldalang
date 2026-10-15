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
    private RuntimeValue CallStats(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("stats() expects no arguments");
        
        EnsureInitialized();
        
        var byType = new JsonObject();
        var byScope = new JsonObject();
        DateTime? oldest = null;
        DateTime? newest = null;
        
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            IncrementCount(byType, GetMetadataString(nodeObj, "type") ?? "unknown");
            IncrementCount(byScope, GetNodeScope(nodeObj));
            
            var timestampVal = nodeObj.Get("timestamp", null);
            if (timestampVal != null && timestampVal.Type == ValueType.String
                && DateTime.TryParse(timestampVal.AsString(), out var timestamp))
            {
                if (oldest == null || timestamp < oldest)
                    oldest = timestamp;
                if (newest == null || timestamp > newest)
                    newest = timestamp;
            }
        }
        
        var edgeCount = _knowledgeGraph!.CallMethod("edgeCount", new List<RuntimeValue>(), _interpreter!).AsInteger();
        var supersededCount = 0;
        var dualIndexPending = 0;
        DateTime? lastReflectAt = null;
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            if (HasIncomingEdgeType(kvp.Key, SupersedesEdgeType))
                supersededCount++;
            var migratedVal = nodeObj.Get("dualIndexMigrated", null);
            if (migratedVal == null || migratedVal.Type != ValueType.Boolean || !migratedVal.AsBoolean())
                dualIndexPending++;
            var reflectedAt = nodeObj.Get("reflectedAt", null);
            if (reflectedAt != null && reflectedAt.Type == ValueType.String
                && DateTime.TryParse(reflectedAt.AsString(), out var reflectedTs))
            {
                if (lastReflectAt == null || reflectedTs > lastReflectAt)
                    lastReflectAt = reflectedTs;
            }
        }
        
        var stats = new JsonObject();
        stats.Set("nodes", RuntimeValue.Integer(_nodeMetadata.Count));
        stats.Set("edges", RuntimeValue.Integer(edgeCount));
        stats.Set("byType", RuntimeValue.Object(byType));
        stats.Set("byScope", RuntimeValue.Object(byScope));
        stats.Set("oldest", RuntimeValue.String(oldest?.ToString("O") ?? ""));
        stats.Set("newest", RuntimeValue.String(newest?.ToString("O") ?? ""));
        stats.Set("supersededCount", RuntimeValue.Integer(supersededCount));
        stats.Set("dualIndexPending", RuntimeValue.Integer(dualIndexPending));
        stats.Set("lastReflectAt", RuntimeValue.String(lastReflectAt?.ToString("O") ?? ""));
        
        return RuntimeValue.Object(stats);
    }
    
    private static void IncrementCount(JsonObject counts, string key)
    {
        var existing = counts.Get(key, null);
        var next = existing != null && existing.Type == ValueType.Integer ? existing.AsInteger() + 1 : 1;
        counts.Set(key, RuntimeValue.Integer(next));
    }
    
    private static string? GetMetadataString(JsonObject nodeObj, string field)
    {
        var val = nodeObj.Get(field, null);
        if (val != null && val.Type == ValueType.String && !string.IsNullOrWhiteSpace(val.AsString()))
            return val.AsString();
        return null;
    }
    
    private RuntimeValue CallPrune(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new RuntimeException("prune() expects 1 object argument (options)");
        var options = RequireJsonObject(args[0], "prune() expects 1 object argument (options)");
        
        EnsureInitialized();
        
        if (!HasPruneFilter(options))
            throw new RuntimeException("prune() requires at least one filter: type, scope, phase, source, or olderThanDays");
        
        var typeFilter = GetStringOption(options, "type");
        var scopeFilter = GetStringOption(options, "scope");
        var phaseFilter = GetStringOption(options, "phase");
        var sourceFilter = GetStringOption(options, "source");
        var olderThanDays = GetIntOption(options, "olderThanDays", 0);
        var maxImportanceBelow = GetDoubleOption(options, "maxImportanceBelow", -1);
        var cutoffUtc = olderThanDays > 0 ? DateTime.UtcNow.AddDays(-olderThanDays) : (DateTime?)null;
        
        var toRemove = new List<string>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            if (typeFilter != null)
            {
                var typeVal = nodeObj.Get("type", null);
                if (typeVal == null || typeVal.Type != ValueType.String
                    || !string.Equals(typeVal.AsString(), typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            
            if (phaseFilter != null)
            {
                var phaseVal = nodeObj.Get("phase", null);
                if (phaseVal == null || phaseVal.Type != ValueType.String
                    || !string.Equals(phaseVal.AsString(), phaseFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            
            if (sourceFilter != null)
            {
                var sourceVal = nodeObj.Get("source", null);
                if (sourceVal == null || sourceVal.Type != ValueType.String
                    || !string.Equals(sourceVal.AsString(), sourceFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            
            if (scopeFilter != null)
            {
                var nodeScope = GetNodeScope(nodeObj);
                if (!string.Equals(nodeScope, scopeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            
            if (cutoffUtc != null)
            {
                var timestampVal = nodeObj.Get("timestamp", null);
                if (timestampVal == null || timestampVal.Type != ValueType.String
                    || !DateTime.TryParse(timestampVal.AsString(), out var timestamp)
                    || timestamp >= cutoffUtc)
                    continue;
            }
            
            var consolidatedFilter = GetOptionalBoolOption(options, "consolidated");
            if (consolidatedFilter != null)
            {
                var consolidatedVal = nodeObj.Get("consolidated", null);
                var isConsolidated = consolidatedVal != null && consolidatedVal.Type == ValueType.Boolean && consolidatedVal.AsBoolean();
                if (consolidatedFilter.Value != isConsolidated)
                    continue;
            }
            
            if (maxImportanceBelow >= 0)
            {
                var importance = GetNodeImportance(nodeObj);
                if (importance >= maxImportanceBelow)
                    continue;
            }
            
            toRemove.Add(kvp.Key);
        }
        
        var removed = 0;
        foreach (var nodeId in toRemove)
        {
            if (RemoveNodeById(nodeId))
                removed++;
        }
        
        return RuntimeValue.Integer(removed);
    }
    
    private RuntimeValue CallConsolidate(List<RuntimeValue> args)
    {
        JsonObject? options = null;
        if (args.Count >= 1)
            options = RequireJsonObject(args[0], "consolidate() expects 0-1 object argument (options?)");
        
        EnsureInitialized();
        
        var scopeFilter = GetStringOption(options, "scope");
        var maxEpisodic = Math.Max(1, GetIntOption(options, "maxEpisodic", 50));
        var minEpisodic = Math.Max(1, GetIntOption(options, "minEpisodic", 3));
        var episodics = CollectUnconsolidatedEpisodics(scopeFilter, maxEpisodic);
        
        var result = new JsonObject();
        if (episodics.Count < minEpisodic)
        {
            result.Set("semanticNodesCreated", RuntimeValue.Integer(0));
            result.Set("episodicsMarked", RuntimeValue.Integer(0));
            return RuntimeValue.Object(result);
        }
        
        var factBuilder = new System.Text.StringBuilder();
        var contextBuilder = new System.Text.StringBuilder();
        for (var i = 0; i < episodics.Count; i++)
        {
            var nodeObj = episodics[i].NodeObj;
            
            var factVal = nodeObj.Get("fact");
            var contextVal = nodeObj.Get("context", null);
            var index = i + 1;
            if (factVal.Type == ValueType.String)
                factBuilder.Append(index).Append(". ").AppendLine(factVal.AsString());
            if (contextVal != null && contextVal.Type == ValueType.String)
                contextBuilder.Append('[').Append(index).Append("] ").AppendLine(contextVal.AsString());
        }
        
        var scopeLabel = scopeFilter ?? "global";
        var summaryFact = $"Consolidated conversation ({episodics.Count} episodic turns, scope {scopeLabel})";
        var summaryContext = factBuilder.ToString().Trim();
        var responseContext = contextBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(responseContext))
            summaryContext = summaryContext + "\n---\n" + responseContext;
        if (summaryContext.Length > MaxConsolidatedContextChars)
            summaryContext = summaryContext[..MaxConsolidatedContextChars];
        
        var metadata = new JsonObject();
        metadata.Set("type", RuntimeValue.String("semantic"));
        metadata.Set("source", RuntimeValue.String("consolidate"));
        metadata.Set("importance", RuntimeValue.Float(0.6));
        if (scopeFilter != null)
            metadata.Set("scope", RuntimeValue.String(scopeFilter));
        
        var semanticId = $"node_{_nodeIdCounter++}";
        var nodeData = BuildNodeData(RuntimeValue.String(summaryFact), RuntimeValue.String(summaryContext), metadata);
        var description = new System.Text.StringBuilder(BuildNodeDescription(RuntimeValue.String(summaryFact), RuntimeValue.String(summaryContext)));
        AppendMetadataToDescription(description, metadata);
        StoreNewMemory(semanticId, RuntimeValue.String(summaryFact), nodeData, description.ToString());
        LinkSimilarMemories(semanticId, description.ToString());
        
        var marked = 0;
        foreach (var episodic in episodics)
        {
            AddDerivedFromEdge(semanticId, episodic.NodeId);
            if (MarkEpisodicConsolidated(episodic.NodeId))
                marked++;
        }
        
        result.Set("semanticNodesCreated", RuntimeValue.Integer(1));
        result.Set("episodicsMarked", RuntimeValue.Integer(marked));
        result.Set("semanticNodeId", RuntimeValue.String(semanticId));
        return RuntimeValue.Object(result);
    }

    private List<(string NodeId, DateTime Timestamp, JsonObject NodeObj)> CollectUnconsolidatedEpisodics(string? scopeFilter, int maxEpisodic)
    {
        var episodics = new List<(string NodeId, DateTime Timestamp, JsonObject NodeObj)>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            var typeVal = nodeObj.Get("type", null);
            if (typeVal == null || typeVal.Type != ValueType.String
                || !string.Equals(typeVal.AsString(), "episodic", StringComparison.OrdinalIgnoreCase))
                continue;
            
            var consolidatedVal = nodeObj.Get("consolidated", null);
            if (consolidatedVal != null && consolidatedVal.Type == ValueType.Boolean && consolidatedVal.AsBoolean())
                continue;
            
            if (scopeFilter != null && !string.Equals(GetNodeScope(nodeObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var timestampVal = nodeObj.Get("timestamp", null);
            if (timestampVal == null || timestampVal.Type != ValueType.String
                || !DateTime.TryParse(timestampVal.AsString(), out var timestamp))
                continue;
            
            episodics.Add((kvp.Key, timestamp, nodeObj));
        }
        
        return episodics
            .OrderByDescending(entry => entry.Timestamp)
            .Take(Math.Max(1, maxEpisodic))
            .ToList();
    }
    
    private void AddDerivedFromEdge(string semanticNodeId, string episodicNodeId)
    {
        if (_knowledgeGraph == null || _interpreter == null)
            return;
        
        if (!_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue>
            {
                RuntimeValue.String(semanticNodeId)
            }, _interpreter).AsBoolean()
            || !_knowledgeGraph.CallMethod("hasNode", new List<RuntimeValue>
            {
                RuntimeValue.String(episodicNodeId)
            }, _interpreter).AsBoolean())
            return;
        
        if (_knowledgeGraph.CallMethod("hasEdge", new List<RuntimeValue>
            {
                RuntimeValue.String(semanticNodeId),
                RuntimeValue.String(episodicNodeId)
            }, _interpreter).AsBoolean())
            return;
        
        var edgeProps = new DictionaryInstance();
        edgeProps.SetEntry("type", RuntimeValue.String(DerivedFromEdgeType));
        _knowledgeGraph.CallMethod("addEdge", new List<RuntimeValue>
        {
            RuntimeValue.String(semanticNodeId),
            RuntimeValue.String(episodicNodeId),
            RuntimeValue.Float(1.0),
            RuntimeValue.Object(edgeProps)
        }, _interpreter);
    }
    
    private bool MarkEpisodicConsolidated(string nodeId)
    {
        if (!_nodeMetadata.TryGetValue(nodeId, out var nodeValue)
            || nodeValue.Type != ValueType.Object
            || nodeValue.AsObject() is not JsonObject existingObj)
            return false;
        
        var fact = existingObj.Get("fact");
        var context = existingObj.Get("context", null);
        var metadata = CopyMetadataFields(existingObj);
        metadata.Set("consolidated", RuntimeValue.Boolean(true));
        
        var description = GetStoredDescription(nodeId);
        UpdateExistingMemory(nodeId, fact, context.Type != ValueType.Null ? context : null, metadata, description);
        return true;
    }
    
    private static JsonObject CloneJsonObject(JsonObject nodeObj)
    {
        var copy = new JsonObject();
        foreach (var kvp in nodeObj.GetProperties())
            copy.Set(kvp.Key, kvp.Value);
        return copy;
    }

    private static JsonObject CopyMetadataFields(JsonObject nodeObj)
    {
        var metadata = new JsonObject();
        foreach (var field in new[] { "phase", "type", "source", "scope", "filePath", "fileHash", "category" })
        {
            var val = nodeObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                metadata.Set(field, val);
        }

        var tags = NormalizeTagsValue(nodeObj.Get("tags", null));
        if (tags.Count > 0)
            metadata.Set("tags", RuntimeValue.Array(tags.Select(RuntimeValue.String).ToList()));
        
        var iterationVal = nodeObj.Get("iteration", null);
        if (iterationVal != null && iterationVal.Type != ValueType.Null)
            metadata.Set("iteration", iterationVal);
        
        var consolidatedVal = nodeObj.Get("consolidated", null);
        if (consolidatedVal != null && consolidatedVal.Type != ValueType.Null)
            metadata.Set("consolidated", consolidatedVal);
        
        foreach (var field in new[] { "confidence", "importance", "accessCount", "lastAccessed" })
        {
            var val = nodeObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                metadata.Set(field, val);
        }
        
        return metadata;
    }

    private static bool HasPruneFilter(JsonObject options)
    {
        if (GetStringOption(options, "type") != null
            || GetStringOption(options, "scope") != null
            || GetStringOption(options, "phase") != null
            || GetStringOption(options, "source") != null)
            return true;
        
        if (GetIntOption(options, "olderThanDays", 0) > 0)
            return true;
        
        return GetDoubleOption(options, "maxImportanceBelow", -1) >= 0;
    }

    private RuntimeValue CallEnforceLimits(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new RuntimeException("enforceLimits() expects 1 object argument (options)");
        var options = RequireJsonObject(args[0], "enforceLimits() expects 1 object argument (options)");
        
        EnsureInitialized();
        
        var maxNodes = GetIntOption(options, "maxNodes", 0);
        if (maxNodes <= 0)
            throw new RuntimeException("enforceLimits() requires maxNodes > 0");
        
        var typeFilter = GetStringOption(options, "type") ?? "episodic";
        var scopeFilter = GetStringOption(options, "scope");
        
        var candidates = new List<(string NodeId, DateTime Timestamp)>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            var typeVal = nodeObj.Get("type", null);
            if (typeVal == null || typeVal.Type != ValueType.String
                || !string.Equals(typeVal.AsString(), typeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (scopeFilter != null && !string.Equals(GetNodeScope(nodeObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var timestampVal = nodeObj.Get("timestamp", null);
            if (timestampVal == null || timestampVal.Type != ValueType.String
                || !DateTime.TryParse(timestampVal.AsString(), out var timestamp))
                continue;
            
            candidates.Add((kvp.Key, timestamp));
        }
        
        var removed = 0;
        if (_nodeMetadata.Count > maxNodes)
        {
            var excess = _nodeMetadata.Count - maxNodes;
            foreach (var entry in candidates.OrderBy(c => c.Timestamp).Take(excess))
            {
                if (RemoveNodeById(entry.NodeId))
                    removed++;
            }
        }
        
        return RuntimeValue.Integer(removed);
    }

    private RuntimeValue CallForgetByScope(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("forgetByScope() expects scope string and optional options object");
        var scope = args[0].AsString();
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            options = RequireJsonObject(args[1], "forgetByScope() optional second argument must be options object");
        }
        var pruneOpts = new JsonObject();
        pruneOpts.Set("scope", RuntimeValue.String(scope));
        var typeFilter = GetStringOption(options, "type");
        if (!string.IsNullOrWhiteSpace(typeFilter))
            pruneOpts.Set("type", RuntimeValue.String(typeFilter!));
        return CallPrune(new List<RuntimeValue> { RuntimeValue.Object(pruneOpts) });
    }

    private RuntimeValue CallForgetByCategory(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("forgetByCategory() expects category string and optional options object");
        var category = args[0].AsString();
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            options = RequireJsonObject(args[1], "forgetByCategory() optional second argument must be options object");
        }
        EnsureInitialized();
        var scopeFilter = GetStringOption(options, "scope");
        var toRemove = new List<string>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var categoryVal = nodeObj.Get("category", null);
            if (categoryVal == null || categoryVal.Type != ValueType.String
                || !string.Equals(categoryVal.AsString(), category, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(scopeFilter)
                && !string.Equals(GetNodeScope(nodeObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            toRemove.Add(kvp.Key);
        }
        var removed = 0;
        foreach (var nodeId in toRemove)
        {
            if (RemoveNodeById(nodeId))
                removed++;
        }
        return RuntimeValue.Integer(removed);
    }

    private RuntimeValue CallForgetByTag(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("forgetByTag() expects tag string and optional options object");
        var tag = args[0].AsString().Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tag))
            throw new RuntimeException("forgetByTag() expects a non-empty tag string");
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            options = RequireJsonObject(args[1], "forgetByTag() optional second argument must be options object");
        }
        EnsureInitialized();
        var scopeFilter = GetStringOption(options, "scope");
        var toRemove = new List<string>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var nodeTags = GetNodeTags(nodeObj);
            if (!nodeTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(scopeFilter)
                && !string.Equals(GetNodeScope(nodeObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            toRemove.Add(kvp.Key);
        }
        var removed = 0;
        foreach (var nodeId in toRemove)
        {
            if (RemoveNodeById(nodeId))
                removed++;
        }
        return RuntimeValue.Integer(removed);
    }
}
