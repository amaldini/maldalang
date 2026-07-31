// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using System.Collections.Generic;

public class ObjectPatternProperty
{
    public string Key { get; }
    public Pattern? Pattern { get; }  // null means shorthand: { key } is same as { key: key }
    public string? BindingName { get; }  // If pattern is null, this is the binding name
    
    public ObjectPatternProperty(string key, Pattern? pattern = null, string? bindingName = null)
    {
        Key = key;
        Pattern = pattern;
        BindingName = bindingName;
    }
}

public class ObjectPattern : Pattern
{
    public List<ObjectPatternProperty> Properties { get; }
    
    public ObjectPattern(List<ObjectPatternProperty> properties, int line = 0, int column = 0)
        : base(line, column)
    {
        Properties = properties;
    }
}
