// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class ArrayClassDefinition : ClassDefinition
{
    public static ArrayClassDefinition Instance { get; } = new ArrayClassDefinition();
    
    private ArrayClassDefinition() : base("Array", null)
    {
        // Arrays do not currently expose any static fields or methods via the class
        // instance; all behavior is provided through instance methods and properties.
    }
}
