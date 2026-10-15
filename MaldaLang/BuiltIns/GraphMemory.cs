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

public partial class GraphMemoryInstance : ObjectInstance
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
            name == "startKbWatch" || name == "stopKbWatch" ||
            name == "ask")
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
                case "ask":
                    return CallAsk(args);
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
