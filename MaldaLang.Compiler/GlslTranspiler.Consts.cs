// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// A top-level MALDA <c>const</c> with a literal value, as an <c>@shader()</c> function sees it:
/// one declaration that serves the host and the shader.
/// </summary>
/// <param name="Name">The const name.</param>
/// <param name="Type"><c>int</c>, <c>float</c> or <c>bool</c>, from the literal (or a <c>float</c> hint on an int literal).</param>
/// <param name="GlslValue">The value as GLSL source.</param>
public sealed record GlslLiteralConst(string Name, GlslType Type, string GlslValue)
{
    /// <summary>
    /// Reads <c>const NAME = literal;</c>. The initializer must be an int, float or bool literal,
    /// with an optional leading <c>-</c>; anything computed is not a kernel const.
    /// </summary>
    public static GlslLiteralConst? TryRead(VarDeclStatement declaration)
    {
        if (!declaration.IsConst)
            return null;

        var negative = false;
        var initializer = declaration.Initializer;
        if (initializer is UnaryExpression { Operator: TokenType.Minus, Right: LiteralExpression inner })
        {
            negative = true;
            initializer = inner;
        }

        if (initializer is not LiteralExpression literal)
            return null;

        var floatHint = string.Equals(declaration.TypeHint?.Trim(), "float", StringComparison.Ordinal);
        var sign = negative ? "-" : string.Empty;
        switch (literal.Value)
        {
            case bool value when !negative:
                return new GlslLiteralConst(declaration.Name, GlslType.Bool, value ? "true" : "false");
            case int or long:
            {
                var digits = Convert.ToInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                return floatHint
                    ? new GlslLiteralConst(declaration.Name, GlslType.Float, sign + digits + ".0")
                    : new GlslLiteralConst(declaration.Name, GlslType.Int, sign + digits);
            }
            case float or double or decimal:
                return new GlslLiteralConst(
                    declaration.Name,
                    GlslType.Float,
                    sign + GlslTranspiler.FormatFloatLiteral((IFormattable)literal.Value));
            default:
                return null;
        }
    }
}

public static partial class GlslTranspiler
{
    private static readonly Regex DeclarationWords = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    /// <summary>The shortest text that reads back as the same number, with a decimal point (2.2, not 2.2000000000000002).</summary>
    internal static string FormatFloatLiteral(IFormattable value)
    {
        var text = Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    /// <summary>
    /// The name a <c>glsl.compile</c> declaration string introduces: the second word of
    /// <c>"vec3 uCars[CAR_COUNT]"</c> or <c>"float EPSILON = 0.0015"</c>, the third when the string
    /// repeats its qualifier (<c>"uniform vec3 uCamPos"</c>). Null when the string has no name.
    /// </summary>
    public static string? DeclaredName(string declaration)
    {
        var words = DeclarationWords.Matches(declaration);
        var index = words.Count > 0 && words[0].Value is "uniform" or "varying" or "const" or "in" or "out" ? 2 : 1;
        // "highp float x": a precision qualifier comes before the type.
        if (words.Count > index - 1 && words[index - 1].Value is "highp" or "mediump" or "lowp")
            index++;
        return words.Count > index ? words[index].Value : null;
    }

    /// <summary>
    /// The MALDA consts the compiled functions name, in declaration order, without those the
    /// request already declares itself (a <c>consts</c> / <c>uniforms</c> / <c>varyings</c> string
    /// of that name, or a raw header that mentions it) and those a function shadows with a
    /// parameter or a local.
    /// </summary>
    private static List<GlslLiteralConst> UsedMaldaConsts(GlslCompileRequest request)
    {
        var used = new List<GlslLiteralConst>();
        if (request.MaldaConsts.Count == 0)
            return used;

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in request.Consts.Concat(request.Uniforms).Concat(request.Varyings))
        {
            if (DeclaredName(declaration) is { } name)
                declared.Add(name);
        }

        if (!string.IsNullOrEmpty(request.Header))
        {
            foreach (Match word in DeclarationWords.Matches(request.Header))
                declared.Add(word.Value);
        }

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in request.Functions)
        {
            var nodes = AstNodeWalker.Descendants(function.Body).ToList();
            var locals = new HashSet<string>(function.Parameters, StringComparer.Ordinal);
            foreach (var local in nodes.OfType<VarDeclStatement>())
                locals.Add(local.Name);
            foreach (var identifier in nodes.OfType<IdentifierExpression>())
            {
                if (!locals.Contains(identifier.Name))
                    named.Add(identifier.Name);
            }
        }

        foreach (var constant in request.MaldaConsts)
        {
            if (named.Contains(constant.Name) && !declared.Contains(constant.Name))
                used.Add(constant);
        }

        return used;
    }

    private static void AppendMaldaConsts(StringBuilder output, GlslCompileRequest request)
    {
        foreach (var constant in UsedMaldaConsts(request))
            output.AppendLine($"const {constant.Type} {constant.Name} = {constant.GlslValue};");
    }
}
