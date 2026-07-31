// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class GraphEdge
{
    public string SourceId { get; }
    public string TargetId { get; }
    public double Weight { get; set; }
    public DictionaryInstance? Properties { get; set; }
    
    public GraphEdge(string sourceId, string targetId, double weight = 1.0, DictionaryInstance? properties = null)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Weight = weight;
        Properties = properties;
    }
}
