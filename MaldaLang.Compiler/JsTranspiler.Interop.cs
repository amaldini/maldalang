// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// <c>import js …</c> (JavaScript interop). Each bound name is a module-scope <c>let</c>.
/// Globals are read from <c>globalThis</c> when the program loads; modules load through
/// <c>import()</c>, started when the program loads and awaited at the top of <c>main()</c>.
/// <c>import()</c> is written into the generated file itself, so a relative specifier resolves
/// against that file in Node and in the browser.
/// </summary>
public partial class JsTranspiler
{
    /// <summary>Names bound by <c>import js</c>; a local of the same name shadows them.</summary>
    private readonly HashSet<string> _jsImportNames = new(StringComparer.Ordinal);

    private const string JsReadyName = "__maldaJsReady";

    /// <summary>
    /// Names a binding cannot take: JavaScript reserved words (a <c>let</c> of that name is a
    /// syntax error) and the names the generated module uses itself.
    /// </summary>
    private static readonly HashSet<string> ReservedJsBindingNames = new(StringComparer.Ordinal)
    {
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete",
        "do", "else", "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements",
        "import", "in", "instanceof", "interface", "let", "new", "null", "package", "private", "protected",
        "public", "return", "static", "super", "switch", "this", "throw", "true", "try", "typeof", "var",
        "void", "while", "with", "yield", "arguments", "eval",
        "mlRuntime", "MaldaApp", "main", "globalThis"
    };

    /// <summary>
    /// Registers the names of <paramref name="imports"/> and drops repeats of the same binding
    /// (a file imported twice). A name bound to two different things is an error.
    /// </summary>
    private List<JsImportStatement> CollectJsImports(IEnumerable<JsImportStatement> imports)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var kept = new List<JsImportStatement>();
        foreach (var jsImport in imports)
        {
            var isRepeat = true;
            foreach (var name in jsImport.Names)
            {
                if (ReservedJsBindingNames.Contains(name) || name.StartsWith("__malda", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Line {jsImport.Line}: 'import js' cannot bind '{name}' (a JavaScript reserved word or a name the generated code uses). " +
                        "Import the module as a namespace and read the member, e.g. import js lib = \"…\"; then lib.default.");
                }

                var source = DescribeJsBinding(jsImport, name);
                if (sources.TryGetValue(name, out var existing))
                {
                    if (existing != source)
                    {
                        throw new NotSupportedException(
                            $"'{name}' is bound twice by 'import js' ({existing} and {source}). Use a different name for one of them.");
                    }

                    continue;
                }

                sources[name] = source;
                _jsImportNames.Add(name);
                isRepeat = false;
            }

            if (!isRepeat)
                kept.Add(jsImport);
        }

        return kept;
    }

    private static string DescribeJsBinding(JsImportStatement jsImport, string name) => jsImport.Kind switch
    {
        JsImportKind.Namespace => $"module \"{jsImport.Specifier}\"",
        JsImportKind.Named => $"export '{name}' of \"{jsImport.Specifier}\"",
        _ => $"global '{name}'"
    };

    /// <summary>Emits the bindings and the module loader. Returns true when <c>main()</c> must await modules.</summary>
    private bool EmitJsImports(List<JsImportStatement> imports)
    {
        if (imports.Count == 0)
            return false;

        var names = imports.SelectMany(jsImport => jsImport.Names).Distinct(StringComparer.Ordinal).Select(EscapeIdentifier);
        EmitLine($"let {string.Join(", ", names)};");

        foreach (var global in imports.Where(jsImport => jsImport.Kind == JsImportKind.Global))
        {
            foreach (var name in global.Names)
                EmitLine($"{EscapeIdentifier(name)} = globalThis[{TranspileLiteral(name)}];");
        }

        var modules = imports.Where(jsImport => jsImport.Kind != JsImportKind.Global).ToList();
        if (modules.Count == 0)
        {
            EmitLine(string.Empty);
            return false;
        }

        EmitLine($"const {JsReadyName} = (async () => {{");
        _indentLevel++;
        var moduleIndex = 0;
        foreach (var module in modules)
        {
            var specifier = TranspileLiteral(module.Specifier!);
            var load = $"await mlRuntime.js.importModule(() => import({specifier}), {specifier})";
            if (module.Kind == JsImportKind.Namespace)
            {
                EmitLine($"{EscapeIdentifier(module.Names[0])} = {load};");
                continue;
            }

            var moduleVar = $"__maldaJsModule{++moduleIndex}";
            EmitLine($"const {moduleVar} = {load};");
            foreach (var name in module.Names)
                EmitLine($"{EscapeIdentifier(name)} = mlRuntime.js.requireExport({moduleVar}, {TranspileLiteral(name)}, {specifier});");
        }
        _indentLevel--;
        EmitLine("})();");
        // main() awaits the loader and sees its error; this keeps a failure before main()
        // starts from being reported as an unhandled rejection as well.
        EmitLine($"{JsReadyName}.catch(() => {{}});");
        EmitLine(string.Empty);
        return true;
    }

    /// <summary>
    /// True when <paramref name="expression"/> is a member / index / call chain that starts at a
    /// name bound by <c>import js</c>: such calls are emitted as plain JavaScript, without the
    /// MALDA array / dictionary method rewrites (<c>el.append(x)</c> is the DOM's append).
    /// </summary>
    private bool IsJsImportRooted(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                    return _jsImportNames.Contains(identifier.Name) && !IsLocalName(identifier.Name);
                case MemberAccessExpression member:
                    expression = member.Object;
                    continue;
                case ArrayAccessExpression access:
                    expression = access.Array;
                    continue;
                case FunctionCallExpression call:
                    expression = call.Callee;
                    continue;
                default:
                    return false;
            }
        }
    }

    /// <summary><c>new THREE.Vector3(…)</c>: the dotted path stays a JavaScript member chain.</summary>
    private static string TranspileConstructorPath(string className) =>
        string.Join(".", className.Split('.').Select(EscapeIdentifier));
}
