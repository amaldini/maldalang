// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// Finds the <c>@shader()</c> functions host code reaches, checks them and types them.
/// <list type="bullet">
/// <item><b>Plain</b>: called by name (<c>sdBox(q, s)</c>). May use parameters, locals and
/// top-level MALDA consts.</item>
/// <item><b>Bound</b>: named in <c>glsl.host("name", material)</c>, with the kernels it calls.
/// May also read the uniforms declared to <c>glsl.compile</c> in the file and
/// <c>texelFetch</c> a <c>sampler2D</c> uniform.</item>
/// </list>
/// A function nothing on the host reaches is not looked at: it stays GLSL-only.
/// </summary>
internal sealed class GlslHostAnalysis
{
    private static readonly GlslHostAnalysis Empty = new();

    private readonly Dictionary<string, GlslFunctionTypes> _plainTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GlslFunctionTypes> _boundTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GlslType> _uniformTypes = new(StringComparer.Ordinal);
    private readonly List<string> _boundRoots = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _uniformReads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _needsBinding = new(StringComparer.Ordinal);

    private GlslHostAnalysis()
    {
    }

    /// <summary>Functions called by plain name and their callees, in declaration order.</summary>
    public IReadOnlyList<FunctionDeclaration> PlainFunctions { get; private set; } = Array.Empty<FunctionDeclaration>();

    /// <summary>Functions bound with <c>glsl.host</c> and their callees, in declaration order.</summary>
    public IReadOnlyList<FunctionDeclaration> BoundFunctions { get; private set; } = Array.Empty<FunctionDeclaration>();

    /// <summary>The names given to <c>glsl.host</c>, in first-use order.</summary>
    public IReadOnlyList<string> BoundRoots => _boundRoots;

    /// <summary>Types of a plain function: consts are its only globals.</summary>
    public IReadOnlyDictionary<string, GlslFunctionTypes> PlainTypes => _plainTypes;

    /// <summary>Types of a bound function: consts and the declared uniforms are its globals.</summary>
    public IReadOnlyDictionary<string, GlslFunctionTypes> BoundTypes => _boundTypes;

    /// <summary>The uniforms declared to <c>glsl.compile</c> in the file, by name (first declaration).</summary>
    public IReadOnlyDictionary<string, GlslType> UniformTypes => _uniformTypes;

    /// <summary>The uniforms a bound function reads itself (not through a callee), in source order.</summary>
    public IReadOnlyList<string> UniformReads(string function) =>
        _uniformReads.TryGetValue(function, out var reads) ? reads : Array.Empty<string>();

    /// <summary>
    /// True for a bound function that reads a uniform, itself or through a kernel it calls: its twin
    /// takes the material as a hidden first parameter. The others keep the plain shape.
    /// </summary>
    public bool NeedsBinding(string function) => _needsBinding.Contains(function);

    /// <summary>The top-level literal MALDA consts of the file, by name.</summary>
    public IReadOnlyDictionary<string, GlslLiteralConst> Consts { get; private set; } =
        new Dictionary<string, GlslLiteralConst>(StringComparer.Ordinal);

    /// <exception cref="NotSupportedException">A reached function uses something with no host meaning, or does not type-check.</exception>
    public static GlslHostAnalysis Run(
        IEnumerable<Statement> statements,
        IReadOnlyDictionary<string, FunctionDeclaration> shaderFunctions,
        IReadOnlyList<GlslLiteralConst> consts)
    {
        if (shaderFunctions.Count == 0)
            return Empty;

        // Calls inside a shader function are GLSL calls, not host calls.
        var hostCalls = AstNodeWalker.Descendants(
                statements.ToList(),
                node => node is not FunctionDeclaration function || !ShaderFunction.IsMarked(function))
            .OfType<FunctionCallExpression>()
            // The walk is not in source order; "first declaration" and "first use" must be.
            .OrderBy(call => call.Line)
            .ThenBy(call => call.Column)
            .ToList();

        var analysis = new GlslHostAnalysis();
        var plainRoots = new List<string>();
        foreach (var call in hostCalls)
        {
            if (call.Callee is IdentifierExpression callee && shaderFunctions.ContainsKey(callee.Name))
            {
                if (!plainRoots.Contains(callee.Name, StringComparer.Ordinal))
                    plainRoots.Add(callee.Name);
            }
            else if (call.Callee is MemberAccessExpression { Member: "host", Object: IdentifierExpression { Name: "glsl" } })
            {
                var name = HostTarget(call, shaderFunctions);
                if (!analysis._boundRoots.Contains(name, StringComparer.Ordinal))
                    analysis._boundRoots.Add(name);
            }
        }

        if (plainRoots.Count == 0 && analysis._boundRoots.Count == 0)
            return Empty;

        var declared = DeclaredGlobals.Read(hostCalls);
        analysis.Consts = consts.ToDictionary(constant => constant.Name, StringComparer.Ordinal);
        foreach (var (name, types) in declared.UniformTypes)
            analysis._uniformTypes[name] = types[0];

        var constTypes = analysis.Consts.ToDictionary(entry => entry.Key, entry => entry.Value.Type, StringComparer.Ordinal);
        var boundGlobals = new Dictionary<string, GlslType>(analysis._uniformTypes, StringComparer.Ordinal);
        // A const shadows a uniform of the same name on the host, as the diagnostics resolve it.
        foreach (var (name, type) in constTypes)
            boundGlobals[name] = type;

        Reach(plainRoots, analysis._plainTypes, shaderFunctions, constTypes, declared, analysis.Consts, bound: false);
        Reach(analysis._boundRoots, analysis._boundTypes, shaderFunctions, boundGlobals, declared, analysis.Consts, bound: true);

        analysis.FindUniformReads();
        analysis.PlainFunctions = shaderFunctions.Values.Where(f => analysis._plainTypes.ContainsKey(f.Name)).ToList();
        analysis.BoundFunctions = shaderFunctions.Values.Where(f => analysis._boundTypes.ContainsKey(f.Name)).ToList();
        return analysis;
    }

    private void FindUniformReads()
    {
        foreach (var (name, types) in _boundTypes)
        {
            var reads = AstNodeWalker.Descendants(types.Function.Body)
                .OfType<IdentifierExpression>()
                .Where(identifier => types.IsGlobal(identifier) && !Consts.ContainsKey(identifier.Name))
                .OrderBy(identifier => identifier.Line)
                .ThenBy(identifier => identifier.Column)
                .Select(identifier => identifier.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            _uniformReads[name] = reads;
            if (reads.Count > 0)
                _needsBinding.Add(name);
        }

        // A caller of a function that needs the material needs it too, to pass it on.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (name, types) in _boundTypes)
            {
                if (!_needsBinding.Contains(name) && types.Callees.Any(_needsBinding.Contains))
                    changed |= _needsBinding.Add(name);
            }
        }
    }

    private static void Reach(
        IEnumerable<string> roots,
        Dictionary<string, GlslFunctionTypes> reached,
        IReadOnlyDictionary<string, FunctionDeclaration> shaderFunctions,
        IReadOnlyDictionary<string, GlslType> globals,
        DeclaredGlobals declared,
        IReadOnlyDictionary<string, GlslLiteralConst> consts,
        bool bound)
    {
        var pending = new Queue<(string Name, string? ReachedThrough)>();
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (queued.Add(root))
                pending.Enqueue((root, null));
        }

        while (pending.Count > 0)
        {
            var (name, reachedThrough) = pending.Dequeue();
            var function = shaderFunctions[name];
            // What exists only on the GPU is named here, before the type pass meets it as an unknown name.
            GlslHostDiagnostics.Check(
                function,
                declared.Kinds,
                reachedThrough,
                consts,
                declared.ConstStringValues,
                bound ? declared.UniformTypes : null);
            var types = GlslTranspiler.AnalyzeTypes(function, shaderFunctions, globals);
            reached[name] = types;
            foreach (var callee in types.Callees)
            {
                if (queued.Add(callee))
                    pending.Enqueue((callee, name));
            }
        }
    }

    /// <summary>The function a <c>glsl.host("name", material)</c> call names.</summary>
    public static string HostTarget(FunctionCallExpression call, IReadOnlyDictionary<string, FunctionDeclaration> shaderFunctions)
    {
        if (call.Arguments.Count != 2 || call.Arguments[0] is not LiteralExpression { Value: string name })
        {
            throw new NotSupportedException(
                $"glsl.host ({call.Line}:{call.Column}) expects the name of an @shader() function as a string literal, and a material: glsl.host(\"sceneDistance\", material).");
        }

        if (!shaderFunctions.ContainsKey(name))
            throw new NotSupportedException($"glsl.host ({call.Line}:{call.Column}) references '{name}', which is not an @shader() function in this file.");
        return name;
    }

    /// <summary>What the file declares to <c>glsl.compile</c> as strings: <c>varyings</c>, <c>uniforms</c>, <c>consts</c>.</summary>
    private sealed class DeclaredGlobals
    {
        /// <summary>Name to kind: <c>varying</c>, <c>uniform</c> or <c>const</c> (first declaration).</summary>
        public Dictionary<string, string> Kinds { get; } = new(StringComparer.Ordinal);

        /// <summary>Const name to the text after <c>=</c>.</summary>
        public Dictionary<string, string> ConstStringValues { get; } = new(StringComparer.Ordinal);

        /// <summary>Uniform name to the distinct types it is declared with (more than one is a conflict).</summary>
        public Dictionary<string, IReadOnlyList<GlslType>> UniformTypes { get; } = new(StringComparer.Ordinal);

        public static DeclaredGlobals Read(IEnumerable<FunctionCallExpression> hostCalls)
        {
            var result = new DeclaredGlobals();
            foreach (var call in hostCalls)
            {
                if (call.Callee is not MemberAccessExpression { Member: "compile", Object: IdentifierExpression { Name: "glsl" } } ||
                    call.Arguments.Count != 1 || call.Arguments[0] is not ObjectLiteralExpression options)
                {
                    continue;
                }

                foreach (var (keyExpr, valueExpr) in options.Properties)
                {
                    var kind = (keyExpr as LiteralExpression)?.Value?.ToString() switch
                    {
                        "varyings" or "varying" => "varying",
                        "uniforms" or "uniform" => "uniform",
                        "consts" or "const" => "const",
                        _ => null
                    };
                    if (kind == null || valueExpr is not ArrayLiteralExpression declarations)
                        continue;

                    foreach (var declaration in declarations.Elements)
                    {
                        var text = (declaration as LiteralExpression)?.Value as string ?? string.Empty;
                        if (GlslTranspiler.DeclaredName(text) is not { } name)
                            continue;

                        result.Kinds.TryAdd(name, kind);
                        var equals = text.IndexOf('=');
                        if (kind == "const" && equals >= 0)
                            result.ConstStringValues.TryAdd(name, text[(equals + 1)..].Trim().TrimEnd(';').Trim());

                        if (kind == "uniform" && GlslTranspiler.TryParseDeclaration(text, out _, out var type))
                        {
                            var types = result.UniformTypes.TryGetValue(name, out var existing)
                                ? (List<GlslType>)existing
                                : new List<GlslType>();
                            // An array sized by a const name matches any length: not a conflict.
                            if (!types.Any(known => SameUniformType(known, type)))
                                types.Add(type);
                            result.UniformTypes[name] = types;
                        }
                    }
                }
            }

            return result;
        }

        private static bool SameUniformType(GlslType left, GlslType right) =>
            left == right ||
            (left.IsArray && right.IsArray && left.Element == right.Element &&
             (left.ArrayLength == GlslType.UnknownArrayLength || right.ArrayLength == GlslType.UnknownArrayLength));
    }
}
