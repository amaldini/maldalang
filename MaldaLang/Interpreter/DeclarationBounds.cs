// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
using MaldaLang.IDE;
using MaldaLang.Parser.AST.Declarations;

internal static class DeclarationBounds
{
    public static int? TryGetWithinTimeoutMs(FunctionDeclaration? declaration) =>
        TryGetWithinTimeoutMs(declaration?.Decorators);

    public static int? TryGetWithinTimeoutMs(PromptDeclaration? declaration) =>
        TryGetWithinTimeoutMs(declaration?.Decorators);

    public static int? TryGetWithinTimeoutMs(IReadOnlyList<Decorator>? decorators)
    {
        if (decorators == null || decorators.Count == 0)
            return null;

        var decorator = DecoratorArgs.FindDecoratorFromList(decorators, "within");
        if (decorator == null)
            return null;

        return DecoratorArgs.TryReadPositiveIntArgument(decorator, out var ms) ? ms : null;
    }
}
