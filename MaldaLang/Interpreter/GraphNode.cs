// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class GraphNode
{
    public string Id { get; }
    public RuntimeValue? Data { get; set; }
    public List<GraphEdge> Edges { get; }
    
    public GraphNode(string id, RuntimeValue? data = null)
    {
        Id = id;
        Data = data;
        Edges = new List<GraphEdge>();
    }
    
    public void AddEdge(GraphEdge edge)
    {
        Edges.Add(edge);
    }
    
    public bool RemoveEdge(string targetId)
    {
        return Edges.RemoveAll(e => e.TargetId == targetId) > 0;
    }
    
    public List<string> GetNeighborIds()
    {
        return Edges.Select(e => e.TargetId).ToList();
    }
}
