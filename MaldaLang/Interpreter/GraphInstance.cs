// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

public class GraphInstance : ObjectInstance
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly bool _isDirected;
    private int _edgeCount;
    
    // Internal constructor for deserialization
    private GraphInstance(bool isDirected, Dictionary<string, GraphNode> nodes, int edgeCount)
        : base(GraphClassDefinition.Instance)
    {
        _nodes = nodes;
        _isDirected = isDirected;
        _edgeCount = edgeCount;
    }
    
    public GraphInstance(bool isDirected = true)
        : base(GraphClassDefinition.Instance)
    {
        _nodes = new Dictionary<string, GraphNode>();
        _isDirected = isDirected;
        _edgeCount = 0;
    }
    
    public bool IsDirected => _isDirected;
    public IReadOnlyDictionary<string, GraphNode> Nodes => _nodes;
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Built-in graph methods are provided via the built-in method dispatch pipeline.
        if (name is "addNode" or "addEdge" or "removeNode" or "removeEdge" or "setNodeData" or
            "getNode" or "getNeighbors" or "getEdges" or "hasNode" or "hasEdge" or 
            "getWeight" or "nodeCount" or "edgeCount" or "isDirected" or "nodes" or "edges" or
            "bfs" or "dfs" or "shortestPath" or "topologicalSort" or "connectedComponents" or 
            "isCyclic" or "minimumSpanningTree" or "serialize" or "deserialize")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }
        
        // Fallback to base behavior (class fields/methods)
        return base.Get(name, accessingClass);
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter interpreter)
    {
        switch (methodName)
        {
            case "addNode":
                return CallAddNode(arguments);
            case "addEdge":
                return CallAddEdge(arguments);
            case "removeNode":
                return CallRemoveNode(arguments);
            case "removeEdge":
                return CallRemoveEdge(arguments);
            case "setNodeData":
                return CallSetNodeData(arguments);
            case "getNode":
                return CallGetNode(arguments);
            case "getNeighbors":
                return CallGetNeighbors(arguments);
            case "getEdges":
                return CallGetEdges(arguments);
            case "hasNode":
                return CallHasNode(arguments);
            case "hasEdge":
                return CallHasEdge(arguments);
            case "getWeight":
                return CallGetWeight(arguments);
            case "nodeCount":
                return CallNodeCount(arguments);
            case "edgeCount":
                return CallEdgeCount(arguments);
            case "isDirected":
                return CallIsDirected(arguments);
            case "nodes":
                return CallNodes(arguments);
            case "edges":
                return CallEdges(arguments);
            case "bfs":
                return CallBfs(arguments);
            case "dfs":
                return CallDfs(arguments);
            case "shortestPath":
                return CallShortestPath(arguments);
            case "topologicalSort":
                return CallTopologicalSort(arguments);
            case "connectedComponents":
                return CallConnectedComponents(arguments);
            case "isCyclic":
                return CallIsCyclic(arguments);
            case "minimumSpanningTree":
                return CallMinimumSpanningTree(arguments);
            case "serialize":
                return CallSerialize(arguments);
            case "deserialize":
                return CallDeserialize(arguments);
            default:
                throw new RuntimeException($"Graph has no method '{methodName}'.");
        }
    }
    
    // Basic Operations
    
    private RuntimeValue CallAddNode(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("addNode() expects 1 or 2 arguments (id, data?)");
        
        var idValue = arguments[0];
        if (idValue.Type != ValueType.String)
            throw new RuntimeException("addNode() node ID must be a string");
        
        var id = idValue.AsString();
        var data = arguments.Count > 1 ? arguments[1] : null;
        
        if (_nodes.ContainsKey(id))
            throw new RuntimeException($"Node '{id}' already exists");
        
        _nodes[id] = new GraphNode(id, data);
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue CallAddEdge(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 2 || arguments.Count > 4)
            throw new RuntimeException("addEdge() expects 2-4 arguments (from, to, weight?, properties?)");
        
        var fromValue = arguments[0];
        var toValue = arguments[1];
        
        if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
            throw new RuntimeException("addEdge() node IDs must be strings");
        
        var from = fromValue.AsString();
        var to = toValue.AsString();
        
        if (!_nodes.ContainsKey(from))
            throw new RuntimeException($"Node '{from}' does not exist");
        if (!_nodes.ContainsKey(to))
            throw new RuntimeException($"Node '{to}' does not exist");
        
        double weight = 1.0;
        if (arguments.Count > 2 && arguments[2].Type != ValueType.Null)
        {
            if (arguments[2].Type == ValueType.Integer)
                weight = arguments[2].AsInteger();
            else if (arguments[2].Type == ValueType.Float)
                weight = arguments[2].AsFloat();
            else
                throw new RuntimeException("addEdge() weight must be a number");
        }
        
        DictionaryInstance? properties = null;
        if (arguments.Count > 3 && arguments[3].Type != ValueType.Null)
        {
            if (arguments[3].Type == ValueType.Object && arguments[3].AsObject() is DictionaryInstance dict)
                properties = dict;
            else
                throw new RuntimeException("addEdge() properties must be a dictionary");
        }
        
        var edge = new GraphEdge(from, to, weight, properties);
        _nodes[from].AddEdge(edge);
        _edgeCount++;
        
        // For undirected graphs, add reverse edge
        if (!_isDirected)
        {
            var reverseEdge = new GraphEdge(to, from, weight, properties);
            _nodes[to].AddEdge(reverseEdge);
        }
        
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue CallRemoveNode(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("removeNode() expects 1 argument");
        
        var idValue = arguments[0];
        if (idValue.Type != ValueType.String)
            throw new RuntimeException("removeNode() node ID must be a string");
        
        var id = idValue.AsString();
        
        if (!_nodes.TryGetValue(id, out var nodeToRemove))
            return RuntimeValue.Boolean(false);
        
        // Remove all edges FROM this node (outgoing edges)
        var outgoingEdgeCount = nodeToRemove.Edges.Count;
        _edgeCount -= outgoingEdgeCount;
        
        // Remove all edges TO this node (incoming edges from other nodes)
        foreach (var node in _nodes.Values)
        {
            if (node.Id != id)
            {
                var removed = node.RemoveEdge(id);
                if (removed)
                {
                    _edgeCount--;
                    if (!_isDirected)
                        _edgeCount--; // Undirected edges counted twice
                }
            }
        }
        
        _nodes.Remove(id);
        return RuntimeValue.Boolean(true);
    }
    
    private RuntimeValue CallSetNodeData(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("setNodeData() expects 2 arguments (nodeId, data)");
        
        if (arguments[0].Type != ValueType.String)
            throw new RuntimeException("setNodeData() node ID must be a string");
        
        var id = arguments[0].AsString();
        if (!_nodes.TryGetValue(id, out var node))
            return RuntimeValue.Boolean(false);
        
        node.Data = arguments[1];
        return RuntimeValue.Boolean(true);
    }
    
    private RuntimeValue CallRemoveEdge(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("removeEdge() expects 2 arguments");
        
        var fromValue = arguments[0];
        var toValue = arguments[1];
        
        if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
            throw new RuntimeException("removeEdge() node IDs must be strings");
        
        var from = fromValue.AsString();
        var to = toValue.AsString();
        
        if (!_nodes.ContainsKey(from))
            return RuntimeValue.Boolean(false);
        
        var removed = _nodes[from].RemoveEdge(to);
        if (removed)
        {
            _edgeCount--;
            
            // For undirected graphs, remove reverse edge
            if (!_isDirected && _nodes.ContainsKey(to))
            {
                _nodes[to].RemoveEdge(from);
            }
        }
        
        return RuntimeValue.Boolean(removed);
    }
    
    private RuntimeValue CallGetNode(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("getNode() expects 1 argument");
        
        var idValue = arguments[0];
        if (idValue.Type != ValueType.String)
            throw new RuntimeException("getNode() node ID must be a string");
        
        var id = idValue.AsString();
        
        if (!_nodes.TryGetValue(id, out var node))
            return RuntimeValue.Null();
        
        return node.Data ?? RuntimeValue.Null();
    }
    
    private RuntimeValue CallGetNeighbors(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("getNeighbors() expects 1 argument");
        
        var idValue = arguments[0];
        if (idValue.Type != ValueType.String)
            throw new RuntimeException("getNeighbors() node ID must be a string");
        
        var id = idValue.AsString();
        
        if (!_nodes.TryGetValue(id, out var node))
            throw new RuntimeException($"Node '{id}' does not exist");
        
        var neighbors = node.GetNeighborIds().Select(RuntimeValue.String).ToList();
        return RuntimeValue.Array(neighbors);
    }
    
    private RuntimeValue CallGetEdges(List<RuntimeValue> arguments)
    {
        if (arguments.Count > 2)
            throw new RuntimeException("getEdges() expects 0, 1, or 2 arguments");
        
        var result = new List<RuntimeValue>();
        
        if (arguments.Count == 0)
        {
            // Return all edges
            foreach (var node in _nodes.Values)
            {
                foreach (var edge in node.Edges)
                {
                    // For undirected graphs, only include each edge once
                    if (_isDirected || edge.SourceId.CompareTo(edge.TargetId) <= 0)
                    {
                        var edgeArray = new List<RuntimeValue>
                        {
                            RuntimeValue.String(edge.SourceId),
                            RuntimeValue.String(edge.TargetId),
                            RuntimeValue.Float(edge.Weight)
                        };
                        result.Add(RuntimeValue.Array(edgeArray));
                    }
                }
            }
        }
        else if (arguments.Count == 1)
        {
            // Return edges from a specific node
            var fromValue = arguments[0];
            if (fromValue.Type != ValueType.String)
                throw new RuntimeException("getEdges() node ID must be a string");
            
            var from = fromValue.AsString();
            if (!_nodes.TryGetValue(from, out var node))
                throw new RuntimeException($"Node '{from}' does not exist");
            
            foreach (var edge in node.Edges)
            {
                var edgeArray = new List<RuntimeValue>
                {
                    RuntimeValue.String(edge.SourceId),
                    RuntimeValue.String(edge.TargetId),
                    RuntimeValue.Float(edge.Weight)
                };
                result.Add(RuntimeValue.Array(edgeArray));
            }
        }
        else
        {
            // Return edges between two specific nodes
            var fromValue = arguments[0];
            var toValue = arguments[1];
            
            if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
                throw new RuntimeException("getEdges() node IDs must be strings");
            
            var from = fromValue.AsString();
            var to = toValue.AsString();
            
            if (!_nodes.TryGetValue(from, out var node))
                throw new RuntimeException($"Node '{from}' does not exist");
            
            foreach (var edge in node.Edges)
            {
                if (edge.TargetId == to)
                {
                    var edgeArray = new List<RuntimeValue>
                    {
                        RuntimeValue.String(edge.SourceId),
                        RuntimeValue.String(edge.TargetId),
                        RuntimeValue.Float(edge.Weight)
                    };
                    result.Add(RuntimeValue.Array(edgeArray));
                }
            }
        }
        
        return RuntimeValue.Array(result);
    }
    
    private RuntimeValue CallHasNode(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("hasNode() expects 1 argument");
        
        var idValue = arguments[0];
        if (idValue.Type != ValueType.String)
            throw new RuntimeException("hasNode() node ID must be a string");
        
        var id = idValue.AsString();
        return RuntimeValue.Boolean(_nodes.ContainsKey(id));
    }
    
    private RuntimeValue CallHasEdge(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("hasEdge() expects 2 arguments");
        
        var fromValue = arguments[0];
        var toValue = arguments[1];
        
        if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
            throw new RuntimeException("hasEdge() node IDs must be strings");
        
        var from = fromValue.AsString();
        var to = toValue.AsString();
        
        if (!_nodes.TryGetValue(from, out var node))
            return RuntimeValue.Boolean(false);
        
        return RuntimeValue.Boolean(node.Edges.Any(e => e.TargetId == to));
    }
    
    private RuntimeValue CallGetWeight(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("getWeight() expects 2 arguments");
        
        var fromValue = arguments[0];
        var toValue = arguments[1];
        
        if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
            throw new RuntimeException("getWeight() node IDs must be strings");
        
        var from = fromValue.AsString();
        var to = toValue.AsString();
        
        if (!_nodes.TryGetValue(from, out var node))
            throw new RuntimeException($"Node '{from}' does not exist");
        
        var edge = node.Edges.FirstOrDefault(e => e.TargetId == to);
        if (edge == null)
            throw new RuntimeException($"Edge from '{from}' to '{to}' does not exist");
        
        return RuntimeValue.Float(edge.Weight);
    }
    
    private RuntimeValue CallNodeCount(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("nodeCount() expects 0 arguments");
        
        return RuntimeValue.Integer(_nodes.Count);
    }
    
    private RuntimeValue CallEdgeCount(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("edgeCount() expects 0 arguments");
        
        return RuntimeValue.Integer(_edgeCount);
    }
    
    private RuntimeValue CallIsDirected(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("isDirected() expects 0 arguments");
        
        return RuntimeValue.Boolean(_isDirected);
    }
    
    private RuntimeValue CallNodes(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("nodes() expects 0 arguments");
        
        var nodeIds = _nodes.Keys.Select(RuntimeValue.String).ToList();
        return RuntimeValue.Array(nodeIds);
    }
    
    private RuntimeValue CallEdges(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("edges() expects 0 arguments");
        
        return CallGetEdges(new List<RuntimeValue>());
    }
    
    // Graph Algorithms
    
    private RuntimeValue CallBfs(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("bfs() expects 1 or 2 arguments (start, target?)");
        
        var startValue = arguments[0];
        if (startValue.Type != ValueType.String)
            throw new RuntimeException("bfs() start node ID must be a string");
        
        var start = startValue.AsString();
        
        if (!_nodes.ContainsKey(start))
            throw new RuntimeException($"Node '{start}' does not exist");
        
        if (arguments.Count == 2)
        {
            // BFS to find path to target
            var targetValue = arguments[1];
            if (targetValue.Type != ValueType.String)
                throw new RuntimeException("bfs() target node ID must be a string");
            
            var target = targetValue.AsString();
            var path = BfsPath(start, target);
            
            var result = new DictionaryInstance();
            result.SetEntry("path", RuntimeValue.Array(path.Select(RuntimeValue.String).ToList()));
            result.SetEntry("found", RuntimeValue.Boolean(path.Count > 0));
            return RuntimeValue.Object(result);
        }
        else
        {
            // BFS to get all reachable nodes
            var visited = BfsTraversal(start);
            return RuntimeValue.Array(visited.Select(RuntimeValue.String).ToList());
        }
    }
    
    private RuntimeValue CallDfs(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("dfs() expects 1 or 2 arguments (start, target?)");
        
        var startValue = arguments[0];
        if (startValue.Type != ValueType.String)
            throw new RuntimeException("dfs() start node ID must be a string");
        
        var start = startValue.AsString();
        
        if (!_nodes.ContainsKey(start))
            throw new RuntimeException($"Node '{start}' does not exist");
        
        if (arguments.Count == 2)
        {
            // DFS to find path to target
            var targetValue = arguments[1];
            if (targetValue.Type != ValueType.String)
                throw new RuntimeException("dfs() target node ID must be a string");
            
            var target = targetValue.AsString();
            var path = DfsPath(start, target);
            
            var result = new DictionaryInstance();
            result.SetEntry("path", RuntimeValue.Array(path.Select(RuntimeValue.String).ToList()));
            result.SetEntry("found", RuntimeValue.Boolean(path.Count > 0));
            return RuntimeValue.Object(result);
        }
        else
        {
            // DFS to get all reachable nodes
            var visited = DfsTraversal(start);
            return RuntimeValue.Array(visited.Select(RuntimeValue.String).ToList());
        }
    }
    
    private RuntimeValue CallShortestPath(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("shortestPath() expects 2 arguments (from, to)");
        
        var fromValue = arguments[0];
        var toValue = arguments[1];
        
        if (fromValue.Type != ValueType.String || toValue.Type != ValueType.String)
            throw new RuntimeException("shortestPath() node IDs must be strings");
        
        var from = fromValue.AsString();
        var to = toValue.AsString();
        
        if (!_nodes.ContainsKey(from))
            throw new RuntimeException($"Node '{from}' does not exist");
        if (!_nodes.ContainsKey(to))
            throw new RuntimeException($"Node '{to}' does not exist");
        
        var (path, distance) = Dijkstra(from, to);
        
        var result = new DictionaryInstance();
        result.SetEntry("path", RuntimeValue.Array(path.Select(RuntimeValue.String).ToList()));
        result.SetEntry("distance", RuntimeValue.Float(distance));
        result.SetEntry("found", RuntimeValue.Boolean(path.Count > 0));
        return RuntimeValue.Object(result);
    }
    
    private RuntimeValue CallTopologicalSort(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("topologicalSort() expects 0 arguments");
        
        if (!_isDirected)
            throw new RuntimeException("topologicalSort() only works on directed graphs");
        
        var sorted = TopologicalSort();
        
        var result = new DictionaryInstance();
        result.SetEntry("order", RuntimeValue.Array(sorted.Select(RuntimeValue.String).ToList()));
        result.SetEntry("valid", RuntimeValue.Boolean(sorted.Count == _nodes.Count));
        return RuntimeValue.Object(result);
    }
    
    private RuntimeValue CallConnectedComponents(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("connectedComponents() expects 0 arguments");
        
        var components = FindConnectedComponents();
        
        var resultArray = new List<RuntimeValue>();
        foreach (var component in components)
        {
            resultArray.Add(RuntimeValue.Array(component.Select(RuntimeValue.String).ToList()));
        }
        
        return RuntimeValue.Array(resultArray);
    }
    
    private RuntimeValue CallIsCyclic(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("isCyclic() expects 0 arguments");
        
        return RuntimeValue.Boolean(IsCyclic());
    }
    
    private RuntimeValue CallMinimumSpanningTree(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("minimumSpanningTree() expects 0 arguments");
        
        if (_isDirected)
            throw new RuntimeException("minimumSpanningTree() only works on undirected graphs");
        
        var mst = KruskalMST();
        
        var result = new DictionaryInstance();
        var edgesArray = new List<RuntimeValue>();
        foreach (var edge in mst)
        {
            var edgeArray = new List<RuntimeValue>
            {
                RuntimeValue.String(edge.SourceId),
                RuntimeValue.String(edge.TargetId),
                RuntimeValue.Float(edge.Weight)
            };
            edgesArray.Add(RuntimeValue.Array(edgeArray));
        }
        result.SetEntry("edges", RuntimeValue.Array(edgesArray));
        
        var totalWeight = mst.Sum(e => e.Weight);
        result.SetEntry("totalWeight", RuntimeValue.Float(totalWeight));
        
        return RuntimeValue.Object(result);
    }
    
    // Algorithm Implementations
    
    private List<string> BfsTraversal(string start)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(start);
        visited.Add(start);
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            
            if (_nodes.TryGetValue(current, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    if (!visited.Contains(edge.TargetId))
                    {
                        visited.Add(edge.TargetId);
                        queue.Enqueue(edge.TargetId);
                    }
                }
            }
        }
        
        return visited.ToList();
    }
    
    private List<string> BfsPath(string start, string target)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        var parent = new Dictionary<string, string>();
        
        queue.Enqueue(start);
        visited.Add(start);
        parent[start] = start;
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            
            if (current == target)
            {
                // Reconstruct path
                var path = new List<string>();
                var pathNode = target;
                while (pathNode != start)
                {
                    path.Add(pathNode);
                    pathNode = parent[pathNode];
                }
                path.Add(start);
                path.Reverse();
                return path;
            }
            
            if (_nodes.TryGetValue(current, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    if (!visited.Contains(edge.TargetId))
                    {
                        visited.Add(edge.TargetId);
                        parent[edge.TargetId] = current;
                        queue.Enqueue(edge.TargetId);
                    }
                }
            }
        }
        
        return new List<string>(); // No path found
    }
    
    private List<string> DfsTraversal(string start)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(start);
        
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            
            if (!visited.Contains(current))
            {
                visited.Add(current);
                
                if (_nodes.TryGetValue(current, out var node))
                {
                    foreach (var edge in node.Edges)
                    {
                        if (!visited.Contains(edge.TargetId))
                        {
                            stack.Push(edge.TargetId);
                        }
                    }
                }
            }
        }
        
        return visited.ToList();
    }
    
    private List<string> DfsPath(string start, string target)
    {
        var visited = new HashSet<string>();
        var path = new List<string>();
        
        bool DfsHelper(string current)
        {
            if (current == target)
            {
                path.Add(current);
                return true;
            }
            
            visited.Add(current);
            
            if (_nodes.TryGetValue(current, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    if (!visited.Contains(edge.TargetId))
                    {
                        if (DfsHelper(edge.TargetId))
                        {
                            path.Add(current);
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }
        
        if (DfsHelper(start))
        {
            path.Reverse();
        }
        
        return path;
    }
    
    private (List<string> path, double distance) Dijkstra(string start, string target)
    {
        var distances = new Dictionary<string, double>();
        var previous = new Dictionary<string, string>();
        var unvisited = new HashSet<string>();
        
        foreach (var nodeId in _nodes.Keys)
        {
            distances[nodeId] = double.MaxValue;
            unvisited.Add(nodeId);
        }
        
        distances[start] = 0;
        
        while (unvisited.Count > 0)
        {
            // Find unvisited node with minimum distance
            string? current = null;
            double minDist = double.MaxValue;
            
            foreach (var nodeId in unvisited)
            {
                if (distances[nodeId] < minDist)
                {
                    minDist = distances[nodeId];
                    current = nodeId;
                }
            }
            
            if (current == null || minDist == double.MaxValue)
                break;
            
            unvisited.Remove(current);
            
            if (current == target)
                break;
            
            if (_nodes.TryGetValue(current, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    var alt = distances[current] + edge.Weight;
                    if (alt < distances[edge.TargetId])
                    {
                        distances[edge.TargetId] = alt;
                        previous[edge.TargetId] = current;
                    }
                }
            }
        }
        
        // Reconstruct path
        var path = new List<string>();
        if (distances[target] != double.MaxValue)
        {
            var node = target;
            while (node != start)
            {
                path.Add(node);
                if (!previous.TryGetValue(node, out var prev))
                    break;
                node = prev;
            }
            path.Add(start);
            path.Reverse();
        }
        
        return (path, distances[target] == double.MaxValue ? -1 : distances[target]);
    }
    
    private List<string> TopologicalSort()
    {
        var inDegree = new Dictionary<string, int>();
        foreach (var nodeId in _nodes.Keys)
        {
            inDegree[nodeId] = 0;
        }
        
        // Calculate in-degrees
        foreach (var node in _nodes.Values)
        {
            foreach (var edge in node.Edges)
            {
                inDegree[edge.TargetId]++;
            }
        }
        
        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                queue.Enqueue(kvp.Key);
        }
        
        var result = new List<string>();
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            
            if (_nodes.TryGetValue(current, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    inDegree[edge.TargetId]--;
                    if (inDegree[edge.TargetId] == 0)
                        queue.Enqueue(edge.TargetId);
                }
            }
        }
        
        return result;
    }
    
    private List<List<string>> FindConnectedComponents()
    {
        var visited = new HashSet<string>();
        var components = new List<List<string>>();
        
        foreach (var nodeId in _nodes.Keys)
        {
            if (!visited.Contains(nodeId))
            {
                var component = BfsTraversal(nodeId);
                foreach (var node in component)
                {
                    visited.Add(node);
                }
                components.Add(component);
            }
        }
        
        return components;
    }
    
    private bool IsCyclic()
    {
        var visited = new HashSet<string>();
        var recStack = new HashSet<string>();
        
        bool DfsCyclic(string nodeId)
        {
            visited.Add(nodeId);
            recStack.Add(nodeId);
            
            if (_nodes.TryGetValue(nodeId, out var node))
            {
                foreach (var edge in node.Edges)
                {
                    if (!visited.Contains(edge.TargetId))
                    {
                        if (DfsCyclic(edge.TargetId))
                            return true;
                    }
                    else if (recStack.Contains(edge.TargetId))
                    {
                        return true;
                    }
                }
            }
            
            recStack.Remove(nodeId);
            return false;
        }
        
        foreach (var nodeId in _nodes.Keys)
        {
            if (!visited.Contains(nodeId))
            {
                if (DfsCyclic(nodeId))
                    return true;
            }
        }
        
        return false;
    }
    
    private List<GraphEdge> KruskalMST()
    {
        // Collect all unique edges (for undirected graphs)
        var allEdges = new List<GraphEdge>();
        var edgeSet = new HashSet<string>();
        
        foreach (var node in _nodes.Values)
        {
            foreach (var edge in node.Edges)
            {
                var edgeKey = edge.SourceId.CompareTo(edge.TargetId) < 0 
                    ? $"{edge.SourceId}:{edge.TargetId}"
                    : $"{edge.TargetId}:{edge.SourceId}";
                
                if (!edgeSet.Contains(edgeKey))
                {
                    edgeSet.Add(edgeKey);
                    allEdges.Add(edge);
                }
            }
        }
        
        // Sort edges by weight
        allEdges.Sort((a, b) => a.Weight.CompareTo(b.Weight));
        
        // Union-Find data structure
        var parent = new Dictionary<string, string>();
        var rank = new Dictionary<string, int>();
        
        foreach (var nodeId in _nodes.Keys)
        {
            parent[nodeId] = nodeId;
            rank[nodeId] = 0;
        }
        
        string Find(string x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }
        
        void Union(string x, string y)
        {
            var rootX = Find(x);
            var rootY = Find(y);
            
            if (rootX == rootY)
                return;
            
            if (rank[rootX] < rank[rootY])
                parent[rootX] = rootY;
            else if (rank[rootX] > rank[rootY])
                parent[rootY] = rootX;
            else
            {
                parent[rootY] = rootX;
                rank[rootX]++;
            }
        }
        
        var mst = new List<GraphEdge>();
        
        foreach (var edge in allEdges)
        {
            if (Find(edge.SourceId) != Find(edge.TargetId))
            {
                mst.Add(edge);
                Union(edge.SourceId, edge.TargetId);
            }
        }
        
        return mst;
    }
    
    // Serialization and Deserialization
    
    private RuntimeValue CallSerialize(List<RuntimeValue> arguments)
    {
        if (arguments.Count > 1)
            throw new RuntimeException("serialize() expects 0 or 1 argument (filePath?)");
        
        var json = SerializeToJson();
        
        if (arguments.Count == 1)
        {
            var filePathValue = arguments[0];
            if (filePathValue.Type != ValueType.String)
                throw new RuntimeException("serialize() file path must be a string");
            
            var filePath = filePathValue.AsString();
            try
            {
                File.WriteAllText(filePath, json);
                return RuntimeValue.String(filePath);
            }
            catch (Exception ex)
            {
                throw new RuntimeException($"Failed to write graph to file: {ex.Message}");
            }
        }
        
        return RuntimeValue.String(json);
    }
    
    private RuntimeValue CallDeserialize(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("deserialize() expects 1 argument (jsonString or filePath)");
        
        var inputValue = arguments[0];
        if (inputValue.Type != ValueType.String)
            throw new RuntimeException("deserialize() argument must be a string");
        
        var input = inputValue.AsString();
        string json;
        
        // Check if it's a file path (contains path separators or looks like a file path)
        if (input.Contains(Path.DirectorySeparatorChar) || input.Contains(Path.AltDirectorySeparatorChar) || 
            (input.Length > 0 && !input.TrimStart().StartsWith("{")))
        {
            try
            {
                if (!File.Exists(input))
                    throw new RuntimeException($"File not found: {input}");
                json = File.ReadAllText(input);
            }
            catch (Exception ex)
            {
                throw new RuntimeException($"Failed to read graph from file: {ex.Message}");
            }
        }
        else
        {
            json = input;
        }
        
        try
        {
            var graph = DeserializeFromJson(json);
            return RuntimeValue.Object(graph);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Failed to deserialize graph: {ex.Message}");
        }
    }
    
    private string SerializeToJson()
    {
        var nodesArray = new List<string>();
        var edgesArray = new List<string>();
        
        // Serialize nodes (each node stored once)
        foreach (var node in _nodes.Values)
        {
            var nodeDataJson = node.Data != null ? SerializeRuntimeValue(node.Data) : "null";
            var nodeJson = $"{{\"id\":{JsonSerializer.Serialize(node.Id)},\"data\":{nodeDataJson}}}";
            nodesArray.Add(nodeJson);
        }
        
        // Serialize edges (for undirected graphs, only serialize each edge once)
        var edgeSet = new HashSet<string>();
        foreach (var node in _nodes.Values)
        {
            foreach (var edge in node.Edges)
            {
                // For undirected graphs, only include each edge once
                if (_isDirected || edge.SourceId.CompareTo(edge.TargetId) <= 0)
                {
                    var edgeKey = $"{edge.SourceId}:{edge.TargetId}";
                    if (!edgeSet.Contains(edgeKey))
                    {
                        edgeSet.Add(edgeKey);
                        
                        var propertiesJson = edge.Properties != null 
                            ? SerializeDictionaryInstance(edge.Properties) 
                            : "null";
                        var edgeJson = $"{{\"from\":{JsonSerializer.Serialize(edge.SourceId)},\"to\":{JsonSerializer.Serialize(edge.TargetId)},\"weight\":{edge.Weight.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},\"properties\":{propertiesJson}}}";
                        edgesArray.Add(edgeJson);
                    }
                }
            }
        }
        
        var result = $"{{\"isDirected\":{(_isDirected ? "true" : "false")},\"nodes\":[{string.Join(",", nodesArray)}],\"edges\":[{string.Join(",", edgesArray)}]}}";
        return result;
    }
    
    private static GraphInstance DeserializeFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.ValueKind != JsonValueKind.Object)
                throw new RuntimeException("Invalid graph JSON: expected object");
            
            // Parse isDirected
            if (!root.TryGetProperty("isDirected", out var isDirectedProp))
                throw new RuntimeException("Invalid graph JSON: missing 'isDirected' property");
            var isDirected = isDirectedProp.GetBoolean();
            
            // Parse nodes
            if (!root.TryGetProperty("nodes", out var nodesProp) || nodesProp.ValueKind != JsonValueKind.Array)
                throw new RuntimeException("Invalid graph JSON: missing or invalid 'nodes' array");
            
            var nodes = new Dictionary<string, GraphNode>();
            var nodeMap = new Dictionary<string, GraphNode>();
            
            foreach (var nodeElement in nodesProp.EnumerateArray())
            {
                if (nodeElement.ValueKind != JsonValueKind.Object)
                    throw new RuntimeException("Invalid graph JSON: node must be an object");
                
                if (!nodeElement.TryGetProperty("id", out var idProp))
                    throw new RuntimeException("Invalid graph JSON: node missing 'id' property");
                var id = idProp.GetString();
                if (string.IsNullOrEmpty(id))
                    throw new RuntimeException("Invalid graph JSON: node 'id' cannot be empty");
                
                RuntimeValue? data = null;
                if (nodeElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind != JsonValueKind.Null)
                {
                    data = DeserializeRuntimeValue(dataProp);
                }
                
                var node = new GraphNode(id, data);
                nodes[id] = node;
                nodeMap[id] = node;
            }
            
            // Parse edges
            if (!root.TryGetProperty("edges", out var edgesProp) || edgesProp.ValueKind != JsonValueKind.Array)
                throw new RuntimeException("Invalid graph JSON: missing or invalid 'edges' array");
            
            int edgeCount = 0;
            foreach (var edgeElement in edgesProp.EnumerateArray())
            {
                if (edgeElement.ValueKind != JsonValueKind.Object)
                    throw new RuntimeException("Invalid graph JSON: edge must be an object");
                
                if (!edgeElement.TryGetProperty("from", out var fromProp))
                    throw new RuntimeException("Invalid graph JSON: edge missing 'from' property");
                var from = fromProp.GetString();
                if (string.IsNullOrEmpty(from))
                    throw new RuntimeException("Invalid graph JSON: edge 'from' cannot be empty");
                
                if (!edgeElement.TryGetProperty("to", out var toProp))
                    throw new RuntimeException("Invalid graph JSON: edge missing 'to' property");
                var to = toProp.GetString();
                if (string.IsNullOrEmpty(to))
                    throw new RuntimeException("Invalid graph JSON: edge 'to' cannot be empty");
                
                if (!nodeMap.ContainsKey(from))
                    throw new RuntimeException($"Invalid graph JSON: edge references unknown node '{from}'");
                if (!nodeMap.ContainsKey(to))
                    throw new RuntimeException($"Invalid graph JSON: edge references unknown node '{to}'");
                
                double weight = 1.0;
                if (edgeElement.TryGetProperty("weight", out var weightProp))
                {
                    if (weightProp.ValueKind == JsonValueKind.Number)
                        weight = weightProp.GetDouble();
                }
                
                DictionaryInstance? properties = null;
                if (edgeElement.TryGetProperty("properties", out var propertiesProp) && 
                    propertiesProp.ValueKind != JsonValueKind.Null)
                {
                    if (propertiesProp.ValueKind == JsonValueKind.Object)
                    {
                        properties = DeserializeDictionaryInstance(propertiesProp);
                    }
                }
                
                var edge = new GraphEdge(from, to, weight, properties);
                nodes[from].AddEdge(edge);
                edgeCount++;
                
                // For undirected graphs, add reverse edge
                if (!isDirected)
                {
                    var reverseEdge = new GraphEdge(to, from, weight, properties);
                    nodes[to].AddEdge(reverseEdge);
                }
            }
            
            return new GraphInstance(isDirected, nodes, edgeCount);
        }
        catch (JsonException ex)
        {
            throw new RuntimeException($"Invalid JSON format: {ex.Message}");
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
                // For other ObjectInstance types, return empty object
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
    
    private static RuntimeValue DeserializeRuntimeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return RuntimeValue.Object(DeserializeDictionaryInstance(element));
            
            case JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(DeserializeRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
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
            
            default:
                return RuntimeValue.Null();
        }
    }
    
    private static DictionaryInstance DeserializeDictionaryInstance(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new RuntimeException("Expected object for dictionary deserialization");
        
        var dict = new DictionaryInstance();
        foreach (var prop in element.EnumerateObject())
        {
            var value = DeserializeRuntimeValue(prop.Value);
            dict.SetEntry(prop.Name, value);
        }
        return dict;
    }
}
