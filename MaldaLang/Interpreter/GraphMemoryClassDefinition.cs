// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class GraphMemoryClassDefinition : ClassDefinition
{
    public static GraphMemoryClassDefinition Instance { get; } = new GraphMemoryClassDefinition();
    
    private GraphMemoryClassDefinition() : base("GraphMemory", null)
    {
        // GraphMemory exposes behavior only via instance methods.
    }
}
