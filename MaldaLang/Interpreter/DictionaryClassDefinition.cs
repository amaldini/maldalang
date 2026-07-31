// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class DictionaryClassDefinition : ClassDefinition
{
    public static DictionaryClassDefinition Instance { get; } = new DictionaryClassDefinition();
    
    private DictionaryClassDefinition() : base("Dictionary", null)
    {
        // Dictionaries currently expose behavior only via instance methods.
    }
}

