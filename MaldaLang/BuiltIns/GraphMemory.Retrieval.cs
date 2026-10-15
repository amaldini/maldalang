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
    private List<RuntimeValue> QuerySemanticNodes(
        string query,
        int maxResults,
        string? phaseFilter,
        string? typeFilter,
        string? scopeFilter,
        HashSet<string>? scopeHierarchy,
        string? excludeTypeFilter,
        HashSet<string>? includeTypesFilter,
        HashSet<string>? tagsFilter = null,
        string tagsMode = "any",
        double minScore = 0,
        int maxDistance = 0,
        bool useSynapse = true,
        bool useActivation = true,
        double activationDecay = DefaultActivationDecay,
        bool useHybridLexical = false,
        bool useBm25 = false,
        double lexicalWeight = DefaultLexicalWeight,
        double lexicalMinScore = DefaultLexicalMinScore,
        bool lexicalMinScoreAuto = false,
        double diversity = DefaultDiversity,
        HashSet<string>? excludeNodeIds = null,
        bool explain = false,
        QueryDiagnosticsState? diagnostics = null)
    {
        // Step 1: Use VectorDB to find similar nodes
        RuntimeValue searchResults;
        var searchTopN = useSynapse ? Math.Max(maxResults * 4, 20) : maxResults;
        var vectorSearchFailed = false;
        
        // Try to use VectorDB's searchSimilar if calculator function is available
        try
        {
            searchResults = _nodeIndex!.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(query),
                RuntimeValue.Integer(searchTopN)
            }, _interpreter!);
        }
        catch (RuntimeException)
        {
            // If calculator function not available or searchSimilar fails for any reason,
            // return empty results array (query will return no results)
            // This can happen if VectorDB wasn't initialized with a calculator function
            // or if there's an issue with the embedding function
            searchResults = RuntimeValue.Array(new List<RuntimeValue>());
            vectorSearchFailed = true;
        }
        catch
        {
            // Catch any other exceptions and return empty results
            searchResults = RuntimeValue.Array(new List<RuntimeValue>());
            vectorSearchFailed = true;
        }
        
        if (searchResults.Type != ValueType.Array)
            return new List<RuntimeValue>();
        
        var results = searchResults.AsArray();
        var startingNodes = new List<string>();
        var retrievalScores = new Dictionary<string, double>(StringComparer.Ordinal);
        var vectorScores = new Dictionary<string, double>(StringComparer.Ordinal);
        
        // Extract node IDs from search results (optionally filtered by minScore)
        foreach (var result in results)
        {
            if (TryExtractSearchHit(result, out var nodeId, out var similarity))
            {
                if (excludeNodeIds != null && excludeNodeIds.Contains(nodeId))
                    continue;
                
                similarity *= GetVectorKindWeight(result);
                if (similarity >= minScore)
                {
                    startingNodes.Add(nodeId);
                    if (!vectorScores.TryGetValue(nodeId, out var existingVector) || similarity > existingVector)
                        vectorScores[nodeId] = similarity;
                    var blended = useHybridLexical
                        ? BlendRetrievalScore(nodeId, similarity, query, lexicalWeight, useSynapse, useBm25)
                        : GetNodeRetrievalScore(nodeId, similarity, useSynapse);
                    if (!retrievalScores.TryGetValue(nodeId, out var existing) || blended > existing)
                        retrievalScores[nodeId] = blended;
                }
                continue;
            }
            
            if (minScore > 0)
                continue;
            
            if (result.Type == ValueType.Object && result.AsObject() is JsonObject jsonObj)
            {
                var data = jsonObj.Get("data");
                if (data.Type == ValueType.Object && data.AsObject() is JsonObject dataObj)
                {
                    var nodeIdVal = dataObj.Get("nodeId");
                    if (nodeIdVal.Type == ValueType.String)
                    {
                        var fallbackNodeId = nodeIdVal.AsString();
                        if (excludeNodeIds == null || !excludeNodeIds.Contains(fallbackNodeId))
                            startingNodes.Add(fallbackNodeId);
                    }
                }
                else if (data.Type == ValueType.String)
                {
                    var desc = data.AsString();
                    foreach (var kvp in _nodeMetadata)
                    {
                        if (desc.Contains(kvp.Key) || kvp.Key.Contains(desc))
                        {
                            if (excludeNodeIds == null || !excludeNodeIds.Contains(kvp.Key))
                                startingNodes.Add(kvp.Key);
                            break;
                        }
                    }
                }
            }
        }

        if (diagnostics != null)
        {
            diagnostics.VectorCandidates = vectorScores.Count;
            diagnostics.EmbedReady = !vectorSearchFailed && vectorScores.Count > 0;
        }

        // lexicalMinScore: "auto" — when hybrid and vector channel is empty/weak, admit BM25 hits
        if (lexicalMinScoreAuto && useHybridLexical)
        {
            if (vectorScores.Count == 0 || vectorSearchFailed)
            {
                lexicalMinScore = 0.0;
                if (diagnostics != null)
                {
                    diagnostics.LexicalMinScoreApplied = 0.0;
                    diagnostics.LexicalMinScoreMode = "auto-weak-vector";
                }
            }
            else if (diagnostics != null)
            {
                diagnostics.LexicalMinScoreApplied = DefaultLexicalMinScore;
                diagnostics.LexicalMinScoreMode = "auto-default";
                lexicalMinScore = DefaultLexicalMinScore;
            }
        }
        else if (diagnostics != null)
        {
            diagnostics.LexicalMinScoreApplied = lexicalMinScore;
            diagnostics.LexicalMinScoreMode = "number";
        }
        
        if (useHybridLexical)
        {
            if (useBm25)
            {
                var bm25Scores = _bm25Index.ScoreQuery(query);
                if (diagnostics != null)
                    diagnostics.Bm25Candidates = bm25Scores.Count;
                foreach (var kvp in bm25Scores)
                {
                    if (!_nodeMetadata.TryGetValue(kvp.Key, out var nodeValue)
                        || !MatchesMemoryFilters(nodeValue, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, excludeTypeFilter, includeTypesFilter, tagsFilter, tagsMode, diagnostics))
                        continue;
                    if (excludeNodeIds != null && excludeNodeIds.Contains(kvp.Key))
                        continue;
                    var lexicalScore = NormalizeBm25Score(kvp.Value);
                    if (lexicalScore < lexicalMinScore)
                    {
                        if (diagnostics != null)
                        {
                            diagnostics.DroppedByLexicalMinScore++;
                            diagnostics.NoteDrop("lexical_min_score", kvp.Key);
                        }
                        continue;
                    }
                    var vectorScore = vectorScores.GetValueOrDefault(kvp.Key);
                    var blended = BlendRetrievalScore(kvp.Key, vectorScore, query, lexicalWeight, useSynapse, useBm25, lexicalScore);
                    if (!retrievalScores.TryGetValue(kvp.Key, out var existing) || blended > existing)
                        retrievalScores[kvp.Key] = blended;
                }
            }
            else
            {
                foreach (var kvp in _nodeMetadata)
                {
                    if (!MatchesMemoryFilters(kvp.Value, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, excludeTypeFilter, includeTypesFilter, tagsFilter, tagsMode, diagnostics))
                        continue;
                    if (excludeNodeIds != null && excludeNodeIds.Contains(kvp.Key))
                        continue;
                    
                    var lexicalScore = ComputeLexicalScore(query, GetStoredDescription(kvp.Key));
                    if (lexicalScore < lexicalMinScore)
                    {
                        if (diagnostics != null)
                        {
                            diagnostics.DroppedByLexicalMinScore++;
                            diagnostics.NoteDrop("lexical_min_score", kvp.Key);
                        }
                        continue;
                    }
                    
                    var vectorScore = vectorScores.GetValueOrDefault(kvp.Key);
                    var blended = BlendRetrievalScore(kvp.Key, vectorScore, query, lexicalWeight, useSynapse, false, lexicalScore);
                    if (!retrievalScores.TryGetValue(kvp.Key, out var existing) || blended > existing)
                        retrievalScores[kvp.Key] = blended;
                }
                if (diagnostics != null)
                    diagnostics.Bm25Candidates = retrievalScores.Count;
            }
        }
        
        startingNodes = retrievalScores.Keys
            .OrderByDescending(id => retrievalScores[id])
            .ToList();
        
        // Step 2: Include semantic hits and expand via graph BFS
        var allRelatedNodes = new HashSet<string>();
        foreach (var startNode in startingNodes.Take(searchTopN))
        {
            if (excludeNodeIds != null && excludeNodeIds.Contains(startNode))
                continue;
            allRelatedNodes.Add(startNode);
            if (!_knowledgeGraph!.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(startNode) }, _interpreter!).AsBoolean())
                continue;
            
            var seedScore = retrievalScores.TryGetValue(startNode, out var score) ? score : 0.0;
            if (!useActivation)
                continue;
            if (maxDistance > 0)
            {
                foreach (var reachableId in CollectReachableWithinDistance(startNode, maxDistance))
                {
                    if (excludeNodeIds != null && excludeNodeIds.Contains(reachableId))
                        continue;
                    allRelatedNodes.Add(reachableId);
                    TrySetRetrievalScore(retrievalScores, reachableId, seedScore * activationDecay, useSynapse);
                }
            }
            else
            {
                var bfsResult = _knowledgeGraph.CallMethod("bfs", new List<RuntimeValue> { RuntimeValue.String(startNode) }, _interpreter!);
                foreach (var reachableId in CollectReachableNodeIds(bfsResult))
                {
                    if (excludeNodeIds != null && excludeNodeIds.Contains(reachableId))
                        continue;
                    allRelatedNodes.Add(reachableId);
                    TrySetRetrievalScore(retrievalScores, reachableId, seedScore * activationDecay, useSynapse);
                }
            }
        }
        
        var rankedResults = new List<(string NodeId, RuntimeValue Value, double Score)>();
        foreach (var nodeId in allRelatedNodes)
        {
            if (excludeNodeIds != null && excludeNodeIds.Contains(nodeId))
                continue;
            if (_nodeMetadata.TryGetValue(nodeId, out var nodeValue)
                && MatchesMemoryFilters(nodeValue, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, excludeTypeFilter, includeTypesFilter, tagsFilter, tagsMode, diagnostics))
            {
                if (!retrievalScores.ContainsKey(nodeId))
                    retrievalScores[nodeId] = GetNodeRetrievalScore(nodeId, 0.0, useSynapse);
                
                var score = retrievalScores[nodeId];
                if (HasIncomingEdgeType(nodeId, SupersedesEdgeType))
                    score -= SupersededPenalty;
                rankedResults.Add((nodeId, nodeValue, score));
            }
        }

        if (diagnostics != null)
            diagnostics.AfterFilters = rankedResults.Count;
        
        var ordered = rankedResults
            .OrderByDescending(entry => entry.Score)
            .ToList();
        var selected = SelectWithMmr(ordered, maxResults, diversity);
        foreach (var selectedNode in selected)
            BumpNodeAccess(selectedNode.NodeId);
        if (!explain)
            return selected.Select(entry => entry.Value).ToList();
        return selected.Select(entry => AttachQueryExplain(
            entry.NodeId,
            entry.Value,
            entry.Score,
            vectorScores,
            query,
            useHybridLexical,
            useBm25,
            lexicalWeight,
            useSynapse,
            tagsFilter)).ToList();
    }

    private RuntimeValue AttachQueryExplain(
        string nodeId,
        RuntimeValue nodeValue,
        double finalScore,
        Dictionary<string, double> vectorScores,
        string query,
        bool useHybridLexical,
        bool useBm25,
        double lexicalWeight,
        bool useSynapse,
        HashSet<string>? tagsFilter = null)
    {
        if (nodeValue.Type != ValueType.Object || nodeValue.AsObject() is not JsonObject src)
            return nodeValue;
        var vector = vectorScores.GetValueOrDefault(nodeId);
        var lexical = useHybridLexical
            ? (useBm25 ? NormalizeBm25Score(_bm25Index.Score(query, nodeId)) : ComputeLexicalScore(query, GetStoredDescription(nodeId)))
            : 0.0;
        var combined = useHybridLexical ? CombineRetrievalScores(vector, lexical, lexicalWeight) : vector;
        var synapseScore = GetNodeRetrievalScore(nodeId, combined, useSynapse);
        var superseded = HasIncomingEdgeType(nodeId, SupersedesEdgeType) ? SupersededPenalty : 0.0;
        double importanceBonus = 0.0;
        List<string> nodeTags = new();
        if (_nodeMetadata.TryGetValue(nodeId, out var meta) && meta.Type == ValueType.Object && meta.AsObject() is JsonObject metaObj)
        {
            importanceBonus = ComputeImportance(metaObj) * 0.05;
            nodeTags = GetNodeTags(metaObj);
        }

        var copy = CloneJsonObject(src);
        copy.Set("nodeId", RuntimeValue.String(nodeId));
        copy.Set("score", RuntimeValue.Float(finalScore));
        var explain = new JsonObject();
        explain.Set("vectorScore", RuntimeValue.Float(vector));
        explain.Set("lexicalScore", RuntimeValue.Float(lexical));
        if (useBm25)
            explain.Set("bm25Score", RuntimeValue.Float(lexical));
        explain.Set("combinedScore", RuntimeValue.Float(combined));
        explain.Set("synapseScore", RuntimeValue.Float(synapseScore));
        explain.Set("importanceBonus", RuntimeValue.Float(importanceBonus));
        explain.Set("supersededPenalty", RuntimeValue.Float(superseded));
        explain.Set("finalScore", RuntimeValue.Float(finalScore));
        explain.Set("tags", RuntimeValue.Array(nodeTags.Select(RuntimeValue.String).ToList()));
        if (tagsFilter != null && tagsFilter.Count > 0)
        {
            var matched = nodeTags.Any(t => tagsFilter.Contains(t));
            explain.Set("tagsMatched", RuntimeValue.Boolean(matched));
        }
        copy.Set("explain", RuntimeValue.Object(explain));
        return RuntimeValue.Object(copy);
    }
    
    private static double CombineRetrievalScores(double vectorScore, double lexicalScore, double lexicalWeight)
    {
        var weight = Math.Clamp(lexicalWeight, 0.0, 1.0);
        return vectorScore * (1.0 - weight) + lexicalScore * weight;
    }
    
    private double BlendRetrievalScore(string nodeId, double vectorScore, string query, double lexicalWeight, bool useSynapse, bool useBm25, double? precomputedLexical = null)
    {
        var lexicalScore = precomputedLexical ?? (useBm25
            ? NormalizeBm25Score(_bm25Index.Score(query, nodeId))
            : ComputeLexicalScore(query, GetStoredDescription(nodeId)));
        var combined = CombineRetrievalScores(vectorScore, lexicalScore, lexicalWeight);
        return GetNodeRetrievalScore(nodeId, combined, useSynapse);
    }
    
    private static double ComputeLexicalScore(string query, string document)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(document))
            return 0.0;
        
        var queryTerms = TokenizeForLexical(query);
        if (queryTerms.Count == 0)
            return 0.0;
        
        var docTerms = TokenizeForLexical(document);
        if (docTerms.Count == 0)
            return 0.0;
        
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in docTerms)
            docFreq[term] = docFreq.GetValueOrDefault(term) + 1;
        
        double score = 0.0;
        var uniqueQueryTerms = queryTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var term in uniqueQueryTerms)
        {
            if (docFreq.TryGetValue(term, out var freq))
                score += freq / (double)docTerms.Count;
        }
        
        return Math.Min(1.0, score / uniqueQueryTerms.Count);
    }
    
    private static List<string> TokenizeForLexical(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '/' || ch == ':' || ch == '.')
                current.Append(ch);
            else if (current.Length > 0)
            {
                if (current.Length >= 2)
                    tokens.Add(current.ToString());
                current.Clear();
            }
        }
        
        if (current.Length >= 2)
            tokens.Add(current.ToString());
        
        return tokens;
    }
    
    private static double GetVectorKindWeight(RuntimeValue result)
    {
        if (result.Type != ValueType.Object
            || !TryGetObjectProperty(result.AsObject(), "data", out var data)
            || data.Type != ValueType.Object)
            return 1.0;
        if (TryGetObjectProperty(data.AsObject(), "vectorWeight", out var explicitWeight))
        {
            if (explicitWeight.Type == ValueType.Float)
                return explicitWeight.AsFloat();
            if (explicitWeight.Type == ValueType.Integer)
                return explicitWeight.AsInteger();
        }
        if (TryGetObjectProperty(data.AsObject(), "vectorKind", out var kind)
            && kind.Type == ValueType.String
            && string.Equals(kind.AsString(), "body", StringComparison.OrdinalIgnoreCase))
            return 0.9;
        return 1.0;
    }
    
    private List<(string NodeId, RuntimeValue Value, double Score)> SelectWithMmr(List<(string NodeId, RuntimeValue Value, double Score)> candidates, int maxResults, double diversity)
    {
        if (candidates.Count <= 1 || diversity <= 0)
            return candidates.Take(maxResults).ToList();
        
        var selected = new List<(string NodeId, RuntimeValue Value, double Score)>();
        var remaining = new List<(string NodeId, RuntimeValue Value, double Score)>(candidates);
        var lambda = Math.Clamp(1.0 - diversity, 0.0, 1.0);
        
        while (remaining.Count > 0 && selected.Count < maxResults)
        {
            var bestIdx = 0;
            var bestScore = double.MinValue;
            for (var i = 0; i < remaining.Count; i++)
            {
                var candidate = remaining[i];
                var redundancy = 0.0;
                for (var j = 0; j < selected.Count; j++)
                {
                    var sim = ComputeLexicalScore(GetStoredDescription(candidate.NodeId), GetStoredDescription(selected[j].NodeId));
                    if (sim > redundancy)
                        redundancy = sim;
                }
                var mmr = lambda * candidate.Score - (1.0 - lambda) * redundancy;
                if (mmr > bestScore)
                {
                    bestScore = mmr;
                    bestIdx = i;
                }
            }
            selected.Add(remaining[bestIdx]);
            remaining.RemoveAt(bestIdx);
        }
        
        return selected;
    }
    
    private void TrySetRetrievalScore(Dictionary<string, double> scores, string nodeId, double seedScore, bool useSynapse)
    {
        var score = Math.Max(seedScore, GetNodeRetrievalScore(nodeId, 0.0, useSynapse));
        if (!scores.TryGetValue(nodeId, out var existing) || score > existing)
            scores[nodeId] = score;
    }
    
    private double GetNodeRetrievalScore(string nodeId, double rawSimilarity, bool useSynapse)
    {
        if (!useSynapse)
            return rawSimilarity;
        
        if (!_nodeMetadata.TryGetValue(nodeId, out var nodeValue)
            || nodeValue.Type != ValueType.Object
            || nodeValue.AsObject() is not JsonObject nodeObj)
            return rawSimilarity;
        
        var score = ComputeSynapseScore(rawSimilarity, nodeObj);
        score += ComputeImportance(nodeObj) * 0.05;
        if (HasIncomingEdgeType(nodeId, SupersedesEdgeType))
            score -= SupersededPenalty;
        return score;
    }
    
    private static double GetNodeImportance(JsonObject nodeObj)
    {
        var importanceVal = nodeObj.Get("importance", null);
        if (importanceVal != null)
        {
            if (importanceVal.Type == ValueType.Float)
                return Math.Clamp(importanceVal.AsFloat(), 0.0, 1.0);
            if (importanceVal.Type == ValueType.Integer)
                return Math.Clamp(importanceVal.AsInteger(), 0.0, 1.0);
        }
        return 0.5;
    }
    
    private static int GetNodeAccessCount(JsonObject nodeObj)
    {
        var accessVal = nodeObj.Get("accessCount", null);
        if (accessVal != null && accessVal.Type == ValueType.Integer)
            return Math.Max(0, accessVal.AsInteger());
        return 0;
    }
    
    private static double ComputeImportance(JsonObject nodeObj)
    {
        var baseImportance = GetNodeImportance(nodeObj);
        var accessBoost = Math.Min(0.2, GetNodeAccessCount(nodeObj) * 0.01);
        var confidenceVal = nodeObj.Get("confidence", null);
        var confidenceBoost = 0.0;
        if (confidenceVal != null)
        {
            if (confidenceVal.Type == ValueType.Float)
                confidenceBoost = Math.Clamp(confidenceVal.AsFloat(), 0.0, 1.0) * 0.1;
            else if (confidenceVal.Type == ValueType.Integer)
                confidenceBoost = Math.Clamp(confidenceVal.AsInteger(), 0.0, 1.0) * 0.1;
        }
        return Math.Clamp(baseImportance + accessBoost + confidenceBoost, 0.0, 1.0);
    }
    
    private static double ComputeSynapseScore(double rawSimilarity, JsonObject nodeObj)
    {
        var score = rawSimilarity;
        var memType = GetMetadataString(nodeObj, "type");
        if (string.Equals(memType, "semantic", StringComparison.OrdinalIgnoreCase))
            score += SynapseSemanticBoost;
        else if (string.Equals(memType, "progress", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(memType, "decision", StringComparison.OrdinalIgnoreCase))
            score += SynapseProgressBoost;
        else if (string.Equals(memType, "episodic", StringComparison.OrdinalIgnoreCase))
            score -= SynapseEpisodicPenalty;
        
        var timestampVal = nodeObj.Get("timestamp", null);
        if (timestampVal != null && timestampVal.Type == ValueType.String
            && DateTime.TryParse(timestampVal.AsString(), out var timestamp))
        {
            var ageDays = (DateTime.UtcNow - timestamp.ToUniversalTime()).TotalDays;
            if (ageDays <= 1)
                score += 0.05;
            else if (ageDays <= 7)
                score += 0.03;
            else if (ageDays <= 30)
                score += 0.01;
        }
        
        return score;
    }

    private double ComputeCrossEncoderScore(string query, string nodeId, Dictionary<string, double>? vectorScores)
    {
        var doc = GetStoredDescription(nodeId);
        var bm25 = NormalizeBm25Score(_bm25Index.Score(query, nodeId));
        var lexical = ComputeLexicalScore(query, doc);
        var bigram = ComputeBigramOverlap(query, doc);
        var vector = vectorScores?.GetValueOrDefault(nodeId) ?? TryGetVectorSimilarity(query, nodeId);
        var synapseBonus = ComputeSynapseBonus(nodeId);
        return Math.Clamp(0.35 * bm25 + 0.25 * lexical + 0.15 * bigram + 0.2 * vector + 0.05 * synapseBonus, 0.0, 1.0);
    }

    private double TryGetVectorSimilarity(string query, string nodeId)
    {
        try
        {
            var searchResults = _nodeIndex!.CallMethod("searchSimilar", new List<RuntimeValue>
            {
                RuntimeValue.String(query),
                RuntimeValue.Integer(Math.Max(20, _nodeMetadata.Count))
            }, _interpreter!);
            if (searchResults.Type != ValueType.Array)
                return 0.0;
            foreach (var result in searchResults.AsArray())
            {
                if (TryExtractSearchHit(result, out var hitId, out var similarity)
                    && string.Equals(hitId, nodeId, StringComparison.Ordinal))
                    return similarity;
            }
        }
        catch
        {
        }
        return 0.0;
    }

    private double ComputeSynapseBonus(string nodeId)
    {
        if (!_nodeMetadata.TryGetValue(nodeId, out var meta) || meta.Type != ValueType.Object || meta.AsObject() is not JsonObject nodeObj)
            return 0.0;
        var type = GetMetadataString(nodeObj, "type");
        if (string.Equals(type, "semantic", StringComparison.OrdinalIgnoreCase))
            return SynapseSemanticBoost;
        if (string.Equals(type, "progress", StringComparison.OrdinalIgnoreCase))
            return SynapseProgressBoost;
        if (string.Equals(type, "episodic", StringComparison.OrdinalIgnoreCase))
            return -SynapseEpisodicPenalty;
        return 0.0;
    }

    private static double ComputeBigramOverlap(string query, string document)
    {
        var q = TokenizeForLexical(query);
        var d = TokenizeForLexical(document);
        if (q.Count < 2 || d.Count < 2)
            return 0.0;
        var docBigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < d.Count - 1; i++)
            docBigrams.Add(d[i] + " " + d[i + 1]);
        var matches = 0;
        for (var i = 0; i < q.Count - 1; i++)
        {
            if (docBigrams.Contains(q[i] + " " + q[i + 1]))
                matches++;
        }
        return Math.Min(1.0, matches / (double)Math.Max(1, q.Count - 1));
    }
}
