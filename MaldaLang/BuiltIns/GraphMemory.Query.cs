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
    private RuntimeValue CallQuery(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("query() expects 1 string argument (query, maxResults?, options?)");
        
        EnsureInitialized();
        
        var query = args[0].AsString();
        var options = ParseQueryOptions(args, out var maxResults);
        var recentCount = GetIntOption(options, "recentCount", 0);
        var hybrid = GetBoolOption(options, "hybrid", recentCount > 0);
        var phaseFilter = GetStringOption(options, "phase");
        var typeFilter = GetStringOption(options, "type");
        var scopeFilter = GetStringOption(options, "scope");
        var excludeTypeFilter = GetStringOption(options, "excludeType");
        var includeTypesFilter = GetStringListOption(options, "includeTypes");
        var tagsFilter = GetTagsFilterOption(options);
        var tagsMode = GetStringOption(options, "tagsMode") ?? "any";
        if (!string.Equals(tagsMode, "all", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tagsMode, "any", StringComparison.OrdinalIgnoreCase))
            tagsMode = "any";
        var minScore = GetDoubleOption(options, "minScore", 0);
        var maxDistance = GetIntOption(options, "maxDistance", 0);
        var useSynapse = GetBoolOption(options, "synapse", true);
        var useActivation = GetBoolOption(options, "activation", useSynapse);
        var activationDecay = GetDoubleOption(options, "activationDecay", DefaultActivationDecay);
        var diversity = GetDoubleOption(options, "diversity", DefaultDiversity);
        var useHybridLexical = GetBoolOption(options, "hybridLexical", false);
        var useBm25 = useHybridLexical && UseBm25Lexical(options);
        var lexicalWeight = GetDoubleOption(options, "lexicalWeight", DefaultLexicalWeight);
        double lexicalMinScore;
        bool lexicalMinScoreAuto;
        if (!TryResolveLexicalMinScoreOption(options, out lexicalMinScore, out lexicalMinScoreAuto))
        {
            lexicalMinScore = DefaultLexicalMinScore;
            lexicalMinScoreAuto = false;
        }
        var excludeNodeIds = GetStringListOption(options, "excludeNodeIds");
        var scopeHierarchy = ResolveScopeHierarchy(scopeFilter, options);
        var diagnosticsDetailed = GetBoolOption(options, "diagnostics", false);
        var diagnostics = new QueryDiagnosticsState
        {
            Query = query,
            MaxResults = maxResults,
            HybridLexical = useHybridLexical,
            LexicalMode = !useHybridLexical ? "none" : (useBm25 ? "bm25" : "overlap"),
            LexicalMinScoreAuto = lexicalMinScoreAuto,
            LexicalMinScoreApplied = lexicalMinScore,
            LexicalMinScoreMode = lexicalMinScoreAuto ? "auto-default" : "number",
            Detailed = diagnosticsDetailed
        };
        
        var rerank = GetBoolOption(options, "rerank", false);
        var rerankMode = GetStringOption(options, "rerankMode") ?? "llm";
        var rerankTopK = Math.Max(maxResults, GetIntOption(options, "rerankTopK", 20));
        var useExplain = GetBoolOption(options, "explain", false);
        var semanticLimit = rerank ? rerankTopK : maxResults;
        var semanticResults = QuerySemanticNodes(
            query, semanticLimit, phaseFilter, typeFilter, scopeFilter, scopeHierarchy,
            excludeTypeFilter, includeTypesFilter, tagsFilter, tagsMode,
            minScore, maxDistance, useSynapse, useActivation, activationDecay,
            useHybridLexical, useBm25, lexicalWeight, lexicalMinScore, lexicalMinScoreAuto,
            diversity, excludeNodeIds, useExplain, diagnostics);
        if (rerank)
            semanticResults = ApplyRerank(query, semanticResults, options, maxResults, rerankMode);
        if (!hybrid || recentCount <= 0)
        {
            diagnostics.Returned = semanticResults.Count;
            _lastQueryDiagnostics = BuildLastQueryDiagnostics(diagnostics);
            return FinishQueryResult(semanticResults, options);
        }
        
        var recentResults = CollectRecentEntries(recentCount, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, tagsFilter, tagsMode);
        var merged = MergeMemoryResults(semanticResults, recentResults);
        diagnostics.Returned = merged.Count;
        _lastQueryDiagnostics = BuildLastQueryDiagnostics(diagnostics);
        return FinishQueryResult(merged, options);
    }

    private RuntimeValue CallAsk(List<RuntimeValue> args)
    {
        var hits = CallQuery(args);
        return GroundedStdLib.EnsureMemoryHitsWrapped(hits);
    }

    private static RuntimeValue FinishQueryResult(List<RuntimeValue> hits, JsonObject? options)
    {
        var array = RuntimeValue.Array(hits);
        return GetBoolOption(options, "grounded", false)
            ? GroundedStdLib.WrapMemoryHits(array)
            : array;
    }

    private List<RuntimeValue> ApplyRerank(string query, List<RuntimeValue> candidates, JsonObject? options, int maxResults, string rerankMode)
    {
        if (candidates.Count <= 1)
            return candidates.Take(maxResults).ToList();

        if (string.Equals(rerankMode, "cross", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rerankMode, "onnx", StringComparison.OrdinalIgnoreCase))
        {
            var crossRanked = RankWithCrossEncoder(query, candidates, options, rerankMode);
            return crossRanked.Take(maxResults).ToList();
        }

        var injectedScores = GetRerankScoresOption(options);
        Dictionary<string, double>? rerankScores = injectedScores;
        if (rerankScores == null)
        {
            try
            {
                var prompt = BuildRerankPrompt(query, candidates);
                RuntimeValue response;
                var clientValue = options?.Get("rerankClient", null);
                if (clientValue != null && clientValue.Type == ValueType.Object)
                {
                    response = MemoryReflectService.CallClientComplete(clientValue.AsObject()!, prompt, _interpreter);
                }
                else
                {
                    var model = GetStringOption(options, "rerankModel");
                    var client = new OpenRouterClientInstance(model);
                    response = client.CallMethod("complete", new List<RuntimeValue> { RuntimeValue.String(prompt) }, _interpreter);
                }
                rerankScores = ParseRerankScores(ExtractCompletionText(response));
            }
            catch
            {
                return candidates.Take(maxResults).ToList();
            }
        }
        if (rerankScores == null || rerankScores.Count == 0)
            return candidates.Take(maxResults).ToList();
        var ranked = candidates
            .Select((value, index) => (Value: value, Index: index, NodeId: GetNodeId(value)))
            .OrderByDescending(entry => entry.NodeId != null && rerankScores.TryGetValue(entry.NodeId, out var score) ? score : double.NegativeInfinity)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Value)
            .ToList();
        return ranked.Take(maxResults).ToList();
    }

    private List<RuntimeValue> RankWithCrossEncoder(
        string query,
        List<RuntimeValue> candidates,
        JsonObject? options,
        string rerankMode)
    {
        var onnxEncoder = string.Equals(rerankMode, "onnx", StringComparison.OrdinalIgnoreCase)
            ? GetOrCreateOnnxCrossEncoder(ResolveRerankModelPath(options))
            : null;

        return candidates
            .Select((value, index) => (Value: value, Index: index, NodeId: GetNodeId(value)))
            .OrderByDescending(entry =>
            {
                if (entry.NodeId == null)
                    return double.NegativeInfinity;
                if (onnxEncoder != null)
                {
                    var doc = GetStoredDescription(entry.NodeId);
                    try
                    {
                        return onnxEncoder.Score(query, doc);
                    }
                    catch
                    {
                        return ComputeCrossEncoderScore(query, entry.NodeId, null);
                    }
                }
                return ComputeCrossEncoderScore(query, entry.NodeId, null);
            })
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Value)
            .ToList();
    }

    private MemoryOnnxCrossEncoder? GetOrCreateOnnxCrossEncoder(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return null;
        lock (_lock)
        {
            if (_onnxCrossEncoder != null && string.Equals(_onnxCrossEncoderPath, modelPath, StringComparison.OrdinalIgnoreCase))
                return _onnxCrossEncoder;
            _onnxCrossEncoder?.Dispose();
            _onnxCrossEncoder = MemoryOnnxCrossEncoder.TryCreate(modelPath);
            _onnxCrossEncoderPath = _onnxCrossEncoder != null ? modelPath : null;
            return _onnxCrossEncoder;
        }
    }

    private static string? ResolveRerankModelPath(JsonObject? options)
    {
        var fromOptions = GetStringOption(options, "rerankModelPath");
        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_RERANK_MODEL_PATH");
        var configured = !string.IsNullOrWhiteSpace(fromOptions) ? fromOptions : env;
        return CrossEncoderOnnxModels.ResolveRerankModelPath(configured);
    }

    private static string BuildRerankPrompt(string query, List<RuntimeValue> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank candidate memory nodes for this query.");
        sb.AppendLine("Return JSON only: {\"scores\":[{\"nodeId\":\"...\",\"score\":0.0}]}, score in [0,1].");
        sb.AppendLine("Query:");
        sb.AppendLine(query);
        sb.AppendLine("Candidates:");
        foreach (var candidate in candidates)
        {
            var nodeId = GetNodeId(candidate);
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;
            var line = FormatMemoryLine(candidate);
            sb.Append("- ").Append(nodeId).Append(": ").AppendLine(line);
        }
        return sb.ToString();
    }

    private static string? GetNodeId(RuntimeValue value)
    {
        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject obj)
            return null;
        var nodeId = obj.Get("nodeId", null);
        if (nodeId == null || nodeId.Type != ValueType.String || string.IsNullOrWhiteSpace(nodeId.AsString()))
            return null;
        return nodeId.AsString();
    }

    private static string ExtractCompletionText(RuntimeValue response)
    {
        if (response.Type == ValueType.String)
            return response.AsString();
        if (response.Type == ValueType.Object && response.AsObject() is JsonObject obj)
        {
            var content = obj.Get("content", null);
            if (content != null && content.Type == ValueType.String)
                return content.AsString();
        }
        throw new RuntimeException("rerank response did not contain text");
    }

    private static Dictionary<string, double>? ParseRerankScores(string responseText)
    {
        var text = responseText.Trim();
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("scores", out var scores) || scores.ValueKind != JsonValueKind.Array)
            return null;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in scores.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("nodeId", out var nodeIdProp) || nodeIdProp.ValueKind != JsonValueKind.String)
                continue;
            var nodeId = nodeIdProp.GetString();
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;
            double score = 0.0;
            if (item.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                score = Math.Clamp(scoreProp.GetDouble(), 0.0, 1.0);
            map[nodeId] = score;
        }
        return map;
    }

    private static Dictionary<string, double>? GetRerankScoresOption(JsonObject? options)
    {
        if (options == null)
            return null;
        var rerankScoresVal = options.Get("rerankScores", null);
        if (rerankScoresVal == null || rerankScoresVal.Type != ValueType.Array)
            return null;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in rerankScoresVal.AsArray())
        {
            if (entry.Type != ValueType.Object || entry.AsObject() is not JsonObject entryObj)
                continue;
            var nodeIdVal = entryObj.Get("nodeId", null);
            if (nodeIdVal == null || nodeIdVal.Type != ValueType.String || string.IsNullOrWhiteSpace(nodeIdVal.AsString()))
                continue;
            var score = 0.0;
            var scoreVal = entryObj.Get("score", null);
            if (scoreVal != null)
            {
                if (scoreVal.Type == ValueType.Float)
                    score = Math.Clamp(scoreVal.AsFloat(), 0.0, 1.0);
                else if (scoreVal.Type == ValueType.Integer)
                    score = Math.Clamp(scoreVal.AsInteger(), 0.0, 1.0);
            }
            map[nodeIdVal.AsString()] = score;
        }
        return map;
    }
    
    private RuntimeValue CallGetRecent(List<RuntimeValue> args)
    {
        EnsureInitialized();
        
        int count = 5;
        if (args.Count >= 1 && args[0].Type == ValueType.Integer)
            count = Math.Max(1, args[0].AsInteger());
        
        string? phaseFilter = null;
        string? typeFilter = null;
        string? scopeFilter = null;
        if (args.Count >= 2 && args[1].Type == ValueType.String)
        {
            var phase = args[1].AsString();
            if (!string.IsNullOrWhiteSpace(phase))
                phaseFilter = phase;
        }
        if (args.Count >= 3 && args[2].Type == ValueType.String)
        {
            var type = args[2].AsString();
            if (!string.IsNullOrWhiteSpace(type))
                typeFilter = type;
        }
        if (args.Count >= 4 && args[3].Type == ValueType.String)
        {
            var scope = args[3].AsString();
            if (!string.IsNullOrWhiteSpace(scope))
                scopeFilter = scope;
        }

        JsonObject? options = null;
        if (args.Count >= 5 && args[4].Type == ValueType.Object && args[4].AsObject() is JsonObject optionsArg)
            options = optionsArg;
        var scopeHierarchy = ResolveScopeHierarchy(scopeFilter, options);
        
        return RuntimeValue.Array(CollectRecentEntries(count, phaseFilter, typeFilter, scopeFilter, scopeHierarchy));
    }

    private sealed class QueryDiagnosticsState
    {
        public string Query = "";
        public int MaxResults;
        public bool HybridLexical;
        public string LexicalMode = "none";
        public bool LexicalMinScoreAuto;
        public double LexicalMinScoreApplied = DefaultLexicalMinScore;
        public string LexicalMinScoreMode = "number";
        public int VectorCandidates;
        public int Bm25Candidates;
        public int AfterFilters;
        public int Returned;
        public int DroppedByLexicalMinScore;
        public int DroppedByTagFilter;
        public int DroppedByTypeFilter;
        public bool EmbedReady = true;
        public bool Detailed;
        public readonly List<(string NodeId, string Reason)> DroppedSamples = new();

        public void NoteDrop(string reason, string? nodeId = null)
        {
            if (!Detailed || DroppedSamples.Count >= 5 || string.IsNullOrEmpty(nodeId))
                return;
            DroppedSamples.Add((nodeId, reason));
        }
    }

    private static HashSet<string>? GetTagsFilterOption(JsonObject? options)
    {
        if (options == null)
            return null;
        var tags = NormalizeTagsValue(options.Get("tags", null));
        if (tags.Count == 0)
            return null;
        return new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveLexicalMinScoreOption(JsonObject? options, out double score, out bool auto)
    {
        score = DefaultLexicalMinScore;
        auto = false;
        if (options == null)
            return false;
        var val = options.Get("lexicalMinScore", null);
        if (val == null || val.Type == ValueType.Null)
            return false;
        if (val.Type == ValueType.String
            && string.Equals(val.AsString().Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            auto = true;
            score = DefaultLexicalMinScore;
            return true;
        }
        if (val.Type == ValueType.Float || val.Type == ValueType.Integer)
        {
            score = val.Type == ValueType.Integer ? val.AsInteger() : val.AsFloat();
            return true;
        }
        return false;
    }

    private RuntimeValue BuildLastQueryDiagnostics(QueryDiagnosticsState state)
    {
        var obj = new JsonObject();
        obj.Set("query", RuntimeValue.String(state.Query));
        obj.Set("maxResults", RuntimeValue.Integer(state.MaxResults));
        obj.Set("hybridLexical", RuntimeValue.Boolean(state.HybridLexical));
        obj.Set("lexicalMode", RuntimeValue.String(state.LexicalMode));
        obj.Set("lexicalMinScoreApplied", RuntimeValue.Float(state.LexicalMinScoreApplied));
        obj.Set("lexicalMinScoreMode", RuntimeValue.String(state.LexicalMinScoreMode));
        obj.Set("vectorCandidates", RuntimeValue.Integer(state.VectorCandidates));
        obj.Set("bm25Candidates", RuntimeValue.Integer(state.Bm25Candidates));
        obj.Set("afterFilters", RuntimeValue.Integer(state.AfterFilters));
        obj.Set("returned", RuntimeValue.Integer(state.Returned));
        obj.Set("droppedByLexicalMinScore", RuntimeValue.Integer(state.DroppedByLexicalMinScore));
        obj.Set("droppedByTagFilter", RuntimeValue.Integer(state.DroppedByTagFilter));
        obj.Set("droppedByTypeFilter", RuntimeValue.Integer(state.DroppedByTypeFilter));
        obj.Set("embedReady", RuntimeValue.Boolean(state.EmbedReady));
        if (state.Detailed && state.DroppedSamples.Count > 0)
        {
            var samples = new List<RuntimeValue>();
            foreach (var sample in state.DroppedSamples)
            {
                var row = new JsonObject();
                row.Set("nodeId", RuntimeValue.String(sample.NodeId));
                row.Set("reason", RuntimeValue.String(sample.Reason));
                samples.Add(RuntimeValue.Object(row));
            }
            obj.Set("droppedSamples", RuntimeValue.Array(samples));
        }
        return RuntimeValue.Object(obj);
    }
    
    private static string GetNodeScope(JsonObject nodeObj)
    {
        var scopeVal = nodeObj.Get("scope", null);
        if (scopeVal != null && scopeVal.Type == ValueType.String && !string.IsNullOrWhiteSpace(scopeVal.AsString()))
            return scopeVal.AsString();
        return "global";
    }
    
    private List<RuntimeValue> CollectRecentEntries(
        int count,
        string? phaseFilter,
        string? typeFilter,
        string? scopeFilter = null,
        HashSet<string>? scopeHierarchy = null,
        HashSet<string>? tagsFilter = null,
        string tagsMode = "any")
    {
        scopeHierarchy ??= ResolveScopeHierarchy(scopeFilter, null);
        var entries = new List<(DateTime Timestamp, RuntimeValue Value)>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            if (!MatchesMemoryFilters(kvp.Value, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, tagsFilter: tagsFilter, tagsMode: tagsMode))
                continue;
            
            var timestampVal = nodeObj.Get("timestamp", null);
            if (timestampVal == null || timestampVal.Type != ValueType.String)
                continue;
            if (!DateTime.TryParse(timestampVal.AsString(), out var timestamp))
                continue;
            
            entries.Add((timestamp, kvp.Value));
        }
        
        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        var result = new List<RuntimeValue>();
        var limit = Math.Max(1, count);
        for (int i = 0; i < entries.Count && i < limit; i++)
            result.Add(entries[i].Value);
        return result;
    }
    
    private static List<RuntimeValue> MergeMemoryResults(List<RuntimeValue> semanticResults, List<RuntimeValue> recentResults)
    {
        var merged = new List<RuntimeValue>();
        var seen = new HashSet<string>();
        
        void AddUnique(RuntimeValue value)
        {
            var key = BuildMemoryDedupKey(value);
            if (seen.Add(key))
                merged.Add(value);
        }
        
        foreach (var recent in recentResults)
            AddUnique(recent);
        foreach (var semantic in semanticResults)
            AddUnique(semantic);
        
        return merged;
    }
    
    private static string BuildMemoryDedupKey(RuntimeValue value)
    {
        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject nodeObj)
            return value.ToString() ?? Guid.NewGuid().ToString();
        
        var fact = nodeObj.Get("fact", null);
        if (fact != null && fact.Type == ValueType.String)
            return fact.AsString();
        
        var timestamp = nodeObj.Get("timestamp", null);
        if (timestamp != null && timestamp.Type == ValueType.String)
            return timestamp.AsString();
        
        return value.ToString() ?? Guid.NewGuid().ToString();
    }
    
    public static string FormatMemoryLine(RuntimeValue mem)
    {
        if (mem.Type != ValueType.Object || mem.AsObject() is not JsonObject memObj)
            return mem.ToString() ?? "";
        
        var fact = memObj.Get("fact", null);
        var factText = fact != null && fact.Type == ValueType.String ? fact.AsString() : mem.ToString() ?? "";
        
        var prefixParts = new List<string>();
        var phase = memObj.Get("phase", null);
        if (phase != null && phase.Type == ValueType.String && !string.IsNullOrWhiteSpace(phase.AsString()))
            prefixParts.Add(phase.AsString());
        var memType = memObj.Get("type", null);
        if (memType != null && memType.Type == ValueType.String && !string.IsNullOrWhiteSpace(memType.AsString()))
            prefixParts.Add(memType.AsString());
        var iteration = memObj.Get("iteration", null);
        if (iteration != null && (iteration.Type == ValueType.Integer || iteration.Type == ValueType.String))
            prefixParts.Add("iter " + iteration.ToString());
        
        if (prefixParts.Count == 0)
            return factText;
        
        return "[" + string.Join(" | ", prefixParts) + "] " + factText;
    }
}
