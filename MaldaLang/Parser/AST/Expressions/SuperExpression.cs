// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class SuperExpression : Expression
{
    public SuperExpression(int line = 0, int column = 0) : base(line, column) { }
}