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
}
