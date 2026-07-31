// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

internal interface IOptionalPackTranspileEmitter
{
    bool CanEmit(string name);
    void Emit(OptionalPackEmitContext ctx, string name, List<Expression> arguments);
}
