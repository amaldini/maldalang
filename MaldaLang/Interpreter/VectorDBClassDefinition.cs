// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class VectorDBClassDefinition : ClassDefinition
{
    public static VectorDBClassDefinition Instance { get; } = new VectorDBClassDefinition();
    
    private VectorDBClassDefinition() : base("VectorDB", null)
    {
        // VectorDB exposes behavior only via instance methods.
    }
}
