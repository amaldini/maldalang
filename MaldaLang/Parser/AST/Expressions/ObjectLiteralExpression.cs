// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class ObjectLiteralExpression : Expression
{
    public List<(Expression Key, Expression Value)> Properties { get; }
    
    public ObjectLiteralExpression(List<(Expression Key, Expression Value)> properties, int line = 0, int column = 0)
        : base(line, column)
    {
        Properties = properties;
    }
}