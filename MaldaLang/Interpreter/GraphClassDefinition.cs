// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class GraphClassDefinition : ClassDefinition
{
    public static GraphClassDefinition Instance { get; } = new GraphClassDefinition();
    
    private GraphClassDefinition() : base("Graph", null)
    {
        // Graphs expose behavior only via instance methods.
    }
}
