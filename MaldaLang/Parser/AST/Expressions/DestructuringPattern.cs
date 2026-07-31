// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public abstract class DestructuringPattern : Pattern
{
    protected DestructuringPattern(int line = 0, int column = 0) : base(line, column) { }
}

public class ArrayDestructuringPattern : DestructuringPattern
{
    public List<Pattern> Elements { get; }
    public RestPattern? Rest { get; }
    
    public ArrayDestructuringPattern(List<Pattern> elements, RestPattern? rest = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Elements = elements;
        Rest = rest;
    }
}

public class ObjectDestructuringPattern : DestructuringPattern
{
    public List<ObjectPatternProperty> Properties { get; }
    
    public ObjectDestructuringPattern(List<ObjectPatternProperty> properties, int line = 0, int column = 0)
        : base(line, column)
    {
        Properties = properties;
    }
}
