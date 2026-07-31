// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

public class ContinueStatement : Statement
{
    public ContinueStatement(int line = 0, int column = 0) : base(line, column) { }
}