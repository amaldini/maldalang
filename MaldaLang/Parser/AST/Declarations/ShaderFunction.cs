// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

/// <summary>
/// Marks MALDA functions that the JavaScript backend may compile to GLSL.
/// Host code never calls these; <c>glsl.compile({ ... })</c> inlines them as shader strings.
/// </summary>
public static class ShaderFunction
{
    public const string DecoratorName = "shader";

    public static bool IsMarked(FunctionDeclaration function)
    {
        foreach (var decorator in function.Decorators)
        {
            if (string.Equals(decorator.Name, DecoratorName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
