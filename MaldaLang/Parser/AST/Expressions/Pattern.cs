// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using MaldaLang.Parser.AST;

public abstract class Pattern : Node
{
    protected Pattern(int line = 0, int column = 0) : base(line, column) { }
}
