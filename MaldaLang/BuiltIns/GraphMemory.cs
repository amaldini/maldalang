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

public class GraphMemoryInstance : ObjectInstance
{
    private readonly object _lock = new object();
    private GraphInstance? _knowledgeGraph;
    private VectorDBInstance? _nodeIndex;
    private Interpreter? _interpreter;
    private int _nodeIdCounter;
    private Dictionary<string, RuntimeValue> _nodeMetadata;
    private KbWatchService? _kbWatchService;
    private string? _kbWatchSavePath;
    private volatile bool _reflectAsyncRunning;
    private readonly MemoryBm25Index _bm25Index = new();
    private MemoryOnnxCrossEncoder? _onnxCrossEncoder;
    private string? _onnxCrossEncoderPath;
    private RuntimeValue _lastQueryDiagnostics = RuntimeValue.Null();
    private const double Bm25ScoreNormalizationCap = 8.0;
    private FunctionValue? _customEmbeddingFunction;
    private int _currentDimension = DefaultDimension;
    private const int DefaultDimension = 384;
    private const string DefaultPrecision = "single";
    private const int DefaultMaxSimilarLinks = 5;
    private const double DefaultMinSimilarity = 0.6;
    private const double DefaultDedupSimilarity = 0.95;
    private const double SynapseSemanticBoost = 0.08;
    private const double SynapseProgressBoost = 0.04;
    private const double SynapseEpisodicPenalty = 0.06;
    private const int MaxIndexedDocumentChars = 12000;
    private const int DefaultIndexChunkSize = 2000;
    private const int DefaultIndexChunkOverlap = 100;
    private const string RelatedEdgeType = "related_to";
    private const string DerivedFromEdgeType = "derived_from";
    private const string SupersedesEdgeType = "supersedes";
    private const double DefaultLexicalWeight = 0.25;
    private const double DefaultLexicalMinScore = 0.15;
    private const int MaxConsolidatedContextChars = 6000;
    private const string BundleManifestVersion = "1";
    private const double ReflectMinConfidence = 0.7;
    private const double ReflectConflictSimilarityThreshold = 0.85;
    private const double DefaultActivationDecay = 0.85;
    private const double DefaultDiversity = 0.3;
    private const double SupersedesSimilarityThreshold = 0.9;
    private const double SupersededPenalty = 0.2;
    
    public GraphMemoryInstance() : base(null)
    {
        _nodeIdCounter = 0;
        _nodeMetadata = new Dictionary<string, RuntimeValue>();
    }
    
    public void SetInterpreter(Interpreter interpreter)
    {
        lock (_lock)
        {
            _interpreter = interpreter;
        }
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle method access - create a FunctionValue wrapper
        if (name == "remember" || name == "query" || name == "getRecent" || name == "addCodeElement" || 
            name == "findRelated" || name == "findCodeRelationships" || 
            name == "exportGraph" || name == "importGraph" || name == "clear" ||
            name == "forget" || name == "indexDocuments" || name == "stats" || name == "prune" ||
            name == "consolidate" || name == "reflect" || name == "reflectAsync" || name == "validate" || name == "enforceLimits" ||
            name == "exportBundle" || name == "importBundle" ||
            name == "getNode" || name == "hasNode" || name == "update" ||
            name == "reindexDocuments" || name == "analyzeFile" || name == "save" || name == "load" || name == "initialize" ||
            name == "forgetByScope" || name == "forgetByCategory" || name == "forgetByTag" ||
            name == "getLastQueryDiagnostics" ||
            name == "startKbWatch" || name == "stopKbWatch")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on GraphMemory.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter)
    {
        lock (_lock)
        {
            if (interpreter == null)
                interpreter = TranspiledBuiltinRuntime.GetOrCreateInterpreter();
            _interpreter = interpreter;
            switch (methodName)
            {
                case "initialize":
                    return CallInitialize(args);
                case "remember":
                    return CallRemember(args);
                case "query":
                    return CallQuery(args);
                case "getRecent":
                    return CallGetRecent(args);
                case "addCodeElement":
                    return CallAddCodeElement(args);
                case "findRelated":
                    return CallFindRelated(args);
                case "findCodeRelationships":
                    return CallFindCodeRelationships(args);
                case "exportGraph":
                    return CallExportGraph(args);
                case "importGraph":
                    return CallImportGraph(args);
                case "clear":
                    return CallClear(args);
                case "forget":
                    return CallForget(args);
                case "indexDocuments":
                    return CallIndexDocuments(args);
                case "reindexDocuments":
                    return CallReindexDocuments(args);
                case "stats":
                    return CallStats(args);
                case "prune":
                    return CallPrune(args);
                case "consolidate":
                    return CallConsolidate(args);
                case "reflect":
                    return CallReflect(args);
                case "reflectAsync":
                    return CallReflectAsync(args);
                case "validate":
                    return CallValidate(args);
                case "enforceLimits":
                    return CallEnforceLimits(args);
                case "exportBundle":
                    return CallExportBundle(args);
                case "importBundle":
                    return CallImportBundle(args);
                case "getNode":
                    return CallGetNode(args);
                case "hasNode":
                    return CallHasNode(args);
                case "update":
                    return CallUpdate(args);
                case "analyzeFile":
                    return CallAnalyzeFile(args);
                case "save":
                    return CallSave(args);
                case "load":
                    return CallLoad(args);
                case "forgetByScope":
                    return CallForgetByScope(args);
                case "forgetByCategory":
                    return CallForgetByCategory(args);
                case "forgetByTag":
                    return CallForgetByTag(args);
                case "getLastQueryDiagnostics":
                    return CallGetLastQueryDiagnostics(args);
                case "startKbWatch":
                    return CallStartKbWatch(args);
                case "stopKbWatch":
                    return CallStopKbWatch(args);
                default:
                    throw new RuntimeException($"GraphMemory has no method '{methodName}'.");
            }
        }
    }
    
    private RuntimeValue CallInitialize(List<RuntimeValue> args)
    {
        int dimension = DefaultDimension;
        string precision = DefaultPrecision;
        FunctionValue? customEmbeddingFunction = null;
        
        if (args.Count >= 1 && args[0].Type == ValueType.Integer)
        {
            dimension = args[0].AsInteger();
            if (dimension <= 0)
                throw new RuntimeException("initialize() dimension must be greater than 0");
        }
        
        if (args.Count >= 2 && args[1].Type == ValueType.String)
        {
            precision = args[1].AsString();
            if (precision != "single" && precision != "double")
                throw new RuntimeException("initialize() precision must be 'single' or 'double'");
        }
        
        // Optional 3rd parameter: custom embedding function
        if (args.Count >= 3 && args[2].Type == ValueType.Function)
        {
            customEmbeddingFunction = args[2].AsFunction();
            _customEmbeddingFunction = customEmbeddingFunction;
        }
        else
        {
            _customEmbeddingFunction = null;
        }
        
        // Store dimension for CalculateEmbedding
        _currentDimension = dimension;
        
        // Create graph
        _knowledgeGraph = new GraphInstance(isDirected: true);
        
        // Create VectorDB
        _nodeIndex = new VectorDBInstance(dimension, precision);
        
        // Initialize VectorDB with embedding function wrapper
        if (_interpreter != null)
        {
            var embedFunc = CreateEmbeddingWrapper(dimension, _interpreter, customEmbeddingFunction);
            if (embedFunc != null)
            {
                try
                {
                    _nodeIndex.CallMethod("init", new List<RuntimeValue> { RuntimeValue.Function(embedFunc) }, _interpreter);
                }
                catch
                {
                    // If initialization fails, VectorDB will work without calculator function
                    // (entries will need to be added with explicit vectors)
                }
            }
        }
        
        return RuntimeValue.Null();
    }
    
    private FunctionValue? CreateEmbeddingWrapper(int dimension, Interpreter interpreter, FunctionValue? customFunction = null)
    {
        // If custom embedding function provided, use it directly
        if (customFunction != null)
        {
            return customFunction;
        }
        
        // Otherwise, create a wrapper function using MALDA source code
        // This function will call embedBagOfWords with the captured dimension
        var wrapperSource = $"function embedWrapper(text) {{ return embedBagOfWords(text, {dimension}); }}";
        
        try
        {
            var lexer = new MaldaLang.Lexer(wrapperSource);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            var statements = parser.Parse();
            
            if (statements.Count > 0 && statements[0] is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl)
            {
                return new FunctionValue(funcDecl, interpreter._globals, false, null);
            }
        }
        catch (Exception ex)
        {
            // If parsing fails, we'll work without calculator function
            // Vectors will be added explicitly, and we'll implement manual search
            System.Diagnostics.Debug.WriteLine($"Failed to create embedding wrapper: {ex.Message}");
        }
        
        return null;
    }
    
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
        if (args.Count < 1 || args[0].Type != ValueType.Object || args[0].AsObject() is not JsonObject options)
            throw new RuntimeException("prune() expects 1 object argument (options)");
        
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
        {
            if (args[0].Type != ValueType.Object || args[0].AsObject() is not JsonObject optionsArg)
                throw new RuntimeException("consolidate() expects 0-1 object argument (options?)");
            options = optionsArg;
        }
        
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
    
    private RuntimeValue CallReflect(List<RuntimeValue> args)
    {
        JsonObject? options = null;
        if (args.Count >= 1)
        {
            if (args[0].Type != ValueType.Object || args[0].AsObject() is not JsonObject optionsArg)
                throw new RuntimeException("reflect() expects 0-1 object argument (options?)");
            options = optionsArg;
        }
        
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
        {
            if (args[0].Type != ValueType.Object || args[0].AsObject() is not JsonObject optionsArg)
                throw new RuntimeException("reflectAsync() expects 0-1 object argument (options?)");
            options = optionsArg;
        }

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

    /// <summary>
    /// Normalize tags from array or CSV string: trim, lowercase, dedupe, drop empties.
    /// </summary>
    private static List<string> NormalizeTagsValue(RuntimeValue? tagsVal)
    {
        var result = new List<string>();
        if (tagsVal == null || tagsVal.Type == ValueType.Null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddOne(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            var tag = raw.Trim().ToLowerInvariant();
            if (seen.Add(tag))
                result.Add(tag);
        }

        if (tagsVal.Type == ValueType.Array)
        {
            foreach (var item in tagsVal.AsArray())
            {
                if (item.Type == ValueType.String)
                    AddOne(item.AsString());
            }
        }
        else if (tagsVal.Type == ValueType.String)
        {
            foreach (var part in tagsVal.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddOne(part);
        }

        return result;
    }

    private static List<string> GetNodeTags(JsonObject nodeObj) =>
        NormalizeTagsValue(nodeObj.Get("tags", null));

    private static bool NodeMatchesTags(JsonObject nodeObj, HashSet<string>? tagsFilter, string tagsMode)
    {
        if (tagsFilter == null || tagsFilter.Count == 0)
            return true;

        var nodeTags = GetNodeTags(nodeObj);
        if (nodeTags.Count == 0)
            return false;

        var nodeSet = new HashSet<string>(nodeTags, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(tagsMode, "all", StringComparison.OrdinalIgnoreCase))
            return tagsFilter.All(t => nodeSet.Contains(t));

        // default: any
        return tagsFilter.Any(t => nodeSet.Contains(t));
    }
    
    private static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    
    private static string? NormalizeIndexedFilePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;
        return sourcePath.Replace('\\', '/').Trim();
    }
    
    private bool HasIndexedFileWithHash(string? filePath, string fileHash, string scope)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;
        
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            if (!string.Equals(GetMetadataString(nodeObj, "source"), "file", StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (!string.Equals(GetNodeScope(nodeObj), scope, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedPath = GetMetadataString(nodeObj, "filePath");
            if (string.IsNullOrEmpty(storedPath))
            {
                var contextVal = nodeObj.Get("context", null);
                if (contextVal != null && contextVal.Type == ValueType.String)
                    storedPath = NormalizeIndexedFilePath(contextVal.AsString().Split('#')[0]);
            }
            
            if (!string.Equals(storedPath, filePath, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedHash = GetMetadataString(nodeObj, "fileHash");
            if (string.Equals(storedHash, fileHash, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
    
    private int RemoveIndexedNodesForFile(string filePath, string scope)
    {
        var toRemove = new List<string>();
        foreach (var kvp in _nodeMetadata)
        {
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            
            if (!string.Equals(GetMetadataString(nodeObj, "source"), "file", StringComparison.OrdinalIgnoreCase))
                continue;
            
            if (!string.Equals(GetNodeScope(nodeObj), scope, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var storedPath = GetMetadataString(nodeObj, "filePath");
            if (string.IsNullOrEmpty(storedPath))
            {
                var contextVal = nodeObj.Get("context", null);
                if (contextVal != null && contextVal.Type == ValueType.String)
                    storedPath = NormalizeIndexedFilePath(contextVal.AsString().Split('#')[0]);
            }
            
            if (string.Equals(storedPath, filePath, StringComparison.OrdinalIgnoreCase))
                toRemove.Add(kvp.Key);
        }
        
        var removed = 0;
        foreach (var nodeId in toRemove)
        {
            if (RemoveNodeById(nodeId))
                removed++;
        }
        
        return removed;
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
    
    private RuntimeValue CallIndexDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("indexDocuments() expects (pattern, dirPath?, options?)");
        
        EnsureInitialized();
        
        var result = IndexDocumentsInternal(args, forceChangedOnly: null);
        return RuntimeValue.Integer(result.Indexed);
    }
    
    private RuntimeValue CallReindexDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("reindexDocuments() expects (pattern, dirPath?, options?)");
        
        EnsureInitialized();
        
        var result = IndexDocumentsInternal(args, forceChangedOnly: true);
        var obj = new JsonObject();
        obj.Set("indexed", RuntimeValue.Integer(result.Indexed));
        obj.Set("skipped", RuntimeValue.Integer(result.Skipped));
        obj.Set("removed", RuntimeValue.Integer(result.Removed));
        return RuntimeValue.Object(obj);
    }
    
    private (int Indexed, int Skipped, int Removed) IndexDocumentsInternal(List<RuntimeValue> args, bool? forceChangedOnly)
    {
        ParseIndexDocumentsArgs(args, out var loadArgs, out var chunkSize, out var overlap, out var scope, out var changedOnly);
        if (forceChangedOnly != null)
            changedOnly = forceChangedOnly.Value;
        var documents = AiPipelineHelpers.LoadDocuments(loadArgs);
        if (documents.Type != ValueType.Array)
            return (0, 0, 0);
        
        var indexed = 0;
        var skipped = 0;
        var removed = 0;
        foreach (var document in documents.AsArray())
        {
            if (!TryExtractDocumentContent(document, out var content, out var sourcePath))
                continue;
            
            if (string.IsNullOrWhiteSpace(content))
                continue;
            
            var normalizedPath = NormalizeIndexedFilePath(sourcePath);
            var fileHash = ComputeContentHash(content);
            if (changedOnly && HasIndexedFileWithHash(normalizedPath, fileHash, scope))
            {
                skipped++;
                continue;
            }
            
            if (!string.IsNullOrEmpty(normalizedPath))
                removed += RemoveIndexedNodesForFile(normalizedPath, scope);
            
            var chunks = BuildDocumentChunks(document, content, chunkSize, overlap);
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunkText = chunks[i];
                if (chunkText.Length > MaxIndexedDocumentChars)
                    chunkText = chunkText.Substring(0, MaxIndexedDocumentChars);
                
                var contextLabel = chunks.Count == 1
                    ? normalizedPath ?? ""
                    : $"{normalizedPath ?? "document"}#chunk-{i + 1}";
                
                var metadata = new JsonObject();
                metadata.Set("type", RuntimeValue.String("semantic"));
                metadata.Set("source", RuntimeValue.String("file"));
                metadata.Set("scope", RuntimeValue.String(scope));
                if (!string.IsNullOrEmpty(normalizedPath))
                    metadata.Set("filePath", RuntimeValue.String(normalizedPath));
                metadata.Set("fileHash", RuntimeValue.String(fileHash));
                
                CallRemember(new List<RuntimeValue>
                {
                    RuntimeValue.String(chunkText),
                    RuntimeValue.String(contextLabel),
                    RuntimeValue.Object(metadata)
                });
                indexed++;
            }
        }
        
        if (skipped > 0)
            System.Diagnostics.Debug.WriteLine($"indexDocuments skipped {skipped} unchanged file(s)");
        
        return (indexed, skipped, removed);
    }
    
    private List<string> BuildDocumentChunks(RuntimeValue document, string content, int chunkSize, int overlap)
    {
        if (chunkSize <= 0 || content.Length <= chunkSize)
            return new List<string> { content };
        
        var splitResult = AiPipelineHelpers.SplitDocuments(new List<RuntimeValue>
        {
            RuntimeValue.Array(new List<RuntimeValue> { document }),
            RuntimeValue.Integer(chunkSize),
            RuntimeValue.Integer(overlap)
        });
        
        if (splitResult.Type != ValueType.Array)
            return new List<string> { content };
        
        var chunks = new List<string>();
        foreach (var chunk in splitResult.AsArray())
        {
            if (TryExtractDocumentContent(chunk, out var chunkContent, out _)
                && !string.IsNullOrWhiteSpace(chunkContent))
                chunks.Add(chunkContent);
        }
        
        return chunks.Count > 0 ? chunks : new List<string> { content };
    }
    
    private static void ParseIndexDocumentsArgs(List<RuntimeValue> args, out List<RuntimeValue> loadArgs, out int chunkSize, out int overlap, out string scope, out bool changedOnly)
    {
        loadArgs = new List<RuntimeValue> { args[0] };
        chunkSize = DefaultIndexChunkSize;
        overlap = DefaultIndexChunkOverlap;
        scope = "global";
        changedOnly = false;
        
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.String)
            {
                loadArgs.Add(args[1]);
                if (args.Count >= 3 && args[2].Type == ValueType.Object && args[2].AsObject() is JsonObject optionsArg)
                    options = optionsArg;
            }
            else if (args[1].Type == ValueType.Object && args[1].AsObject() is JsonObject optionsOnly)
            {
                loadArgs.Add(RuntimeValue.String("."));
                options = optionsOnly;
            }
        }
        
        if (options != null)
        {
            chunkSize = GetIntOption(options, "chunkSize", chunkSize);
            overlap = GetIntOption(options, "overlap", overlap);
            var scopeOpt = GetStringOption(options, "scope");
            if (scopeOpt != null)
                scope = scopeOpt;
            changedOnly = GetBoolOption(options, "changedOnly", false);
        }
    }
    
    private static bool TryExtractDocumentContent(RuntimeValue document, out string content, out string? sourcePath)
    {
        content = "";
        sourcePath = null;
        
        if (document.AsObject() is DocumentInstance doc)
        {
            content = doc.Content;
            sourcePath = doc.GetMetadataString("source");
            return true;
        }
        
        if (document.Type == ValueType.Object && document.AsObject() is JsonObject docObj)
        {
            var contentVal = docObj.Get("content");
            if (contentVal.Type == ValueType.String)
                content = contentVal.AsString();
            
            var metadataVal = docObj.Get("metadata");
            if (metadataVal.Type == ValueType.Object && metadataVal.AsObject() is JsonObject metaObj)
            {
                var sourceVal = metaObj.Get("source");
                if (sourceVal.Type == ValueType.String)
                    sourcePath = sourceVal.AsString();
            }
            
            return content.Length > 0;
        }
        
        return false;
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
            return RuntimeValue.Array(semanticResults);
        }
        
        var recentResults = CollectRecentEntries(recentCount, phaseFilter, typeFilter, scopeFilter, scopeHierarchy, tagsFilter, tagsMode);
        var merged = MergeMemoryResults(semanticResults, recentResults);
        diagnostics.Returned = merged.Count;
        _lastQueryDiagnostics = BuildLastQueryDiagnostics(diagnostics);
        return RuntimeValue.Array(merged);
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
    
    private RuntimeValue CallGetNode(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("getNode() expects 1 string argument (nodeId)");
        
        EnsureInitialized();
        
        if (_nodeMetadata.TryGetValue(args[0].AsString(), out var nodeValue))
            return nodeValue;
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallHasNode(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("hasNode() expects 1 string argument (nodeId)");
        
        EnsureInitialized();
        return RuntimeValue.Boolean(_nodeMetadata.ContainsKey(args[0].AsString()));
    }
    
    private RuntimeValue CallUpdate(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args[0].Type != ValueType.String)
            throw new RuntimeException("update() expects at least 2 arguments (nodeId, fact, context?, metadata?)");
        
        EnsureInitialized();
        
        var nodeId = args[0].AsString();
        if (!_nodeMetadata.TryGetValue(nodeId, out var existingValue)
            || existingValue.Type != ValueType.Object
            || existingValue.AsObject() is not JsonObject existingObj)
            throw new RuntimeException($"update() node '{nodeId}' does not exist");
        
        var fact = args[1];
        RuntimeValue? context = args.Count > 2 && args[2].Type != ValueType.Object ? args[2] : null;
        if (context == null)
        {
            var existingContext = existingObj.Get("context", null);
            if (existingContext != null && existingContext.Type != ValueType.Null)
                context = existingContext;
        }
        
        JsonObject? metadataObj = null;
        if (args.Count >= 4 && args[3].Type == ValueType.Object && args[3].AsObject() is JsonObject metaArg)
            metadataObj = metaArg;
        else if (args.Count >= 3 && args[2].Type == ValueType.Object && args[2].AsObject() is JsonObject metaOnly)
            metadataObj = metaOnly;
        else
        {
            metadataObj = CopyMetadataFields(existingObj);
        }
        
        var descriptionBuilder = new System.Text.StringBuilder(BuildNodeDescription(fact, context));
        if (metadataObj != null)
            AppendMetadataToDescription(descriptionBuilder, metadataObj);
        
        UpdateExistingMemory(nodeId, fact, context, metadataObj, descriptionBuilder.ToString());
        return RuntimeValue.String(nodeId);
    }
    
    private void InitializeWithPreservedEmbedding(List<RuntimeValue> args)
    {
        var initArgs = new List<RuntimeValue>(args);
        if ((initArgs.Count < 3 || initArgs[2].Type != ValueType.Function) && _customEmbeddingFunction != null)
        {
            if (initArgs.Count == 0)
                initArgs.Add(RuntimeValue.Integer(_currentDimension > 0 ? _currentDimension : DefaultDimension));
            if (initArgs.Count == 1)
                initArgs.Add(RuntimeValue.String(DefaultPrecision));
            initArgs.Add(RuntimeValue.Function(_customEmbeddingFunction));
        }
        
        CallInitialize(initArgs);
    }
    
    private RuntimeValue CallAddCodeElement(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("addCodeElement() expects 2 arguments (elementId, elementData)");
        
        EnsureInitialized();
        
        var elementId = args[0].AsString();
        var elementData = args[1];
        
        // Create node ID
        var nodeId = $"code_{elementId}";
        
        // Extract description from element data
        var description = BuildCodeElementDescription(elementId, elementData);
        
        // Add to graph
        var nodeData = new JsonObject();
        nodeData.Set("type", RuntimeValue.String("code"));
        nodeData.Set("elementId", RuntimeValue.String(elementId));
        nodeData.Set("elementData", elementData);
        nodeData.Set("timestamp", RuntimeValue.String(DateTime.UtcNow.ToString("O")));
        
        _knowledgeGraph!.CallMethod("addNode", new List<RuntimeValue>
        {
            RuntimeValue.String(nodeId),
            RuntimeValue.Object(nodeData)
        }, _interpreter!);
        
        // Add to VectorDB
        var indexData = new JsonObject();
        indexData.Set("nodeId", RuntimeValue.String(nodeId));
        indexData.Set("description", RuntimeValue.String(description));
        indexData.Set("elementId", RuntimeValue.String(elementId));
        indexData.Set("elementData", elementData);
        
        // Store description with nodeId mapping
        // Calculate embedding manually to ensure we can store the nodeId mapping
        var embedding = CalculateEmbedding(description);
        _nodeIndex!.CallMethod("add", new List<RuntimeValue>
        {
            RuntimeValue.Array(embedding),
            RuntimeValue.Object(indexData) // Store nodeId mapping in data
        }, _interpreter!);
        
        _nodeMetadata[nodeId] = RuntimeValue.Object(nodeData);
        
        return RuntimeValue.String(nodeId);
    }
    
    private RuntimeValue CallFindRelated(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("findRelated() expects 1 string argument (nodeId)");
        
        EnsureInitialized();
        
        var nodeId = args[0].AsString();
        int maxDistance = 2;
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            maxDistance = args[1].AsInteger();
        }
        
        if (!_knowledgeGraph!.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(nodeId) }, _interpreter!).AsBoolean())
        {
            return RuntimeValue.Array(new List<RuntimeValue>());
        }
        
        var relatedNodes = new List<RuntimeValue>();
        
        if (maxDistance < 1)
            return RuntimeValue.Array(relatedNodes);
        
        foreach (var reachableId in CollectReachableWithinDistance(nodeId, maxDistance))
        {
            if (_nodeMetadata.TryGetValue(reachableId, out var nodeValue))
                relatedNodes.Add(nodeValue);
        }
        
        return RuntimeValue.Array(relatedNodes);
    }
    
    private IEnumerable<string> CollectReachableWithinDistance(string startNode, int maxDistance)
    {
        if (maxDistance < 1 || _knowledgeGraph == null || _interpreter == null)
            yield break;
        
        var visited = new HashSet<string>(StringComparer.Ordinal) { startNode };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((startNode, 0));
        
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDistance)
                continue;
            
            var neighborsResult = _knowledgeGraph.CallMethod("getNeighbors", new List<RuntimeValue>
            {
                RuntimeValue.String(current)
            }, _interpreter);
            
            if (neighborsResult.Type != ValueType.Array)
                continue;
            
            foreach (var neighbor in neighborsResult.AsArray())
            {
                if (neighbor.Type != ValueType.String)
                    continue;
                
                var neighborId = neighbor.AsString();
                if (!visited.Add(neighborId))
                    continue;
                
                yield return neighborId;
                queue.Enqueue((neighborId, depth + 1));
            }
        }
    }
    
    private static IEnumerable<string> CollectReachableNodeIds(RuntimeValue bfsResult)
    {
        if (bfsResult.Type == ValueType.Array)
        {
            foreach (var node in bfsResult.AsArray())
            {
                if (node.Type == ValueType.String)
                    yield return node.AsString();
            }
            yield break;
        }
        
        if (bfsResult.Type != ValueType.Object)
            yield break;
        
        var path = RuntimeValue.Null();
        var bfsObj = bfsResult.AsObject();
        if (bfsObj is JsonObject bfsJson)
            path = bfsJson.Get("path");
        else if (bfsObj is DictionaryInstance dict && dict.TryGetEntry("path", out var pathValue))
            path = pathValue;
        
        if (path.Type != ValueType.Array)
            yield break;
        
        foreach (var node in path.AsArray())
        {
            if (node.Type == ValueType.String)
                yield return node.AsString();
        }
    }
    
    private RuntimeValue CallFindCodeRelationships(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("findCodeRelationships() expects 1 string argument (codeElement)");
        
        EnsureInitialized();
        
        var codeElement = args[0].AsString();
        var nodeId = $"code_{codeElement}";
        
        if (!_knowledgeGraph!.CallMethod("hasNode", new List<RuntimeValue> { RuntimeValue.String(nodeId) }, _interpreter!).AsBoolean())
        {
            return RuntimeValue.Array(new List<RuntimeValue>());
        }
        
        // Find all edges connected to this node
        var edges = _knowledgeGraph.CallMethod("edges", new List<RuntimeValue>(), _interpreter!);
        var relationships = new List<RuntimeValue>();
        
        if (edges.Type == ValueType.Array)
        {
            foreach (var edge in edges.AsArray())
            {
                if (edge.Type == ValueType.Object)
                {
                    var edgeObj = edge.AsObject();
                    if (edgeObj is JsonObject edgeJson)
                    {
                        var from = edgeJson.Get("from");
                        var to = edgeJson.Get("to");
                        var edgeType = edgeJson.Get("type");
                        
                        if (from.Type == ValueType.String && from.AsString() == nodeId)
                        {
                            var rel = new JsonObject();
                            rel.Set("type", edgeType);
                            rel.Set("target", to);
                            relationships.Add(RuntimeValue.Object(rel));
                        }
                        else if (to.Type == ValueType.String && to.AsString() == nodeId)
                        {
                            var rel = new JsonObject();
                            rel.Set("type", edgeType);
                            rel.Set("source", from);
                            relationships.Add(RuntimeValue.Object(rel));
                        }
                    }
                }
            }
        }
        
        return RuntimeValue.Array(relationships);
    }
    
    private RuntimeValue CallExportGraph(List<RuntimeValue> args)
    {
        EnsureInitialized();
        return _knowledgeGraph!.CallMethod("serialize", new List<RuntimeValue>(), _interpreter!);
    }
    
    private RuntimeValue CallImportGraph(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("importGraph() expects 1 string argument (graphJson)");
        
        EnsureInitialized();
        
        var graphJson = args[0].AsString();
        var graphResult = _knowledgeGraph!.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(graphJson) }, _interpreter!);
        if (graphResult.Type == ValueType.Object && graphResult.AsObject() is GraphInstance loadedGraph)
            _knowledgeGraph = loadedGraph;
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallClear(List<RuntimeValue> args)
    {
        _knowledgeGraph = null;
        _nodeIndex = null;
        _nodeIdCounter = 0;
        _nodeMetadata.Clear();
        _bm25Index.Clear();
        _customEmbeddingFunction = null;
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallAnalyzeFile(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("analyzeFile() expects 1 string argument (filePath)");
        
        EnsureInitialized();
        
        var filePath = args[0].AsString();
        
        // Normalize path separators for the current platform
        filePath = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        
        // Use getSymbols to extract code structure
        if (_interpreter == null)
            throw new RuntimeException("Interpreter not set for GraphMemory");
        
        // Read file
        if (!File.Exists(filePath))
            throw new RuntimeException($"File not found: {filePath}");
        
        var source = File.ReadAllText(filePath);
        
        // Call getSymbols built-in function directly
        RuntimeValue symbolsResult;
        try
        {
            symbolsResult = BuiltInFunctions.CallBuiltInAsync(
                "getSymbols",
                new List<RuntimeValue> { RuntimeValue.String(source) },
                _interpreter
            ).GetAwaiter().GetResult();
        }
        catch (RuntimeException)
        {
            // Re-throw RuntimeException as-is
            throw;
        }
        catch (System.Exception ex)
        {
            // Convert System.Exception to RuntimeException so it can be caught by try-catch blocks
            throw new RuntimeException($"Error analyzing file: {ex.Message}");
        }
        
        if (symbolsResult.Type != ValueType.Object)
            return RuntimeValue.Integer(0);
        
        var symbols = symbolsResult.AsObject();
        if (symbols is not JsonObject symbolsJson)
            return RuntimeValue.Integer(0);
        
        // Create file node
        var fileNodeId = $"file_{filePath}";
        var fileData = new JsonObject();
        fileData.Set("type", RuntimeValue.String("file"));
        fileData.Set("path", RuntimeValue.String(filePath));
        
        _knowledgeGraph!.CallMethod("addNode", new List<RuntimeValue>
        {
            RuntimeValue.String(fileNodeId),
            RuntimeValue.Object(fileData)
        }, _interpreter);
        
        int elementCount = 0;
        
        // Process classes
        var classes = symbolsJson.Get("classes");
        if (classes.Type == ValueType.Array)
        {
            foreach (var cls in classes.AsArray())
            {
                if (cls.Type == ValueType.Object && cls.AsObject() is JsonObject clsJson)
                {
                    var className = clsJson.Get("name");
                    if (className.Type == ValueType.String)
                    {
                        var classNodeId = $"class_{className.AsString()}";
                        var classData = new JsonObject();
                        classData.Set("type", RuntimeValue.String("class"));
                        classData.Set("name", className);
                        classData.Set("file", RuntimeValue.String(filePath));
                        
                        _knowledgeGraph.CallMethod("addNode", new List<RuntimeValue>
                        {
                            RuntimeValue.String(classNodeId),
                            RuntimeValue.Object(classData)
                        }, _interpreter);
                        
                        // File contains class
                        var edgeProps = new DictionaryInstance();
                        edgeProps.SetEntry("type", RuntimeValue.String("contains"));
                        _knowledgeGraph.CallMethod("addEdge", new List<RuntimeValue>
                        {
                            RuntimeValue.String(fileNodeId),
                            RuntimeValue.String(classNodeId),
                            RuntimeValue.Float(1.0),
                            RuntimeValue.Object(edgeProps)
                        }, _interpreter);
                        
                        elementCount++;
                    }
                }
            }
        }
        
        // Process functions
        var functions = symbolsJson.Get("functions");
        if (functions.Type == ValueType.Array)
        {
            foreach (var func in functions.AsArray())
            {
                if (func.Type == ValueType.Object && func.AsObject() is JsonObject funcJson)
                {
                    var funcName = funcJson.Get("name");
                    if (funcName.Type == ValueType.String)
                    {
                        var funcNodeId = $"func_{funcName.AsString()}";
                        var funcData = new JsonObject();
                        funcData.Set("type", RuntimeValue.String("function"));
                        funcData.Set("name", funcName);
                        funcData.Set("file", RuntimeValue.String(filePath));
                        
                        var signature = funcJson.Get("signature");
                        if (signature.Type == ValueType.String)
                            funcData.Set("signature", signature);
                        
                        _knowledgeGraph.CallMethod("addNode", new List<RuntimeValue>
                        {
                            RuntimeValue.String(funcNodeId),
                            RuntimeValue.Object(funcData)
                        }, _interpreter);
                        
                        // File contains function
                        var edgeProps3 = new DictionaryInstance();
                        edgeProps3.SetEntry("type", RuntimeValue.String("contains"));
                        _knowledgeGraph.CallMethod("addEdge", new List<RuntimeValue>
                        {
                            RuntimeValue.String(fileNodeId),
                            RuntimeValue.String(funcNodeId),
                            RuntimeValue.Float(1.0),
                            RuntimeValue.Object(edgeProps3)
                        }, _interpreter);
                        
                        elementCount++;
                    }
                }
            }
        }
        
        return RuntimeValue.Integer(elementCount);
    }
    
    /// <summary>
    /// Resolves the base path for memory artifact files ({base}.graph.json, etc.).
    /// Dot-prefixed paths (e.g. .ralph-memory) must not use Path.ChangeExtension on Windows,
    /// where it incorrectly strips to the parent directory.
    /// </summary>
    private static string ResolveMemoryBasePath(string filePath)
    {
        if (EmbeddedFolderStore.IsEmbedPath(filePath))
        {
            // Avoid System.IO.Path APIs that mis-handle the embed: scheme on Windows.
            var normalized = filePath.Replace('\\', '/');
            ReadOnlySpan<string> suffixes =
            [
                ".graph.json",
                ".metadata.json",
                ".vectordb.bin",
                ".bundle.json",
                ".mem"
            ];
            foreach (var suffix in suffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return normalized.Substring(0, normalized.Length - suffix.Length);
            }

            return normalized;
        }

        var fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName) && fileName.StartsWith('.'))
            return filePath;

        var withoutExtension = Path.ChangeExtension(filePath, null);
        if (string.IsNullOrWhiteSpace(withoutExtension))
            return filePath;

        var trimmed = withoutExtension.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0 || trimmed.EndsWith('.'))
            return filePath;

        return withoutExtension;
    }

    private static string GetMemoryArtifactDirectory(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath);
        return string.IsNullOrEmpty(dir) ? "." : dir;
    }

    private static bool MemoryPathExists(string path)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
            return EmbeddedFolderStore.HasFile(path);
        return File.Exists(path);
    }

    private static string ReadMemoryText(string path)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            var text = EmbeddedFolderStore.ReadText(path);
            if (text == null)
                throw new RuntimeException($"Embedded memory artifact not found: {path}");
            return text;
        }

        return File.ReadAllText(path);
    }

    private static void RejectEmbedWrite(string path, string operation)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            throw new RuntimeException(
                $"{operation} cannot write to embedded path '{path}' (embed: folders are read-only).");
        }
    }

    private static void ResolveMemoryArtifactPaths(
        string canonicalBasePath,
        out string graphPath,
        out string metadataPath,
        out string vectordbPath)
    {
        graphPath = $"{canonicalBasePath}.graph.json";
        metadataPath = $"{canonicalBasePath}.metadata.json";
        vectordbPath = $"{canonicalBasePath}.vectordb.bin";

        // embed: paths must not use Path.Combine legacy fallback (breaks the scheme).
        if (EmbeddedFolderStore.IsEmbedPath(canonicalBasePath))
            return;

        if (File.Exists(graphPath))
            return;

        var dir = GetMemoryArtifactDirectory(canonicalBasePath);
        var legacyGraph = Path.Combine(dir, ".graph.json");
        if (!File.Exists(legacyGraph))
            return;

        graphPath = legacyGraph;
        metadataPath = Path.Combine(dir, ".metadata.json");
        vectordbPath = Path.Combine(dir, ".vectordb.bin");
    }

    private RuntimeValue CallEnforceLimits(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.Object || args[0].AsObject() is not JsonObject options)
            throw new RuntimeException("enforceLimits() expects 1 object argument (options)");
        
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
    
    private RuntimeValue CallExportBundle(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("exportBundle() expects 1 string argument (filePath)");
        
        EnsureInitialized();
        
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        RejectEmbedWrite(basePath, "exportBundle()");
        WriteMemoryArtifacts(basePath);
        
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        var manifestPath = $"{basePath}.bundle.json";
        var manifest = new Dictionary<string, object>
        {
            ["version"] = BundleManifestVersion,
            ["exportedAt"] = DateTime.UtcNow.ToString("O"),
            ["nodes"] = _nodeMetadata.Count,
            ["artifacts"] = new Dictionary<string, string>
            {
                ["graph"] = graphPath,
                ["metadata"] = metadataPath,
                ["vectordb"] = vectordbPath
            }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        
        return RuntimeValue.String(manifestPath);
    }
    
    private RuntimeValue CallImportBundle(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("importBundle() expects 1 string argument (filePath)");
        
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        var manifestPath = $"{basePath}.bundle.json";
        
        if (!MemoryPathExists(manifestPath))
            throw new RuntimeException($"importBundle() manifest not found: {manifestPath}");
        
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        if (!MemoryPathExists(graphPath) || !MemoryPathExists(metadataPath) || !MemoryPathExists(vectordbPath))
            throw new RuntimeException("importBundle() requires graph, metadata, and vectordb artifacts for the bundle base path");
        
        return CallLoad(args);
    }
    
    private static bool ResolveBackupEnabled(JsonObject? options)
    {
        if (options != null)
        {
            var backupVal = options.Get("backup", null);
            if (backupVal != null && backupVal.Type == ValueType.Boolean)
                return backupVal.AsBoolean();
        }

        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_BACKUP");
        return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveMaxBackups(JsonObject? options)
    {
        if (options != null)
        {
            var maxVal = options.Get("maxBackups", null);
            if (maxVal != null && maxVal.Type == ValueType.Integer && maxVal.AsInteger() > 0)
                return maxVal.AsInteger();
        }

        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_MAX_BACKUPS");
        if (int.TryParse(env, out var parsed) && parsed > 0)
            return parsed;
        return 5;
    }

    private static (bool Enabled, int MaxBackups) ResolveBackupOptions(JsonObject? options) =>
        (ResolveBackupEnabled(options), ResolveMaxBackups(options));

    private static void MaybeRotateBackups(string basePath, (bool Enabled, int MaxBackups) backupOptions)
    {
        if (!backupOptions.Enabled)
            return;

        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        if (!File.Exists(graphPath) && !File.Exists(metadataPath) && !File.Exists(vectordbPath))
            return;

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
        CopyIfExists(graphPath, $"{basePath}.backup.{stamp}.graph.json");
        CopyIfExists(metadataPath, $"{basePath}.backup.{stamp}.metadata.json");
        CopyIfExists(vectordbPath, $"{basePath}.backup.{stamp}.vectordb.bin");
        PruneRotatingBackups(basePath, backupOptions.MaxBackups);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
            return;
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void PruneRotatingBackups(string basePath, int maxBackups)
    {
        if (maxBackups <= 0)
            return;

        var dir = GetMemoryArtifactDirectory(basePath);
        var fileName = Path.GetFileName(basePath);
        if (string.IsNullOrEmpty(fileName))
            fileName = basePath;

        var stamps = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(dir, $"{fileName}.backup.*.graph.json"))
        {
            var name = Path.GetFileName(path);
            var prefix = $"{fileName}.backup.";
            var suffix = ".graph.json";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var stamp = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            if (!string.IsNullOrWhiteSpace(stamp))
                stamps.Add(stamp);
        }

        while (stamps.Count > maxBackups)
        {
            var oldest = stamps.Min!;
            stamps.Remove(oldest);
            DeleteIfExists($"{basePath}.backup.{oldest}.graph.json");
            DeleteIfExists($"{basePath}.backup.{oldest}.metadata.json");
            DeleteIfExists($"{basePath}.backup.{oldest}.vectordb.bin");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private void WriteMemoryArtifacts(string basePath)
    {
        RejectEmbedWrite(basePath, "save()");
        var graphJson = _knowledgeGraph!.CallMethod("serialize", new List<RuntimeValue>(), _interpreter!).AsString();
        File.WriteAllText($"{basePath}.graph.json", graphJson);
        
        _nodeIndex!.CallMethod("serialize", new List<RuntimeValue> { RuntimeValue.String($"{basePath}.vectordb.bin") }, _interpreter!);
        
        var metadataDict = new Dictionary<string, object>();
        foreach (var kvp in _nodeMetadata)
            metadataDict[kvp.Key] = ConvertRuntimeValueToJson(kvp.Value);
        
        var metadataJson = JsonSerializer.Serialize(metadataDict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText($"{basePath}.metadata.json", metadataJson);
    }
    
    private RuntimeValue CallSave(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("save() expects (filePath, options?)");
        
        EnsureInitialized();
        
        JsonObject? saveOptions = null;
        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.Object || args[1].AsObject() is not JsonObject optionsArg)
                throw new RuntimeException("save() optional second argument must be options object");
            saveOptions = optionsArg;
        }

        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        RejectEmbedWrite(basePath, "save()");
        MaybeRotateBackups(basePath, ResolveBackupOptions(saveOptions));
        WriteMemoryArtifacts(basePath);
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallLoad(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("load() expects 1 string argument (filePath, options?)");
        JsonObject? loadOptions = null;
        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.Object || args[1].AsObject() is not JsonObject optionsArg)
                throw new RuntimeException("load() optional second argument must be options object");
            loadOptions = optionsArg;
        }
        var migrateDualIndex = GetBoolOption(loadOptions, "migrateDualIndex", true);
        var filePath = args[0].AsString();
        var basePath = ResolveMemoryBasePath(filePath);
        ResolveMemoryArtifactPaths(basePath, out var graphPath, out var metadataPath, out var vectordbPath);
        
        var initialized = false;
        
        // Load graph
        if (MemoryPathExists(graphPath))
        {
            var graphJson = ReadMemoryText(graphPath);
            InitializeWithPreservedEmbedding(new List<RuntimeValue>());
            initialized = true;
            var graphResult = _knowledgeGraph!.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(graphJson) }, _interpreter!);
            if (graphResult.Type == ValueType.Object && graphResult.AsObject() is GraphInstance loadedGraph)
                _knowledgeGraph = loadedGraph;
        }
        
        // Load VectorDB
        if (MemoryPathExists(vectordbPath))
        {
            if (!initialized)
            {
                InitializeWithPreservedEmbedding(new List<RuntimeValue>());
                initialized = true;
            }

            var vdbResult = _nodeIndex!.CallMethod("deserialize", new List<RuntimeValue> { RuntimeValue.String(vectordbPath) }, _interpreter!);
            if (vdbResult.Type == ValueType.Object && vdbResult.AsObject() is VectorDBInstance loadedVdb)
            {
                _nodeIndex = loadedVdb;
                if (loadedVdb.Dimension > 0)
                    _currentDimension = loadedVdb.Dimension;
                var embedFunc = CreateEmbeddingWrapper(_currentDimension, _interpreter!, _customEmbeddingFunction);
                if (embedFunc != null)
                {
                    try
                    {
                        _nodeIndex.CallMethod("init", new List<RuntimeValue> { RuntimeValue.Function(embedFunc) }, _interpreter!);
                    }
                    catch
                    {
                        // Vector search still works with stored vectors if init fails
                    }
                }
            }
        }
        
        if (!initialized && MemoryPathExists(metadataPath))
            InitializeWithPreservedEmbedding(new List<RuntimeValue>());
        
        // Load metadata
        if (MemoryPathExists(metadataPath))
        {
            var metadataJson = ReadMemoryText(metadataPath);
            var doc = System.Text.Json.JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    var key = prop.Name;
                    var valueJson = prop.Value;
                    // Convert JSON back to RuntimeValue
                    var runtimeValue = JsonToRuntimeValue(valueJson);
                    if (runtimeValue.Type == ValueType.Object && runtimeValue.AsObject() is JsonObject nodeObj)
                    {
                        var nodeIdVal = nodeObj.Get("nodeId", null);
                        if (nodeIdVal == null || nodeIdVal.Type != ValueType.String)
                            nodeObj.Set("nodeId", RuntimeValue.String(key));
                        runtimeValue = RuntimeValue.Object(nodeObj);
                    }
                    _nodeMetadata[key] = runtimeValue;
                }
            }
        }
        
        RestoreNodeIdCounter();
        if (migrateDualIndex)
            MigrateDualIndexEntries();
        RepairUnmappedVectorIndex();
        RebuildBm25Index();

        return RuntimeValue.Null();
    }

    /// <summary>
    /// Older VectorDB saves wrote JsonObject payloads as "{}". Those entries still have
    /// vectors but no nodeId mapping, so hybrid ASK falls back to BM25-only (vec 0).
    /// Rebuild the vector index from metadata when nothing is mapped.
    /// </summary>
    private void RepairUnmappedVectorIndex()
    {
        if (_nodeIndex == null || _interpreter == null || _nodeMetadata.Count == 0)
            return;

        var mapped = _nodeIndex.CollectIndexedNodeIds();
        if (mapped.Count > 0)
            return;

        // Orphan vectors cannot be matched; drop them before re-embedding from metadata.
        _nodeIndex.ClearEntries();
        _currentDimension = _nodeIndex.Dimension > 0 ? _nodeIndex.Dimension : _currentDimension;

        foreach (var kvp in _nodeMetadata.ToList())
        {
            var nodeId = kvp.Key;
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var factVal = nodeObj.Get("fact", null);
            if (factVal == null || factVal.Type == ValueType.Null)
                continue;
            var description = GetStoredDescription(nodeId);
            IndexNodeVector(nodeId, factVal, description);
        }
    }

    private void MigrateDualIndexEntries()
    {
        foreach (var kvp in _nodeMetadata.ToList())
        {
            var nodeId = kvp.Key;
            if (kvp.Value.Type != ValueType.Object || kvp.Value.AsObject() is not JsonObject nodeObj)
                continue;
            var migratedVal = nodeObj.Get("dualIndexMigrated", null);
            if (migratedVal != null && migratedVal.Type == ValueType.Boolean && migratedVal.AsBoolean())
                continue;
            var factVal = nodeObj.Get("fact", null);
            if (factVal == null || factVal.Type == ValueType.Null)
                continue;
            _nodeIndex!.RemoveEntriesForNodeId(nodeId);
            var description = GetStoredDescription(nodeId);
            IndexNodeVector(nodeId, factVal, description);
            nodeObj.Set("dualIndexMigrated", RuntimeValue.Boolean(true));
            _nodeMetadata[nodeId] = RuntimeValue.Object(nodeObj);
            if (_knowledgeGraph != null && _interpreter != null)
            {
                _knowledgeGraph.CallMethod("setNodeData", new List<RuntimeValue>
                {
                    RuntimeValue.String(nodeId),
                    RuntimeValue.Object(nodeObj)
                }, _interpreter);
            }
        }
    }

    private RuntimeValue CallStartKbWatch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("startKbWatch() expects (dir, pattern?, options?)");
        EnsureInitialized();
        _kbWatchService?.Dispose();
        var dir = args[0].AsString();
        var pattern = "**/*.md";
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.String)
                pattern = args[1].AsString();
            else if (args[1].Type == ValueType.Object && args[1].AsObject() is JsonObject optsOnly)
                options = optsOnly;
        }
        if (args.Count >= 3 && args[2].Type == ValueType.Object && args[2].AsObject() is JsonObject opts)
            options = opts;
        var scope = GetStringOption(options, "scope") ?? "global";
        _kbWatchSavePath = GetStringOption(options, "savePath");
        var debounceMs = GetIntOption(options, "debounceMs", 2000);
        var patternFromOpts = GetStringOption(options, "pattern");
        if (!string.IsNullOrWhiteSpace(patternFromOpts))
            pattern = patternFromOpts!;
        _kbWatchService = new KbWatchService(dir, pattern, () =>
        {
            lock (_lock)
            {
                try
                {
                    var reindexOpts = new JsonObject();
                    reindexOpts.Set("changedOnly", RuntimeValue.Boolean(true));
                    reindexOpts.Set("scope", RuntimeValue.String(scope));
                    CallReindexDocuments(new List<RuntimeValue>
                    {
                        RuntimeValue.String(pattern),
                        RuntimeValue.String(dir),
                        RuntimeValue.Object(reindexOpts)
                    });
                    if (!string.IsNullOrWhiteSpace(_kbWatchSavePath))
                        CallSave(new List<RuntimeValue> { RuntimeValue.String(_kbWatchSavePath!) });
                }
                catch
                {
                }
            }
        }, debounceMs);
        _kbWatchService.Start();
        return RuntimeValue.Boolean(true);
    }

    private RuntimeValue CallStopKbWatch(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("stopKbWatch() expects no arguments");
        _kbWatchService?.Dispose();
        _kbWatchService = null;
        _kbWatchSavePath = null;
        return RuntimeValue.Boolean(true);
    }

    private RuntimeValue CallForgetByScope(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new RuntimeException("forgetByScope() expects scope string and optional options object");
        var scope = args[0].AsString();
        JsonObject? options = null;
        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.Object || args[1].AsObject() is not JsonObject opts)
                throw new RuntimeException("forgetByScope() optional second argument must be options object");
            options = opts;
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
            if (args[1].Type != ValueType.Object || args[1].AsObject() is not JsonObject opts)
                throw new RuntimeException("forgetByCategory() optional second argument must be options object");
            options = opts;
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
            if (args[1].Type != ValueType.Object || args[1].AsObject() is not JsonObject opts)
                throw new RuntimeException("forgetByTag() optional second argument must be options object");
            options = opts;
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

    private RuntimeValue CallGetLastQueryDiagnostics(List<RuntimeValue> args)
    {
        return _lastQueryDiagnostics;
    }
    
    private void RestoreNodeIdCounter()
    {
        var maxId = -1;
        foreach (var key in _nodeMetadata.Keys)
        {
            if (!key.StartsWith("node_", StringComparison.Ordinal))
                continue;
            if (int.TryParse(key.AsSpan(5), out var id))
                maxId = Math.Max(maxId, id);
        }
        
        if (maxId >= 0)
            _nodeIdCounter = maxId + 1;
    }
    
    private RuntimeValue JsonToRuntimeValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt32(out int intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            case System.Text.Json.JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            case System.Text.Json.JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            case System.Text.Json.JsonValueKind.Null:
                return RuntimeValue.Null();
            case System.Text.Json.JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            case System.Text.Json.JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            default:
                return RuntimeValue.Null();
        }
    }
    
    private void EnsureInitialized()
    {
        if (_knowledgeGraph == null || _nodeIndex == null)
        {
            CallInitialize(new List<RuntimeValue>());
        }
    }
    
    private string BuildNodeDescription(RuntimeValue fact, RuntimeValue? context)
    {
        var description = new System.Text.StringBuilder();
        
        if (fact.Type == ValueType.String)
        {
            description.Append(fact.AsString());
        }
        else if (fact.Type == ValueType.Object)
        {
            // Try to extract text from object
            if (fact.AsObject() is JsonObject jsonObj)
            {
                var type = jsonObj.Get("type");
                var name = jsonObj.Get("name");
                var desc = jsonObj.Get("description");
                
                if (type.Type == ValueType.String)
                    description.Append(type.AsString()).Append(" ");
                if (name.Type == ValueType.String)
                    description.Append(name.AsString()).Append(" ");
                if (desc.Type == ValueType.String)
                    description.Append(desc.AsString());
            }
        }
        
        if (context != null && context.Type == ValueType.String)
        {
            description.Append(" ").Append(context.AsString());
        }
        
        return description.ToString();
    }
    
    private static void ApplyMetadataFields(JsonObject nodeData, JsonObject metadataObj)
    {
        foreach (var field in new[] { "phase", "type", "source", "scope", "category" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                nodeData.Set(field, val);
        }

        var tags = NormalizeTagsValue(metadataObj.Get("tags", null));
        if (tags.Count > 0)
            nodeData.Set("tags", RuntimeValue.Array(tags.Select(RuntimeValue.String).ToList()));
        
        var iterationVal = metadataObj.Get("iteration", null);
        if (iterationVal != null && iterationVal.Type != ValueType.Null)
            nodeData.Set("iteration", iterationVal);
        
        var consolidatedVal = metadataObj.Get("consolidated", null);
        if (consolidatedVal != null && consolidatedVal.Type != ValueType.Null)
            nodeData.Set("consolidated", consolidatedVal);
        
        var confidenceVal = metadataObj.Get("confidence", null);
        if (confidenceVal != null && confidenceVal.Type != ValueType.Null)
            nodeData.Set("confidence", confidenceVal);
        
        var importanceVal = metadataObj.Get("importance", null);
        if (importanceVal != null && importanceVal.Type != ValueType.Null)
            nodeData.Set("importance", importanceVal);
        
        var accessCountVal = metadataObj.Get("accessCount", null);
        if (accessCountVal != null && accessCountVal.Type != ValueType.Null)
            nodeData.Set("accessCount", accessCountVal);
        
        var lastAccessedVal = metadataObj.Get("lastAccessed", null);
        if (lastAccessedVal != null && lastAccessedVal.Type != ValueType.Null)
            nodeData.Set("lastAccessed", lastAccessedVal);
        
        foreach (var field in new[] { "filePath", "fileHash" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type != ValueType.Null)
                nodeData.Set(field, val);
        }
    }
    
    private static void AppendMetadataToDescription(System.Text.StringBuilder description, JsonObject metadataObj)
    {
        foreach (var field in new[] { "phase", "type", "source", "scope", "category" })
        {
            var val = metadataObj.Get(field, null);
            if (val != null && val.Type == ValueType.String && !string.IsNullOrWhiteSpace(val.AsString()))
                description.Append(' ').Append(val.AsString());
        }

        foreach (var tag in NormalizeTagsValue(metadataObj.Get("tags", null)))
            description.Append(' ').Append(tag);
        
        var iterationVal = metadataObj.Get("iteration", null);
        if (iterationVal != null && (iterationVal.Type == ValueType.Integer || iterationVal.Type == ValueType.String))
            description.Append(" iteration:").Append(iterationVal.ToString());
        
        var confidenceVal = metadataObj.Get("confidence", null);
        if (confidenceVal != null && (confidenceVal.Type == ValueType.Float || confidenceVal.Type == ValueType.Integer))
            description.Append(" confidence:").Append(confidenceVal.ToString());
    }
    
    private static JsonObject? ParseQueryOptions(List<RuntimeValue> args, out int maxResults)
    {
        maxResults = 5;
        JsonObject? options = null;

        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.Integer)
                maxResults = Math.Max(1, args[1].AsInteger());
            else if (args[1].Type == ValueType.Object)
                options = CoerceToJsonObject(args[1]);
        }

        if (args.Count >= 3 && args[2].Type == ValueType.Object)
            options = CoerceToJsonObject(args[2]) ?? options;

        if (options != null)
        {
            var maxFromOptions = GetIntOption(options, "maxResults", maxResults);
            maxResults = Math.Max(1, maxFromOptions);
        }

        return options;
    }

    /// <summary>
    /// Interpreter object literals are <see cref="JsonObject"/>; transpiled ones are
    /// <see cref="DictionaryInstance"/>. GraphMemory option/metadata helpers expect JsonObject.
    /// </summary>
    private static JsonObject? CoerceToJsonObject(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
            return null;
        var obj = value.AsObject();
        if (obj is JsonObject json)
            return json;
        if (obj is DictionaryInstance dict)
        {
            var copy = new JsonObject();
            foreach (var kvp in dict.GetEntries())
                copy.Set(kvp.Key, kvp.Value);
            return copy;
        }
        return null;
    }
    
    private static int GetIntOption(JsonObject? options, string key, int defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Integer)
            return val.AsInteger();
        return defaultValue;
    }
    
    private static bool GetBoolOption(JsonObject? options, string key, bool defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Boolean)
            return val.AsBoolean();
        return defaultValue;
    }
    
    private static bool? GetOptionalBoolOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Boolean)
            return val.AsBoolean();
        return null;
    }
    
    private static HashSet<string>? GetStringListOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        
        var val = options.Get(key, null);
        if (val == null || val.Type == ValueType.Null)
            return null;
        
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (val.Type == ValueType.Array)
        {
            foreach (var item in val.AsArray())
            {
                if (item.Type == ValueType.String && !string.IsNullOrWhiteSpace(item.AsString()))
                    result.Add(item.AsString().Trim());
            }
        }
        else if (val.Type == ValueType.String)
        {
            foreach (var part in val.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result.Add(part);
        }
        
        return result.Count > 0 ? result : null;
    }
    
    private static string? GetStringOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.String)
        {
            var text = val.AsString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        return null;
    }
    
    private static double GetDoubleOption(JsonObject? options, string key, double defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val == null)
            return defaultValue;
        if (val.Type == ValueType.Float)
            return val.AsFloat();
        if (val.Type == ValueType.Integer)
            return val.AsInteger();
        return defaultValue;
    }
    
    private static bool UseBm25Lexical(JsonObject? options)
    {
        if (GetBoolOption(options, "bm25", false))
            return true;
        var mode = GetStringOption(options, "lexicalMode");
        if (string.Equals(mode, "bm25", StringComparison.OrdinalIgnoreCase))
            return true;
        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_LEXICAL_MODE");
        return string.Equals(env, "bm25", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string>? ResolveScopeHierarchy(string? scopeFilter, JsonObject? options)
    {
        var fromOptions = GetStringListOption(options, "scopeHierarchy");
        if (fromOptions != null && fromOptions.Count > 0)
            return fromOptions;

        var fromEnv = ParseScopeHierarchyEnv(System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_HIERARCHY"));
        if (fromEnv != null && fromEnv.Count > 0)
            return MergeScopeHierarchy(scopeFilter, fromEnv);

        if (string.IsNullOrWhiteSpace(scopeFilter))
            return null;

        var hierarchy = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            scopeFilter.Trim(),
            "global"
        };
        var parent = GetStringOption(options, "scopeParent");
        if (string.IsNullOrWhiteSpace(parent))
            parent = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_PARENT");
        if (!string.IsNullOrWhiteSpace(parent))
            hierarchy.Add(parent.Trim());
        return hierarchy;
    }

    private static HashSet<string>? ParseScopeHierarchyEnv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value.Trim());
                    }
                }
                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> MergeScopeHierarchy(string? scopeFilter, HashSet<string> configuredHierarchy)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scopeFilter))
            merged.Add(scopeFilter.Trim());
        foreach (var scope in configuredHierarchy)
            merged.Add(scope);
        if (!merged.Contains("global"))
            merged.Add("global");
        return merged;
    }

    private static double NormalizeBm25Score(double raw) =>
        Math.Min(1.0, raw / Bm25ScoreNormalizationCap);

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

    private static bool MatchesMemoryFilters(
        RuntimeValue nodeValue,
        string? phaseFilter,
        string? typeFilter,
        string? scopeFilter = null,
        HashSet<string>? scopeHierarchy = null,
        string? excludeTypeFilter = null,
        HashSet<string>? includeTypesFilter = null,
        HashSet<string>? tagsFilter = null,
        string tagsMode = "any",
        QueryDiagnosticsState? diagnostics = null)
    {
        if (nodeValue.Type != ValueType.Object || nodeValue.AsObject() is not JsonObject nodeObj)
            return true;
        
        if (phaseFilter != null)
        {
            var phaseVal = nodeObj.Get("phase", null);
            if (phaseVal == null || phaseVal.Type != ValueType.String ||
                !string.Equals(phaseVal.AsString(), phaseFilter, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics?.NoteDrop("type_or_phase_filter");
                return false;
            }
        }
        
        if (typeFilter != null)
        {
            var typeVal = nodeObj.Get("type", null);
            if (typeVal == null || typeVal.Type != ValueType.String ||
                !string.Equals(typeVal.AsString(), typeFilter, StringComparison.OrdinalIgnoreCase))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (includeTypesFilter != null && includeTypesFilter.Count > 0)
        {
            var includeTypeVal = nodeObj.Get("type", null);
            var includeType = includeTypeVal != null && includeTypeVal.Type == ValueType.String
                ? includeTypeVal.AsString()
                : "";
            if (!includeTypesFilter.Contains(includeType))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (excludeTypeFilter != null)
        {
            var typeVal = nodeObj.Get("type", null);
            if (typeVal != null && typeVal.Type == ValueType.String &&
                string.Equals(typeVal.AsString(), excludeTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                if (diagnostics != null)
                    diagnostics.DroppedByTypeFilter++;
                return false;
            }
        }
        
        if (scopeHierarchy != null && scopeHierarchy.Count > 0)
        {
            var nodeScope = GetNodeScope(nodeObj);
            if (!scopeHierarchy.Contains(nodeScope))
                return false;
        }
        else if (scopeFilter != null)
        {
            var nodeScope = GetNodeScope(nodeObj);
            if (!string.Equals(nodeScope, scopeFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(nodeScope, "global", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!NodeMatchesTags(nodeObj, tagsFilter, tagsMode))
        {
            if (diagnostics != null)
                diagnostics.DroppedByTagFilter++;
            return false;
        }
        
        return true;
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
    
    private string BuildCodeElementDescription(string elementId, RuntimeValue elementData)
    {
        var description = new System.Text.StringBuilder();
        description.Append(elementId).Append(" ");
        
        if (elementData.Type == ValueType.Object && elementData.AsObject() is JsonObject jsonObj)
        {
            var type = jsonObj.Get("type");
            var desc = jsonObj.Get("description");
            var name = jsonObj.Get("name");
            
            if (type.Type == ValueType.String)
                description.Append(type.AsString()).Append(" ");
            if (name.Type == ValueType.String)
                description.Append(name.AsString()).Append(" ");
            if (desc.Type == ValueType.String)
                description.Append(desc.AsString());
        }
        
        return description.ToString();
    }
    
    private object ConvertRuntimeValueToJson(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                return value.AsInteger();
            case ValueType.Float:
                return value.AsFloat();
            case ValueType.String:
                return value.AsString();
            case ValueType.Boolean:
                return value.AsBoolean();
            case ValueType.Null:
                return null;
            case ValueType.Array:
                var arr = value.AsArray();
                var jsonArray = new List<object>();
                foreach (var item in arr)
                {
                    jsonArray.Add(ConvertRuntimeValueToJson(item));
                }
                return jsonArray;
            case ValueType.Object:
                var obj = value.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var key in jsonObj.GetAllKeys())
                    {
                        var propValue = jsonObj.Get(key);
                        dict[key] = ConvertRuntimeValueToJson(propValue);
                    }
                    return dict;
                }
                return obj.ToString();
            default:
                return value.ToString();
        }
    }
    
    private List<RuntimeValue> CalculateEmbedding(string text)
    {
        if (_interpreter == null)
            throw new RuntimeException("Interpreter not set for GraphMemory");
        
        RuntimeValue embedding;
        
        // Use custom embedding function if available
        if (_customEmbeddingFunction != null)
        {
            try
            {
                var task = _interpreter.CallFunctionAsync(_customEmbeddingFunction, new List<RuntimeValue> { RuntimeValue.String(text) });
                embedding = task.GetAwaiter().GetResult();
            }
            catch (System.Exception ex) when (!(ex is RuntimeException))
            {
                throw new RuntimeException($"Custom embedding function failed: {ex.Message}");
            }
        }
        else
        {
            // Fall back to embedBagOfWords built-in function
            try
            {
                embedding = BuiltInFunctions.CallBuiltInAsync(
                    "embedBagOfWords",
                    new List<RuntimeValue> 
                    { 
                        RuntimeValue.String(text),
                        RuntimeValue.Integer(_currentDimension)
                    },
                    _interpreter
                ).GetAwaiter().GetResult();
            }
            catch (System.Exception ex) when (!(ex is RuntimeException))
            {
                // Convert System.Exception to RuntimeException so it can be caught by try-catch blocks
                throw new RuntimeException(ex.Message);
            }
        }
        
        if (embedding.Type != ValueType.Array)
            throw new RuntimeException("Embedding function did not return an array");
        
        return embedding.AsArray();
    }
}
