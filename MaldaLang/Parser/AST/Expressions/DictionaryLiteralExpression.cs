// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class DictionaryLiteralExpression : Expression
{
    public List<(Expression Key, Expression Value)> Entries { get; }
    
    public DictionaryLiteralExpression(List<(Expression Key, Expression Value)> entries, int line = 0, int column = 0)
        : base(line, column)
    {
        Entries = entries;
    }
}

