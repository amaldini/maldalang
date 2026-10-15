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
    private RuntimeValue CallReflect(List<RuntimeValue> args)
    {
        JsonObject? options = null;
        if (args.Count >= 1)
            options = RequireJsonObject(args[0], "reflect() expects 0-1 object argument (options?)");
        
        EnsureInitialized();
        
        var scopeFilter = GetStringOption(options, "scope");
        var maxEpisodic = Math.Max(1, GetIntOption(options, "maxEpisodic", 50));
        var minEpisodic = Math.Max(1, GetIntOption(options, "minEpisodic", 3));
        var dryRun = GetBoolOption(options, "dryRun", false);
        var episodics = CollectUnconsolidatedEpisodics(scopeFilter, maxEpisodic);
        
        var result = new JsonObject();
        if (episodics.Count < minEpisodic)
        {
            result.Set("factsCreated", RuntimeValue.Integer(0));
            result.Set("episodicsMarked", RuntimeValue.Integer(0));
            result.Set("facts", RuntimeValue.Array(new List<RuntimeValue>()));
            return RuntimeValue.Object(result);
        }
        
        try
        {
            var reflectedFacts = MemoryReflectService.Reflect(episodics, options, _interpreter);
            var createdFactResults = new List<RuntimeValue>();
            var createdNodeIds = new List<RuntimeValue>();
            var createdCount = 0;
            var minConfidence = GetDoubleOption(options, "minConfidence", ReflectMinConfidence);
            var resolveConflicts = GetBoolOption(options, "resolveConflicts", true);
            
            foreach (var reflected in reflectedFacts)
            {
                var factObj = new JsonObject();
                factObj.Set("fact", RuntimeValue.String(reflected.Fact));
                factObj.Set("confidence", RuntimeValue.Float(reflected.Confidence));
                if (!string.IsNullOrWhiteSpace(reflected.Category))
                    factObj.Set("category", RuntimeValue.String(reflected.Category!));
                createdFactResults.Add(RuntimeValue.Object(factObj));
                
                if (dryRun || reflected.Confidence < minConfidence)
                    continue;
                if (resolveConflicts && ShouldSkipReflectedFact(reflected, scopeFilter))
                    continue;
                
                var metadata = new JsonObject();
                metadata.Set("type", RuntimeValue.String("semantic"));
                metadata.Set("source", RuntimeValue.String("reflect"));
                metadata.Set("confidence", RuntimeValue.Float(reflected.Confidence));
                metadata.Set("importance", RuntimeValue.Float(Math.Clamp(reflected.Confidence, 0.0, 1.0)));
                metadata.Set("reflectedAt", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
                if (!string.IsNullOrWhiteSpace(reflected.Category))
                    metadata.Set("category", RuntimeValue.String(reflected.Category!));
                if (!string.IsNullOrWhiteSpace(scopeFilter))
                    metadata.Set("scope", RuntimeValue.String(scopeFilter!));
                
                var semanticId = $"node_{_nodeIdCounter++}";
                var nodeData = BuildNodeData(RuntimeValue.String(reflected.Fact), RuntimeValue.Null(), metadata);
                var description = new System.Text.StringBuilder(BuildNodeDescription(RuntimeValue.String(reflected.Fact), null));
                AppendMetadataToDescription(description, metadata);
                StoreNewMemory(semanticId, RuntimeValue.String(reflected.Fact), nodeData, description.ToString());
                LinkSimilarMemories(semanticId, description.ToString());
                TryDetectSupersedes(semanticId, description.ToString(), metadata);
                if (resolveConflicts)
                    LinkReflectSupersedesWhenHigherConfidence(semanticId, reflected.Confidence, scopeFilter);
                
                foreach (var episodic in episodics)
                    AddDerivedFromEdge(semanticId, episodic.NodeId);
                
                createdNodeIds.Add(RuntimeValue.String(semanticId));
                createdCount++;
            }
            
            var marked = 0;
            if (!dryRun)
            {
                foreach (var episodic in episodics)
                {
                    if (MarkEpisodicConsolidated(episodic.NodeId))
                        marked++;
                }
            }
            
            result.Set("factsCreated", RuntimeValue.Integer(createdCount));
            result.Set("episodicsMarked", RuntimeValue.Integer(marked));
            result.Set("facts", RuntimeValue.Array(createdFactResults));
            if (createdNodeIds.Count > 0)
                result.Set("semanticNodeIds", RuntimeValue.Array(createdNodeIds));
            return RuntimeValue.Object(result);
        }
        catch (Exception ex)
        {
            var consolidateArgs = new List<RuntimeValue>();
            if (options != null)
                consolidateArgs.Add(RuntimeValue.Object(options));
            var fallback = CallConsolidate(consolidateArgs);
            var fallbackObj = fallback.Type == ValueType.Object && fallback.AsObject() is JsonObject fo ? fo : new JsonObject();
            var mapped = new JsonObject();
            var fallbackCreated = fallbackObj.Get("semanticNodesCreated", null);
            var fallbackMarked = fallbackObj.Get("episodicsMarked", null);
            mapped.Set("factsCreated", fallbackCreated != null && fallbackCreated.Type != ValueType.Null ? fallbackCreated : RuntimeValue.Integer(0));
            mapped.Set("episodicsMarked", fallbackMarked != null && fallbackMarked.Type != ValueType.Null ? fallbackMarked : RuntimeValue.Integer(0));
            mapped.Set("facts", RuntimeValue.Array(new List<RuntimeValue>()));
            var semanticNodeId = fallbackObj.Get("semanticNodeId", null);
            if (semanticNodeId != null && semanticNodeId.Type == ValueType.String)
                mapped.Set("semanticNodeIds", RuntimeValue.Array(new List<RuntimeValue> { semanticNodeId }));
            mapped.Set("errors", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String($"reflect fallback: {ex.Message}") }));
            return RuntimeValue.Object(mapped);
        }
    }

    private RuntimeValue CallReflectAsync(List<RuntimeValue> args)
    {
        if (_reflectAsyncRunning)
        {
            var pending = new JsonObject();
            pending.Set("scheduled", RuntimeValue.Boolean(false));
            pending.Set("pending", RuntimeValue.Boolean(true));
            return RuntimeValue.Object(pending);
        }

        JsonObject? options = null;
        if (args.Count >= 1)
            options = RequireJsonObject(args[0], "reflectAsync() expects 0-1 object argument (options?)");

        var savePath = GetStringOption(options, "savePath");
        var reflectArgs = new List<RuntimeValue>();
        if (options != null)
            reflectArgs.Add(RuntimeValue.Object(options));

        _reflectAsyncRunning = true;
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    CallReflect(reflectArgs);
                    if (!string.IsNullOrWhiteSpace(savePath))
                    {
                        var basePath = ResolveMemoryBasePath(savePath!);
                        MaybeRotateBackups(basePath, ResolveBackupOptions(null));
                        WriteMemoryArtifacts(basePath);
                    }
                }
            }
            catch
            {
                // Background reflect failures are non-fatal; caller can retry on next save.
            }
            finally
            {
                _reflectAsyncRunning = false;
            }
        });

        var scheduled = new JsonObject();
        scheduled.Set("scheduled", RuntimeValue.Boolean(true));
        scheduled.Set("pending", RuntimeValue.Boolean(false));
        return RuntimeValue.Object(scheduled);
    }

    private RuntimeValue CallValidate(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("validate() expects no arguments");

        EnsureInitialized();

        var graphNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var graphNodes = _knowledgeGraph!.CallMethod("nodes", new List<RuntimeValue>(), _interpreter!);
        if (graphNodes.Type == ValueType.Array)
        {
            foreach (var node in graphNodes.AsArray())
            {
                if (node.Type == ValueType.String)
                    graphNodeIds.Add(node.AsString());
            }
        }

        var metadataNodeIds = new HashSet<string>(_nodeMetadata.Keys, StringComparer.Ordinal);
        var vectorNodeIds = _nodeIndex!.CollectIndexedNodeIds();

        var metadataWithoutGraph = metadataNodeIds.Where(id => !graphNodeIds.Contains(id)).ToList();
        var graphWithoutMetadata = graphNodeIds.Where(id => !metadataNodeIds.Contains(id)).ToList();
        var vectorsWithoutNode = vectorNodeIds.Where(id => !metadataNodeIds.Contains(id)).ToList();
        var nodesWithoutVectors = metadataNodeIds.Where(id => !vectorNodeIds.Contains(id)).ToList();

        var danglingEdges = new List<RuntimeValue>();
        var edges = _knowledgeGraph.CallMethod("edges", new List<RuntimeValue>(), _interpreter!);
        if (edges.Type == ValueType.Array)
        {
            foreach (var edge in edges.AsArray())
            {
                string? fromId = null;
                string? toId = null;
                if (edge.Type == ValueType.Array)
                {
                    var edgeArr = edge.AsArray();
                    if (edgeArr.Count >= 2 && edgeArr[0].Type == ValueType.String && edgeArr[1].Type == ValueType.String)
                    {
                        fromId = edgeArr[0].AsString();
                        toId = edgeArr[1].AsString();
                    }
                }
                else if (edge.Type == ValueType.Object && edge.AsObject() is JsonObject edgeJson)
                {
                    var fromVal = edgeJson.Get("from", null);
                    var toVal = edgeJson.Get("to", null);
                    if (fromVal != null && fromVal.Type == ValueType.String)
                        fromId = fromVal.AsString();
                    if (toVal != null && toVal.Type == ValueType.String)
                        toId = toVal.AsString();
                }

                if (fromId == null || toId == null)
                    continue;

                if (!graphNodeIds.Contains(fromId) || !graphNodeIds.Contains(toId))
                {
                    var issue = new JsonObject();
                    issue.Set("from", RuntimeValue.String(fromId));
                    issue.Set("to", RuntimeValue.String(toId));
                    danglingEdges.Add(RuntimeValue.Object(issue));
                }
            }
        }

        var issues = new List<RuntimeValue>();
        foreach (var id in metadataWithoutGraph)
        {
            var issue = new JsonObject();
            issue.Set("type", RuntimeValue.String("metadata_without_graph"));
            issue.Set("nodeId", RuntimeValue.String(id));
            issues.Add(RuntimeValue.Object(issue));
        }
        foreach (var id in graphWithoutMetadata)
        {
            var issue = new JsonObject();
            issue.Set("type", RuntimeValue.String("graph_without_metadata"));
            issue.Set("nodeId", RuntimeValue.String(id));
            issues.Add(RuntimeValue.Object(issue));
        }
        foreach (var id in vectorsWithoutNode)
        {
            var issue = new JsonObject();
            issue.Set("type", RuntimeValue.String("vector_without_node"));
            issue.Set("nodeId", RuntimeValue.String(id));
            issues.Add(RuntimeValue.Object(issue));
        }
        foreach (var id in nodesWithoutVectors)
        {
            var issue = new JsonObject();
            issue.Set("type", RuntimeValue.String("node_without_vector"));
            issue.Set("nodeId", RuntimeValue.String(id));
            issues.Add(RuntimeValue.Object(issue));
        }
        issues.AddRange(danglingEdges.Select(edge =>
        {
            if (edge.Type != ValueType.Object || edge.AsObject() is not JsonObject edgeObj)
                return edge;
            var issue = new JsonObject();
            issue.Set("type", RuntimeValue.String("dangling_edge"));
            issue.Set("from", edgeObj.Get("from", null) ?? RuntimeValue.Null());
            issue.Set("to", edgeObj.Get("to", null) ?? RuntimeValue.Null());
            return RuntimeValue.Object(issue);
        }));

        var counts = new JsonObject();
        counts.Set("metadataNodes", RuntimeValue.Integer(metadataNodeIds.Count));
        counts.Set("graphNodes", RuntimeValue.Integer(graphNodeIds.Count));
        counts.Set("vectorEntries", RuntimeValue.Integer(_nodeIndex.EntryCount));
        counts.Set("vectorNodes", RuntimeValue.Integer(vectorNodeIds.Count));
        counts.Set("metadataWithoutGraph", RuntimeValue.Integer(metadataWithoutGraph.Count));
        counts.Set("graphWithoutMetadata", RuntimeValue.Integer(graphWithoutMetadata.Count));
        counts.Set("vectorsWithoutNode", RuntimeValue.Integer(vectorsWithoutNode.Count));
        counts.Set("nodesWithoutVectors", RuntimeValue.Integer(nodesWithoutVectors.Count));
        counts.Set("danglingEdges", RuntimeValue.Integer(danglingEdges.Count));

        var ok = issues.Count == 0;
        var report = new JsonObject();
        report.Set("ok", RuntimeValue.Boolean(ok));
        report.Set("issues", RuntimeValue.Array(issues));
        report.Set("counts", RuntimeValue.Object(counts));
        return RuntimeValue.Object(report);
    }

    private bool ShouldSkipReflectedFact(MemoryReflectService.ReflectedFact reflected, string? scopeFilter)
    {
        RuntimeValue searchResults;
        try
        {
            searchResults = _nodeIndex!.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(reflected.Fact),
                RuntimeValue.Integer(8)
            }, _interpreter!);
        }
        catch
        {
            return false;
        }
        if (searchResults.Type != ValueType.Array)
            return false;
        foreach (var hit in searchResults.AsArray())
        {
            if (!TryExtractSearchHit(hit, out var existingId, out var similarity))
                continue;
            if (similarity < ReflectConflictSimilarityThreshold)
                continue;
            if (!_nodeMetadata.TryGetValue(existingId, out var existingValue)
                || existingValue.Type != ValueType.Object
                || existingValue.AsObject() is not JsonObject existingObj)
                continue;
            var existingType = existingObj.Get("type", null);
            if (existingType == null || existingType.Type != ValueType.String
                || !string.Equals(existingType.AsString(), "semantic", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(scopeFilter)
                && !string.Equals(GetNodeScope(existingObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var existingConfidence = 0.0;
            var confidenceVal = existingObj.Get("confidence", null);
            if (confidenceVal != null)
            {
                if (confidenceVal.Type == ValueType.Float)
                    existingConfidence = confidenceVal.AsFloat();
                else if (confidenceVal.Type == ValueType.Integer)
                    existingConfidence = confidenceVal.AsInteger();
            }
            if (reflected.Confidence < existingConfidence)
                return true;
        }
        return false;
    }

    private void LinkReflectSupersedesWhenHigherConfidence(string newNodeId, double newConfidence, string? scopeFilter)
    {
        var description = GetStoredDescription(newNodeId);
        if (string.IsNullOrWhiteSpace(description))
            return;
        RuntimeValue searchResults;
        try
        {
            searchResults = _nodeIndex!.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(description),
                RuntimeValue.Integer(8)
            }, _interpreter!);
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
            if (existingId == newNodeId || similarity < ReflectConflictSimilarityThreshold)
                continue;
            if (!_nodeMetadata.TryGetValue(existingId, out var existingValue)
                || existingValue.Type != ValueType.Object
                || existingValue.AsObject() is not JsonObject existingObj)
                continue;
            var existingType = existingObj.Get("type", null);
            if (existingType == null || existingType.Type != ValueType.String
                || !string.Equals(existingType.AsString(), "semantic", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(scopeFilter)
                && !string.Equals(GetNodeScope(existingObj), scopeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var existingConfidence = 0.0;
            var confidenceVal = existingObj.Get("confidence", null);
            if (confidenceVal != null)
            {
                if (confidenceVal.Type == ValueType.Float)
                    existingConfidence = confidenceVal.AsFloat();
                else if (confidenceVal.Type == ValueType.Integer)
                    existingConfidence = confidenceVal.AsInteger();
            }
            if (newConfidence < existingConfidence)
                continue;
            AddTypedEdgeIfMissing(newNodeId, existingId, SupersedesEdgeType, similarity);
            var loweredImportance = Math.Clamp(GetNodeImportance(existingObj) * 0.65, 0.0, 1.0);
            existingObj.Set("importance", RuntimeValue.Float(loweredImportance));
            _nodeMetadata[existingId] = RuntimeValue.Object(existingObj);
            _knowledgeGraph!.CallMethod("setNodeData", new List<RuntimeValue>
            {
                RuntimeValue.String(existingId),
                RuntimeValue.Object(existingObj)
            }, _interpreter!);
            break;
        }
    }
}
