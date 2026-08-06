// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.BuiltIns;

public class VectorDBInstance : ObjectInstance
{
    private readonly int _dimension;
    private readonly string _precision;
    private FunctionValue? _vectorCalculator;
    private readonly List<VectorEntry> _entries;
    private readonly bool _isSinglePrecision;
    
    private class VectorEntry
    {
        public float[]? FloatVector { get; set; }
        public double[]? DoubleVector { get; set; }
        public RuntimeValue Data { get; set; } = RuntimeValue.Null();
    }
    
    // Public constructor
    public VectorDBInstance(int dimension, string precision)
        : base(VectorDBClassDefinition.Instance)
    {
        if (dimension <= 0)
            throw new RuntimeException("VectorDB dimension must be greater than 0");
        
        if (precision != "single" && precision != "double")
            throw new RuntimeException("VectorDB precision must be 'single' or 'double'");
        
        _dimension = dimension;
        _precision = precision;
        _isSinglePrecision = precision == "single";
        _entries = new List<VectorEntry>();
        _vectorCalculator = null;
    }
    
    // Internal constructor for deserialization
    private VectorDBInstance(int dimension, string precision, List<VectorEntry> entries)
        : base(VectorDBClassDefinition.Instance)
    {
        _dimension = dimension;
        _precision = precision;
        _isSinglePrecision = precision == "single";
        _entries = entries;
        _vectorCalculator = null;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "add" or "searchSimilar" or "serialize" or "deserialize" or "init" or "asRetriever")
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
        switch (methodName)
        {
            case "add":
                return CallAdd(arguments, interpreter);
            case "searchSimilar":
                return CallSearchSimilar(arguments, interpreter);
            case "serialize":
                return CallSerialize(arguments);
            case "deserialize":
                return CallDeserialize(arguments, interpreter);
            case "init":
                return CallInit(arguments, interpreter);
            case "asRetriever":
                return CallAsRetriever(arguments);
            default:
                throw new RuntimeException($"VectorDB has no method '{methodName}'.");
        }
    }

    private RuntimeValue CallAsRetriever(List<RuntimeValue> arguments)
    {
        var topK = 5;
        var minScore = 0.0;

        if (arguments.Count > 0 && arguments[0].Type == ValueType.Object)
        {
            var optionsObj = arguments[0].AsObject();
            if (optionsObj is DictionaryInstance dict)
            {
                if (dict.TryGetEntry("topK", out var topKValue))
                    topK = ReadIntOption(topKValue, topK);

                if (dict.TryGetEntry("minScore", out var minScoreValue))
                    minScore = ReadDoubleOption(minScoreValue, minScore);
            }
            else if (optionsObj is BuiltIns.JsonObject jsonObj)
            {
                var topKValue = jsonObj.Get("topK");
                if (topKValue.Type != ValueType.Null)
                    topK = ReadIntOption(topKValue, topK);

                var minScoreValue = jsonObj.Get("minScore");
                if (minScoreValue.Type != ValueType.Null)
                    minScore = ReadDoubleOption(minScoreValue, minScore);
            }
        }

        if (topK <= 0)
            throw new RuntimeException("asRetriever() topK must be greater than 0.");

        return RuntimeValue.Object(new RetrieverInstance(this, topK, minScore));
    }

    private static int ReadIntOption(RuntimeValue value, int defaultValue) =>
        value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => (int)value.AsFloat(),
            _ => defaultValue
        };

    private static double ReadDoubleOption(RuntimeValue value, double defaultValue) =>
        value.Type switch
        {
            ValueType.Float => value.AsFloat(),
            ValueType.Integer => value.AsInteger(),
            _ => defaultValue
        };
    
    private RuntimeValue CallAdd(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("add() expects 1 or 2 arguments: add(data) or add(vector, data)");
        
        RuntimeValue data;
        VectorEntry entry;
        
        if (arguments.Count == 1)
        {
            // Single argument: assume it's data, calculate vector if calculator is set
            data = arguments[0];
            
            if (_vectorCalculator == null)
                throw new RuntimeException("add() with single argument requires a calculator function. Call init(calculatorFunction) first, or use add(vector, data)");
            
            entry = CalcVector(ExtractEmbeddableContent(data), interpreter);
            entry.Data = data;
        }
        else
        {
            // Two arguments: check if first is array (vector) or data
            var firstArg = arguments[0];
            data = arguments[1];
            
            if (firstArg.Type == ValueType.Array)
            {
                // First arg is vector, second is data
                var vectorArray = firstArg.AsArray();
                if (vectorArray.Count != _dimension)
                    throw new RuntimeException($"add() vector dimension mismatch: expected {_dimension}, got {vectorArray.Count}");
                
                entry = new VectorEntry { Data = data };
                
                if (_isSinglePrecision)
                {
                    var floatVector = new float[_dimension];
                    for (int i = 0; i < _dimension; i++)
                    {
                        var elem = vectorArray[i];
                        if (elem.Type == ValueType.Integer)
                            floatVector[i] = elem.AsInteger();
                        else if (elem.Type == ValueType.Float)
                            floatVector[i] = (float)elem.AsFloat();
                        else
                            throw new RuntimeException($"add() vector element at index {i} must be a number");
                    }
                    entry.FloatVector = floatVector;
                }
                else
                {
                    var doubleVector = new double[_dimension];
                    for (int i = 0; i < _dimension; i++)
                    {
                        var elem = vectorArray[i];
                        if (elem.Type == ValueType.Integer)
                            doubleVector[i] = elem.AsInteger();
                        else if (elem.Type == ValueType.Float)
                            doubleVector[i] = elem.AsFloat();
                        else
                            throw new RuntimeException($"add() vector element at index {i} must be a number");
                    }
                    entry.DoubleVector = doubleVector;
                }
            }
            else
            {
                // First arg is not an array - this is invalid when 2 arguments are provided
                throw new RuntimeException("add() with 2 arguments requires first argument to be an array (vector). Use add(data) for single argument, or add(vector, data) with vector as first argument");
            }
        }
        
        _entries.Add(entry);
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue CallInit(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("init() expects 1 argument (calculatorFunction)");
        
        var calculatorValue = arguments[0];
        if (calculatorValue.Type != ValueType.Function)
            throw new RuntimeException("init() argument must be a function");
        
        _vectorCalculator = calculatorValue.AsFunction();
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue CallSearchSimilar(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("searchSimilar() expects 2 arguments (data, topN)");
        
        var data = arguments[0];
        var topNValue = arguments[1];
        
        if (topNValue.Type != ValueType.Integer)
            throw new RuntimeException("searchSimilar() second argument must be an integer");
        
        int topN = topNValue.AsInteger();
        if (topN <= 0)
            throw new RuntimeException("searchSimilar() topN must be greater than 0");
        
        if (_vectorCalculator == null)
            throw new RuntimeException("searchSimilar() requires a calculator function. Call init(calculatorFunction) first");
        
        // Calculate query vector
        var queryVector = CalcVector(data, interpreter);
        
        // Calculate similarities
        var results = new List<(VectorEntry entry, double similarity, RuntimeValue originalVector)>();
        
        foreach (var entry in _entries)
        {
            double similarity;
            RuntimeValue originalVector;
            
            if (_isSinglePrecision)
            {
                similarity = CosineSimilarity(queryVector.FloatVector!, entry.FloatVector!);
                originalVector = ConvertToRuntimeArray(entry.FloatVector!);
            }
            else
            {
                similarity = CosineSimilarity(queryVector.DoubleVector!, entry.DoubleVector!);
                originalVector = ConvertToRuntimeArray(entry.DoubleVector!);
            }
            
            results.Add((entry, similarity, originalVector));
        }
        
        // Sort by similarity (descending) and take top N
        var topResults = results
            .OrderByDescending(r => r.similarity)
            .Take(topN)
            .ToList();
        
        // Build result array
        var resultArray = new List<RuntimeValue>();
        foreach (var (entry, similarity, originalVector) in topResults)
        {
            var resultObj = new JsonObject();
            resultObj.Set("vector", originalVector);
            resultObj.Set("data", entry.Data);
            resultObj.Set("similarity", RuntimeValue.Float(similarity));
            resultArray.Add(RuntimeValue.Object(resultObj));
        }
        
        return RuntimeValue.Array(resultArray);
    }
    
    /// <summary>Returns distinct nodeIds referenced by indexed vector entries.</summary>
    public HashSet<string> CollectIndexedNodeIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            if (TryExtractNodeId(entry.Data, out var nodeId))
                ids.Add(nodeId);
        }
        return ids;
    }

    public int EntryCount => _entries.Count;

    /// <summary>Removes all indexed entries whose data object contains the given nodeId.</summary>
    public int RemoveEntriesForNodeId(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return 0;
        
        var removed = 0;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (TryExtractNodeId(_entries[i].Data, out var entryNodeId)
                && string.Equals(entryNodeId, nodeId, StringComparison.Ordinal))
            {
                _entries.RemoveAt(i);
                removed++;
            }
        }
        
        return removed;
    }
    
    private static bool TryExtractNodeId(RuntimeValue data, out string nodeId)
    {
        nodeId = "";
        if (data.Type != ValueType.Object || data.AsObject() is not JsonObject dataObj)
            return false;
        
        var nodeIdVal = dataObj.Get("nodeId");
        if (nodeIdVal.Type != ValueType.String)
            return false;
        
        nodeId = nodeIdVal.AsString();
        return !string.IsNullOrEmpty(nodeId);
    }
    
    private VectorEntry CalcVector(RuntimeValue data, Interpreter interpreter)
    {
        if (_vectorCalculator == null)
            throw new RuntimeException("Vector calculator function is not set");
        
        // Call the vector calculator function
        var task = interpreter.CallFunctionAsync(_vectorCalculator, new List<RuntimeValue> { data });
        var result = task.GetAwaiter().GetResult();
        
        if (result.Type != ValueType.Array)
            throw new RuntimeException("Vector calculator function must return an array");
        
        var vectorArray = result.AsArray();
        if (vectorArray.Count != _dimension)
            throw new RuntimeException($"Vector calculator returned dimension {vectorArray.Count}, expected {_dimension}");
        
        var entry = new VectorEntry { Data = data };
        
        if (_isSinglePrecision)
        {
            var floatVector = new float[_dimension];
            for (int i = 0; i < _dimension; i++)
            {
                var elem = vectorArray[i];
                if (elem.Type == ValueType.Integer)
                    floatVector[i] = elem.AsInteger();
                else if (elem.Type == ValueType.Float)
                    floatVector[i] = (float)elem.AsFloat();
                else
                    throw new RuntimeException($"Vector calculator returned non-numeric value at index {i}");
            }
            entry.FloatVector = floatVector;
        }
        else
        {
            var doubleVector = new double[_dimension];
            for (int i = 0; i < _dimension; i++)
            {
                var elem = vectorArray[i];
                if (elem.Type == ValueType.Integer)
                    doubleVector[i] = elem.AsInteger();
                else if (elem.Type == ValueType.Float)
                    doubleVector[i] = elem.AsFloat();
                else
                    throw new RuntimeException($"Vector calculator returned non-numeric value at index {i}");
            }
            entry.DoubleVector = doubleVector;
        }
        
        return entry;
    }

    private static RuntimeValue ExtractEmbeddableContent(RuntimeValue data)
    {
        if (data.Type == ValueType.Object && data.AsObject() is BuiltIns.DocumentInstance doc)
            return RuntimeValue.String(doc.Content);

        if (data.Type == ValueType.Object && data.AsObject() is BuiltIns.JsonObject jsonObj)
        {
            var contentValue = jsonObj.Get("content");
            if (contentValue.Type == ValueType.String)
                return contentValue;
        }

        return data;
    }
    
    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new RuntimeException("Vector dimension mismatch in cosine similarity");
        
        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        
        normA = System.Math.Sqrt(normA);
        normB = System.Math.Sqrt(normB);
        
        if (normA == 0.0 || normB == 0.0)
            return 0.0;
        
        return dotProduct / (normA * normB);
    }
    
    private double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length)
            throw new RuntimeException("Vector dimension mismatch in cosine similarity");
        
        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        
        normA = System.Math.Sqrt(normA);
        normB = System.Math.Sqrt(normB);
        
        if (normA == 0.0 || normB == 0.0)
            return 0.0;
        
        return dotProduct / (normA * normB);
    }
    
    private RuntimeValue ConvertToRuntimeArray(float[] vector)
    {
        var list = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            list.Add(RuntimeValue.Float(val));
        }
        return RuntimeValue.Array(list);
    }
    
    private RuntimeValue ConvertToRuntimeArray(double[] vector)
    {
        var list = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            list.Add(RuntimeValue.Float(val));
        }
        return RuntimeValue.Array(list);
    }
    
    private RuntimeValue CallSerialize(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("serialize() expects 1 argument (filePath)");
        
        var filePathValue = arguments[0];
        if (filePathValue.Type != ValueType.String)
            throw new RuntimeException("serialize() file path must be a string");
        
        var filePath = filePathValue.AsString();
        if (EmbeddedFolderStore.IsEmbedPath(filePath))
        {
            throw new RuntimeException(
                $"serialize() cannot write to embedded path '{filePath}' (embed: folders are read-only).");
        }
        
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream, Encoding.UTF8);
            
            // Write magic number
            writer.Write(Encoding.UTF8.GetBytes("VDB2")); // Version 2: no calculator function stored
            
            // Write dimension
            writer.Write(_dimension);
            
            // Write precision (0 = single, 1 = double)
            writer.Write((byte)(_isSinglePrecision ? 0 : 1));
            
            // Write entry count
            writer.Write(_entries.Count);
            
            // Write entries
            foreach (var entry in _entries)
            {
                if (_isSinglePrecision)
                {
                    foreach (var val in entry.FloatVector!)
                    {
                        writer.Write(val);
                    }
                }
                else
                {
                    foreach (var val in entry.DoubleVector!)
                    {
                        writer.Write(val);
                    }
                }
                
                // Serialize data as JSON
                var dataJson = SerializeRuntimeValue(entry.Data);
                var dataBytes = Encoding.UTF8.GetBytes(dataJson);
                writer.Write(dataBytes.Length);
                writer.Write(dataBytes);
            }
            
            return RuntimeValue.String(filePath);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Failed to serialize VectorDB to file: {ex.Message}");
        }
    }
    
    private RuntimeValue CallDeserialize(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("deserialize() expects 1 argument (filePath)");
        
        var filePathValue = arguments[0];
        
        if (filePathValue.Type != ValueType.String)
            throw new RuntimeException("deserialize() file path must be a string");
        
        var filePath = filePathValue.AsString();
        
        try
        {
            Stream stream;
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var bytes = EmbeddedFolderStore.ReadBytes(filePath);
                if (bytes == null)
                    throw new FileNotFoundException($"VectorDB file not found: {filePath}", filePath);
                stream = new MemoryStream(bytes, writable: false);
            }
            else
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }

            using (stream)
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                // Read and validate magic number
                var magicBytes = reader.ReadBytes(4);
                var magic = Encoding.UTF8.GetString(magicBytes);
                
                if (magic != "VDB2")
                    throw new RuntimeException("Invalid VectorDB file format: incorrect magic number. Expected VDB2 format.");
                
                // Read dimension
                var dimension = reader.ReadInt32();
                
                // Read precision
                var precisionByte = reader.ReadByte();
                var precision = precisionByte == 0 ? "single" : "double";
                
                // Read entry count
                var entryCount = reader.ReadInt32();
                
                // Read entries
                var entries = new List<VectorEntry>();
                for (int i = 0; i < entryCount; i++)
                {
                    var entry = new VectorEntry();
                    
                    if (precisionByte == 0)
                    {
                        var floatVector = new float[dimension];
                        for (int j = 0; j < dimension; j++)
                        {
                            floatVector[j] = reader.ReadSingle();
                        }
                        entry.FloatVector = floatVector;
                    }
                    else
                    {
                        var doubleVector = new double[dimension];
                        for (int j = 0; j < dimension; j++)
                        {
                            doubleVector[j] = reader.ReadDouble();
                        }
                        entry.DoubleVector = doubleVector;
                    }
                    
                    // Read data
                    var dataLength = reader.ReadInt32();
                    var dataBytes = reader.ReadBytes(dataLength);
                    var dataJson = Encoding.UTF8.GetString(dataBytes);
                    entry.Data = DeserializeRuntimeValue(dataJson);
                    
                    entries.Add(entry);
                }
                
                // Create and return new VectorDBInstance (calculator not set, must call init after)
                var vectorDB = new VectorDBInstance(dimension, precision, entries);
                return RuntimeValue.Object(vectorDB);
            }
        }
        catch (FileNotFoundException)
        {
            throw new RuntimeException($"VectorDB file not found: {filePath}");
        }
        catch (Exception ex) when (!(ex is RuntimeException))
        {
            throw new RuntimeException($"Failed to deserialize VectorDB from file: {ex.Message}");
        }
    }
    
    private string SerializeRuntimeValue(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return JsonSerializer.Serialize(value.AsString());
            
            case ValueType.Integer:
                return value.AsInteger().ToString();
            
            case ValueType.Float:
                return value.AsFloat().ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            
            case ValueType.Boolean:
                return value.AsBoolean() ? "true" : "false";
            
            case ValueType.Null:
                return "null";
            
            case ValueType.Array:
                var arr = value.AsArray();
                var items = arr.Select(SerializeRuntimeValue);
                return "[" + string.Join(",", items) + "]";
            
            case ValueType.Object:
                var obj = value.AsObject();
                if (obj is DictionaryInstance dict)
                {
                    return SerializeDictionaryInstance(dict);
                }
                return "{}";
            
            default:
                return "null";
        }
    }
    
    private string SerializeDictionaryInstance(DictionaryInstance dict)
    {
        var props = new List<string>();
        foreach (var kvp in dict.Entries)
        {
            var key = JsonSerializer.Serialize(kvp.Key);
            var val = SerializeRuntimeValue(kvp.Value);
            props.Add($"{key}:{val}");
        }
        return "{" + string.Join(",", props) + "}";
    }
    
    private RuntimeValue DeserializeRuntimeValue(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return DeserializeJsonElement(doc.RootElement);
        }
        catch
        {
            // Fallback: try to parse as simple value
            if (json == "null")
                return RuntimeValue.Null();
            if (json == "true")
                return RuntimeValue.Boolean(true);
            if (json == "false")
                return RuntimeValue.Boolean(false);
            if (int.TryParse(json, out var intVal))
                return RuntimeValue.Integer(intVal);
            if (double.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
                return RuntimeValue.Float(doubleVal);
            if (json.StartsWith("\"") && json.EndsWith("\""))
                return RuntimeValue.String(json.Substring(1, json.Length - 2));
            
            throw new RuntimeException($"Failed to deserialize value: {json}");
        }
    }
    
    private RuntimeValue DeserializeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            
            case JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(DeserializeJsonElement(item));
                }
                return RuntimeValue.Array(arr);
            
            case JsonValueKind.Object:
                var dict = new DictionaryInstance();
                foreach (var prop in element.EnumerateObject())
                {
                    dict.Set(prop.Name, DeserializeJsonElement(prop.Value));
                }
                return RuntimeValue.Object(dict);
            
            default:
                return RuntimeValue.Null();
        }
    }
}
