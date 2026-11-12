// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// Rejects, in an <c>@shader()</c> function that host code calls, what exists only on the GPU:
/// <c>gl_*</c> names, varyings, <c>discard</c>, screen-space derivatives, texture lookups,
/// <c>out</c> / <c>inout</c> parameters and <c>mat*</c> types. Uniforms and consts are rejected too
/// until they can be bound to a host call. The error names the function and the construct; the
/// same function is still valid GLSL when only a shader uses it, since this runs for host-reached
/// functions alone.
/// </summary>
internal static class GlslHostDiagnostics
{
    private static readonly HashSet<string> Derivatives = new(StringComparer.Ordinal) { "dFdx", "dFdy", "fwidth" };

    private static readonly HashSet<string> FilteredTextureCalls = new(StringComparer.Ordinal)
    {
        "texture", "texture2D", "textureLod", "textureGrad", "textureProj", "textureCube", "textureOffset"
    };

    /// <param name="function">A shader function reached from host code.</param>
    /// <param name="shaderGlobals">Names declared to <c>glsl.compile</c> in the file, to their kind (<c>varying</c>, <c>uniform</c>, <c>const</c>).</param>
    /// <param name="reachedThrough">The host-called function that calls this one, or null when the host calls it directly.</param>
    /// <exception cref="NotSupportedException">The first construct, in source order, with no host meaning.</exception>
    public static void Check(
        FunctionDeclaration function,
        IReadOnlyDictionary<string, string> shaderGlobals,
        string? reachedThrough = null,
        IReadOnlyDictionary<string, GlslLiteralConst>? maldaConsts = null,
        IReadOnlyDictionary<string, string>? constStringValues = null,
        IReadOnlyDictionary<string, IReadOnlyList<GlslType>>? boundUniforms = null)
    {
        // Non-null for a function reached through glsl.host: it may read the declared uniforms.
        var bound = boundUniforms != null;
        var found = new List<(Node At, string Message)>();
        var known = new HashSet<string>(function.Parameters, StringComparer.Ordinal);

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var name = function.Parameters[i];
            var hint = function.ParameterTypeHints != null && i < function.ParameterTypeHints.Count
                ? function.ParameterTypeHints[i]?.Trim() ?? string.Empty
                : string.Empty;
            foreach (var qualifier in new[] { "inout", "out" })
            {
                if (!hint.StartsWith(qualifier + " ", StringComparison.Ordinal))
                    continue;
                found.Add((function,
                    $"'{qualifier}' parameter '{name}' has no host meaning: a function called from host code gives its result back with return."));
                hint = hint[qualifier.Length..].Trim();
                break;
            }

            AddTypeProblem(found, function, hint, $"parameter '{name}'");
        }

        AddTypeProblem(found, function, function.ReturnType, "the return value");

        var nodes = AstNodeWalker.Descendants(function.Body).ToList();
        var callees = new HashSet<Expression>(ReferenceEqualityComparer.Instance);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case VarDeclStatement varDecl:
                    known.Add(varDecl.Name);
                    AddTypeProblem(found, varDecl, varDecl.TypeHint, $"local '{varDecl.Name}'");
                    break;
                case FunctionCallExpression { Callee: IdentifierExpression callee } call:
                    callees.Add(callee);
                    if (AddCallProblem(found, callee, callee.Name, function.Name, bound))
                    {
                        // The call is the problem, not the sampler or uniform passed to it.
                        foreach (var argument in AstNodeWalker.Descendants(call).OfType<IdentifierExpression>())
                            callees.Add(argument);
                    }
                    break;
                case FunctionCallExpression { Callee: MemberAccessExpression { Object: IdentifierExpression module } }:
                    // math.sqrt(...): `math` is not a value.
                    callees.Add(module);
                    break;
            }
        }

        foreach (var identifier in nodes.OfType<IdentifierExpression>())
        {
            if (callees.Contains(identifier) || known.Contains(identifier.Name))
                continue;

            var name = identifier.Name;
            if (maldaConsts != null && maldaConsts.TryGetValue(name, out var constant))
            {
                // One declaration serves host and shader. A consts string of the same name wins in
                // the GLSL, so it must say the same value or the two sides disagree.
                if (constStringValues != null && constStringValues.TryGetValue(name, out var stringValue) &&
                    !SameConstValue(stringValue, constant.GlslValue))
                {
                    found.Add((identifier,
                        $"const '{name}' is {stringValue} in a glsl.compile consts string and {constant.GlslValue} as a MALDA const: the shader and the host would disagree. Remove the string."));
                }

                continue;
            }

            if (name == "discard")
                found.Add((identifier, "discard has no host meaning: there is no fragment to drop."));
            else if (name.StartsWith("gl_", StringComparison.Ordinal))
                found.Add((identifier, $"{name} has no host meaning: it exists only while the GPU shades a vertex or a fragment."));
            else if (shaderGlobals.TryGetValue(name, out var kind) && kind == "varying")
                found.Add((identifier, $"varying '{name}' has no host meaning: it is interpolated by the GPU for each fragment."));
            else if (shaderGlobals.TryGetValue(name, out kind) && kind == "const")
                found.Add((identifier, $"const '{name}' is declared only in a glsl.compile consts string, which the host cannot read: declare it as a top-level MALDA const (const {name} = ...;) and remove the string."));
            else if (shaderGlobals.TryGetValue(name, out kind) && bound)
            {
                // A uniform: its type comes from the glsl.compile strings of the file.
                if (!boundUniforms!.TryGetValue(name, out var declaredTypes))
                    found.Add((identifier, $"uniform '{name}' has a declaration glsl.host cannot read; write it as \"type {name}\" in the uniforms of glsl.compile."));
                else if (declaredTypes.Count > 1)
                    found.Add((identifier, $"uniform '{name}' is declared as {string.Join(" and as ", declaredTypes)} in the glsl.compile calls of this file: a bound function needs one type."));
            }
            else if (shaderGlobals.TryGetValue(name, out kind))
                found.Add((identifier, $"{kind} '{name}' has no host value when the function is called by name: it can only use its parameters, its locals and top-level MALDA consts. Bind it to a material with glsl.host(\"{function.Name}\", material), or pass the value as a parameter."));
            else if (bound)
                found.Add((identifier, $"'{name}' is not a parameter, a local, a top-level literal MALDA const or a uniform declared to glsl.compile in this file."));
            else
                found.Add((identifier, $"'{name}' is not a parameter, a local or a top-level literal MALDA const: a uniform or varying has no host value. Pass the value as a parameter."));
        }

        if (found.Count == 0)
            return;

        var first = found.OrderBy(problem => problem.At.Line).ThenBy(problem => problem.At.Column).First();
        throw Error(function, first.At, first.Message, reachedThrough);
    }

    /// <summary>The one phrasing for every host-call error: the function, how the host reaches it, where, and what.</summary>
    public static NotSupportedException Error(FunctionDeclaration function, Node at, string message, string? reachedThrough = null)
    {
        var how = reachedThrough == null
            ? "is called from host code"
            : $"is reached from host code through '{reachedThrough}'";
        return new NotSupportedException($"@shader() function '{function.Name}' {how} ({at.Line}:{at.Column}): {message}");
    }

    private static bool SameConstValue(string stringValue, string maldaValue)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var style = System.Globalization.NumberStyles.Float;
        if (double.TryParse(stringValue, style, culture, out var left) && double.TryParse(maldaValue, style, culture, out var right))
            return left == right;
        // An expression (`2.0 * PI`) or a constructor cannot be compared here: not reported.
        return stringValue == maldaValue || stringValue is not ("true" or "false");
    }

    private static void AddTypeProblem(List<(Node, string)> found, Node at, string? hint, string what)
    {
        if (!GlslType.TryParse(hint, out var type) || type.Base != GlslBaseType.Opaque)
            return;

        var name = type.OpaqueName ?? string.Empty;
        if (name.StartsWith("mat", StringComparison.Ordinal))
            found.Add((at, $"type {type} of {what} is not supported on the host: mat* types are GLSL-only in this version."));
        else if (name.Contains("sampler", StringComparison.Ordinal))
            found.Add((at, $"type {type} of {what} has no host value: a texture lives on the GPU."));
        else
            found.Add((at, $"type {type} of {what} has no host value."));
    }

    private static bool AddCallProblem(List<(Node, string)> found, Node at, string name, string functionName, bool bound)
    {
        // A bound function reads a data texture of its material; the type pass checks the arguments.
        if (bound && name == "texelFetch")
            return false;

        string? message = null;
        if (Derivatives.Contains(name))
            message = $"{name}() has no host meaning: screen-space derivatives exist only in a fragment shader.";
        else if (FilteredTextureCalls.Contains(name))
            message = $"{name}() has no host meaning: a filtered texture lookup needs the GPU.";
        else if (name == "texelFetch")
            message = $"texelFetch() has no host value when the function is called by name: bind it to a material with glsl.host(\"{functionName}\", material).";
        else if (name is "mat2" or "mat3" or "mat4")
            message = $"{name}() is not supported on the host: mat* types are GLSL-only in this version.";

        if (message == null)
            return false;
        found.Add((at, message));
        return true;
    }
}
