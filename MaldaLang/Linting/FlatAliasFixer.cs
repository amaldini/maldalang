// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MaldaLang.BuiltIns;
using MaldaLang.Formatting;
using MaldaLang.IDE;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>One replacement made by <see cref="FlatAliasFixer"/> (offsets in the decoded source text; 1-based line / column).</summary>
public sealed record FlatAliasEdit(int Start, int Length, string OldText, string NewText, int Line, int Column);

/// <summary>Fix plan for one file: the edits and the rewritten text (equal to the input when there are none).</summary>
public sealed class FlatAliasFileFix
{
    public FlatAliasFileFix(string path, string source, IReadOnlyList<FlatAliasEdit> edits, bool parsed)
    {
        Path = path;
        Source = source;
        Edits = edits;
        Parsed = parsed;
        FixedSource = Apply(source, edits);
    }

    public string Path { get; }
    public string Source { get; }
    public string FixedSource { get; }
    public IReadOnlyList<FlatAliasEdit> Edits { get; }
    /// <summary>False when the file does not parse; nothing is rewritten then.</summary>
    public bool Parsed { get; }
    public bool Changed => Edits.Count > 0;

    private static string Apply(string source, IReadOnlyList<FlatAliasEdit> edits)
    {
        if (edits.Count == 0)
            return source;

        var sb = new StringBuilder(source.Length + edits.Count * 4);
        var at = 0;
        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            sb.Append(source, at, edit.Start - at);
            sb.Append(edit.NewText);
            at = edit.Start + edit.Length;
        }

        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }
}

/// <summary>
/// <c>malda lint --fix</c>: rewrites deprecated flat stdlib aliases to their namespaced form
/// (<c>print(</c> → <c>io.print(</c>, <c>max(</c> → <c>math.max(</c>, <c>Math.floor(</c> →
/// <c>math.floor(</c>) — the <c>malda-style</c> warning of <c>malda check</c>. The alias → module
/// table is <see cref="StdLibNamespaces.TryGetFlatAliasModule"/>.
/// <para>
/// Only mechanical cases are rewritten: a call the parser sees, whose name token is followed by
/// <c>(</c> on the lossless <see cref="SourceTriviaScanner"/> scan, outside strings, comments and
/// <c>@shader()</c> bodies. A name the program (or a file it includes or imports) binds anywhere —
/// function, class, member, variable, parameter, pattern, import, or any bare non-call use — is
/// left alone, and so is every alias of a module whose name (<c>io</c> / <c>str</c> / <c>math</c>)
/// the program binds. Calls inside <c>$"{…}"</c> holes are rewritten too (the hole text is scanned
/// the same way). Only the name token changes; every other byte is kept.
/// </para>
/// <para>
/// Suppress with <c>// malda-lint-disable-next-line malda-style</c> (or <c>-line</c> / <c>-file</c>,
/// or a directive without codes).
/// </para>
/// </summary>
public static class FlatAliasFixer
{
    /// <summary>Diagnostic source of the warning this fixes; also the suppression code.</summary>
    public const string StyleCode = "malda-style";

    /// <summary>Fix one source. <paramref name="path"/> resolves <c>include</c> / file <c>import</c>.</summary>
    public static FlatAliasFileFix Fix(string source, string? path = null)
    {
        var program = Analyze(source, path);
        return Build(program, program.Bound);
    }

    /// <summary>
    /// Fix several files together. A file that another listed file includes is also checked
    /// against the names bound by that includer (a library may call a function its includer
    /// declares). Files are returned in input order.
    /// </summary>
    public static List<FlatAliasFileFix> FixFiles(IReadOnlyList<(string Path, string Source)> files)
    {
        var programs = files.Select(f => Analyze(f.Source, f.Path)).ToList();
        var fixes = new List<FlatAliasFileFix>(programs.Count);
        foreach (var program in programs)
        {
            var veto = new HashSet<string>(program.Bound, StringComparer.Ordinal);
            foreach (var other in programs)
            {
                if (!ReferenceEquals(other, program) && other.Files.Any(f => SameFile(f, program.Path)))
                    veto.UnionWith(other.Bound);
            }

            fixes.Add(Build(program, veto));
        }

        return fixes;
    }

    private static FlatAliasFileFix Build(ProgramScan program, HashSet<string> veto)
    {
        if (!program.Parsed || veto.Contains(AllNames))
            return new FlatAliasFileFix(program.Path ?? "", program.Source, Array.Empty<FlatAliasEdit>(), program.Parsed);

        var suppressions = LintSuppressions.Parse(program.Source);
        var edits = program.Candidates
            .Where(c => !veto.Contains(c.Name) && !veto.Contains(c.Module))
            .Where(c => !suppressions.IsSuppressed(StyleCode, c.Token.Line))
            .GroupBy(c => c.Token.Start)
            .Select(g => g.First())
            .OrderBy(c => c.Token.Start)
            .Select(c => new FlatAliasEdit(c.Token.Start, c.Token.Text.Length, c.Token.Text, c.Replacement, c.Token.Line, c.Token.Column))
            .ToList();
        return new FlatAliasFileFix(program.Path ?? "", program.Source, edits, true);
    }

    /// <summary>Marker in a bound-name set: veto every rewrite (e.g. a package <c>using</c> may export any name).</summary>
    private const string AllNames = "*";

    private sealed record Candidate(TriviaToken Token, string Name, string Module, string Member, string Replacement);

    private sealed class ProgramScan
    {
        public string Source = "";
        public string? Path;
        public bool Parsed = true;
        /// <summary>Full paths of the root and every file spliced in by <c>include</c>.</summary>
        public List<string> Files = new();
        public HashSet<string> Bound = new(StringComparer.Ordinal);
        public List<Candidate> Candidates = new();
    }

    private static ProgramScan Analyze(string source, string? path)
    {
        var scan = new ProgramScan { Source = source, Path = path };
        List<Statement> statements;
        try
        {
            var tokens = new Lexer(source, path).Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, path);
            statements = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                scan.Parsed = false;
                return scan;
            }
        }
        catch
        {
            scan.Parsed = false;
            return scan;
        }

        if (path != null)
            scan.Files.Add(FullPath(path));

        // Calls the parser sees, outside @shader() bodies. Positions inside $"{…}" holes are
        // relative to the hole, so those calls only contribute their names.
        var callSites = new HashSet<(int Line, int Column, string Name)>();
        var holeCallNames = new HashSet<string>(StringComparer.Ordinal);
        var includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statements)
        {
            if (IsShader(statement))
            {
                // GLSL body: bare cos / dot / length are GPU intrinsics, never host aliases.
                CollectBound(statement, scan.Bound, path);
                continue;
            }

            foreach (var node in Walk(statement, n => !IsShader(n) && n is not InterpolatedStringExpression))
            {
                CollectBound(node, scan.Bound, path);
                var inRoot = node.SourceFile == null || path == null || DiagnosticSourceFiles.IsSameFile(node.SourceFile, path);
                if (!inRoot && !string.IsNullOrWhiteSpace(node.SourceFile))
                    includedFiles.Add(FullPath(node.SourceFile!));

                if (node is InterpolatedStringExpression interpolated && !IsShader(node))
                {
                    foreach (var inner in Walk(interpolated, n => !IsShader(n)))
                    {
                        CollectBound(inner, scan.Bound, path);
                        if (CallName(inner) is { } holeName)
                            holeCallNames.Add(holeName.Key);
                    }

                    continue;
                }

                if (inRoot && !IsShader(node) && CallName(node) is { } call)
                    callSites.Add((call.Line, call.Column, call.Key));
            }
        }

        foreach (var included in includedFiles)
        {
            scan.Files.Add(included);
            CollectBoundFromFile(included, scan.Bound);
        }

        var rootTokens = SafeScan(source);
        CollectBoundFromTokens(rootTokens, scan.Bound);
        CollectCandidates(rootTokens, callSites, holeCallNames, scan.Candidates, hole: false);
        return scan;
    }

    /// <summary>A flat-alias call the parser built: key "name" or "Math.member", at the name token.</summary>
    private static (string Key, int Line, int Column)? CallName(Node node)
    {
        switch (node)
        {
            case FunctionCallExpression { Callee: IdentifierExpression id }
                when StdLibNamespaces.TryGetFlatAliasModule(id.Name, out _):
                return (id.Name, id.Line, id.Column);
            case FunctionCallExpression { Callee: MemberAccessExpression { Object: IdentifierExpression { Name: StdLibNamespaces.DeprecatedMathModuleAlias } module } member }
                when StdLibNamespaces.MathMethodNames.Contains(member.Member):
                return (StdLibNamespaces.DeprecatedMathModuleAlias + "." + member.Member, module.Line, module.Column);
            case PrintStatement print:
                return ("print", print.Line, print.Column);
            default:
                return null;
        }
    }

    /// <summary>
    /// <see cref="AstNodeWalker.Descendants"/> plus the <c>(key, value)</c> entries of object and
    /// dictionary literals, which it does not reach (tuple items are fields, not properties).
    /// A start node <paramref name="descendInto"/> rejects is yielded without its children.
    /// </summary>
    private static IEnumerable<Node> Walk(Node start, Func<Node, bool> descendInto)
    {
        if (!descendInto(start))
        {
            yield return start;
            yield break;
        }

        foreach (var node in AstNodeWalker.Descendants(start, descendInto))
        {
            yield return node;
            if (!ReferenceEquals(node, start) && !descendInto(node))
                continue;

            var entries = node switch
            {
                ObjectLiteralExpression obj => obj.Properties,
                DictionaryLiteralExpression dict => dict.Entries,
                _ => null
            };
            if (entries == null)
                continue;

            foreach (var (key, value) in entries)
            {
                foreach (var inner in Walk(key, descendInto))
                    yield return inner;
                foreach (var inner in Walk(value, descendInto))
                    yield return inner;
            }
        }
    }

    private static bool IsShader(Node node) => node is FunctionDeclaration function && ShaderFunction.IsMarked(function);

    /// <summary>
    /// Code tokens of <paramref name="tokens"/>: a call candidate is <c>name (</c> (or
    /// <c>Math . member (</c>) whose name is not a member (<c>x.name</c>), a declaration
    /// (<c>function name(</c>), a decorator (<c>@name(</c>) or a constructor (<c>new name(</c>).
    /// </summary>
    private static void CollectCandidates(
        List<TriviaToken> tokens,
        HashSet<(int Line, int Column, string Name)> callSites,
        HashSet<string> holeCallNames,
        List<Candidate> candidates,
        bool hole)
    {
        var code = tokens.Where(t => t.IsCode).ToList();
        for (var i = 0; i < code.Count; i++)
        {
            var token = code[i];
            if (token.Kind == TriviaKind.String)
            {
                foreach (var holeTokens in ScanHoles(token))
                    CollectCandidates(holeTokens, callSites, holeCallNames, candidates, hole: true);
                continue;
            }

            if (token.Kind != TriviaKind.Word)
                continue;

            var prev = i > 0 ? code[i - 1] : null;
            if (prev != null && (prev.Is(".") || prev.Is("@") || prev.Is("new") || prev.Is("function") ||
                                 prev.Is("class") || prev.Is("prompt") || prev.Is("workflow")))
                continue;

            if (token.Text == StdLibNamespaces.DeprecatedMathModuleAlias)
            {
                if (i + 3 < code.Count && code[i + 1].Is(".") && code[i + 2].Kind == TriviaKind.Word &&
                    code[i + 3].Is("(") && StdLibNamespaces.MathMethodNames.Contains(code[i + 2].Text))
                {
                    var key = StdLibNamespaces.DeprecatedMathModuleAlias + "." + code[i + 2].Text;
                    if (hole ? holeCallNames.Contains(key) : callSites.Contains((token.Line, token.Column, key)))
                        candidates.Add(new Candidate(token, token.Text, StdLibNamespaces.MathModule, code[i + 2].Text, StdLibNamespaces.MathModule));
                }

                continue;
            }

            if (i + 1 >= code.Count || !code[i + 1].Is("(") ||
                !StdLibNamespaces.TryGetFlatAliasModule(token.Text, out var module))
                continue;

            if (hole ? holeCallNames.Contains(token.Text) : callSites.Contains((token.Line, token.Column, token.Text)))
                candidates.Add(new Candidate(token, token.Text, module, token.Text, module + "." + token.Text));
        }
    }

    /// <summary>
    /// Token lists for each <c>{…}</c> hole of an interpolated string token, with offsets, lines
    /// and columns in the enclosing source. Hole boundaries follow the lexer
    /// (<c>{{</c> is a literal brace; quotes are not special while matching braces).
    /// </summary>
    private static IEnumerable<List<TriviaToken>> ScanHoles(TriviaToken token)
    {
        var text = token.Text;
        if (!text.StartsWith("$\"", StringComparison.Ordinal))
            yield break;

        var i = text.StartsWith("$\"\"\"", StringComparison.Ordinal) ? 4 : 2;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '\\')
            {
                i += 2;
                continue;
            }

            if (ch == '{' && i + 1 < text.Length && text[i + 1] == '{')
            {
                i += 2;
                continue;
            }

            if (ch != '{')
            {
                i++;
                continue;
            }

            var start = i + 1;
            var depth = 1;
            var j = start;
            while (j < text.Length && depth > 0)
            {
                if (text[j] == '\\')
                    j++;
                else if (text[j] == '{')
                    depth++;
                else if (text[j] == '}')
                    depth--;
                j++;
            }

            if (depth != 0)
                yield break;

            var content = text.Substring(start, j - 1 - start);
            var (line, column) = Advance(token.Line, token.Column, text, start);
            yield return SafeScan(content)
                .Select(t => Shift(t, token.Start + start, line, column))
                .ToList();
            i = j;
        }
    }

    private static (int Line, int Column) Advance(int line, int column, string text, int count)
    {
        for (var k = 0; k < count && k < text.Length; k++)
        {
            if (text[k] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private static TriviaToken Shift(TriviaToken t, int offset, int line, int column) =>
        new(t.Kind, t.Text, t.Start + offset, t.Line == 1 ? line : line + t.Line - 1, t.Line == 1 ? column + t.Column - 1 : t.Column);

    /// <summary>
    /// Names a declaration binds (functions, classes and their members, prompts, workflows,
    /// variables, parameters, patterns, imports). Over-approximating only skips rewrites.
    /// </summary>
    private static void CollectBound(Node node, HashSet<string> bound, string? path)
    {
        switch (node)
        {
            case IdentifierExpression:
            case MemberAccessExpression:
            case FunctionCallExpression:
            case NamedArgumentExpression:
                return;
            case UsingStatement:
                bound.Add(AllNames);
                return;
            case ClassDeclaration cls:
                foreach (var member in cls.Members)
                    bound.Add(member.Name);
                break;
            case ImportStatement import:
                if (import.SelectedNames != null)
                    bound.UnionWith(import.SelectedNames);
                if (import.Alias != null)
                    bound.Add(import.Alias);
                if (import.IsFileImport && import.SelectedNames == null)
                    CollectBoundFromImport(import, bound, path);
                return;
        }

        foreach (var property in node.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            if (property.PropertyType == typeof(string) && BindingProperties.Contains(property.Name))
            {
                if (property.GetValue(node) is string name && name.Length > 0)
                    bound.Add(name);
            }
            else if (typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType) && BindingListProperties.Contains(property.Name))
            {
                if (property.GetValue(node) is IEnumerable<string> names)
                    bound.UnionWith(names.Where(n => !string.IsNullOrEmpty(n)));
            }
        }
    }

    private static readonly HashSet<string> BindingProperties = new(StringComparer.Ordinal)
    {
        "Name", "VariableName", "Variable", "BindingName", "ParameterName", "ExceptionVariable",
        "Alias", "ClassName", "ActorName"
    };

    private static readonly HashSet<string> BindingListProperties = new(StringComparer.Ordinal)
    {
        "Parameters", "ParameterNames", "SelectedNames"
    };

    /// <summary>
    /// Token-level complement of <see cref="CollectBound"/>: a bare word (not after <c>.</c>) that
    /// is not called — <c>var max = …</c>, <c>(max) =&gt;</c>, <c>case max:</c>, <c>f(max)</c> — may
    /// shadow the alias, so it is bound. Object keys and named arguments (<c>{ max: 1 }</c>) are not.
    /// For a module name, <c>io.</c> is the module itself; any other bare use binds it.
    /// </summary>
    private static void CollectBoundFromTokens(List<TriviaToken> tokens, HashSet<string> bound)
    {
        var code = tokens.Where(t => t.IsCode).ToList();
        for (var i = 0; i < code.Count; i++)
        {
            var token = code[i];
            if (token.Kind == TriviaKind.String)
            {
                foreach (var hole in ScanHoles(token))
                    CollectBoundFromTokens(hole, bound);
                continue;
            }

            if (token.Kind != TriviaKind.Word || !IsWatchedName(token.Text))
                continue;

            var prev = i > 0 ? code[i - 1] : null;
            var next = i + 1 < code.Count ? code[i + 1] : null;
            if (prev != null && prev.Is("."))
                continue;

            if (IsModuleName(token.Text))
            {
                if (next == null || !next.Is("."))
                    bound.Add(token.Text);
                continue;
            }

            if (next != null && next.Is("("))
                continue;
            if (next != null && next.Is(":") && prev != null && (prev.Is("{") || prev.Is(",") || prev.Is("(")))
                continue;
            bound.Add(token.Text);
        }
    }

    private static bool IsModuleName(string name) =>
        name is StdLibNamespaces.MathModule or StdLibNamespaces.StrModule or StdLibNamespaces.IoModule
            or StdLibNamespaces.DeprecatedMathModuleAlias;

    private static bool IsWatchedName(string name) =>
        IsModuleName(name) || StdLibNamespaces.TryGetFlatAliasModule(name, out _);

    private static void CollectBoundFromFile(string file, HashSet<string> bound)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            return;
        }

        CollectBoundFromTokens(SafeScan(text), bound);
    }

    /// <summary>A whole-file <c>import "lib.malda";</c> brings the module's names into scope.</summary>
    private static void CollectBoundFromImport(ImportStatement import, HashSet<string> bound, string? path)
    {
        var baseDir = path != null ? System.IO.Path.GetDirectoryName(FullPath(path)) : Directory.GetCurrentDirectory();
        var target = System.IO.Path.Combine(baseDir ?? "", import.FilePath!);
        if (!File.Exists(target))
        {
            // Unresolved: the imported names are unknown, so rewrite nothing.
            bound.Add(AllNames);
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(target);
        }
        catch
        {
            bound.Add(AllNames);
            return;
        }

        try
        {
            var statements = new MaldaLang.Parser.Parser(new Lexer(text, target).Tokenize(), target).Parse();
            foreach (var statement in statements)
            {
                foreach (var node in Walk(statement, _ => true))
                {
                    if (node is not ImportStatement)
                        CollectBound(node, bound, target);
                }
            }
        }
        catch
        {
            bound.Add(AllNames);
            return;
        }

        CollectBoundFromTokens(SafeScan(text), bound);
    }

    private static List<TriviaToken> SafeScan(string source)
    {
        try
        {
            return SourceTriviaScanner.Scan(source);
        }
        catch
        {
            return new List<TriviaToken>();
        }
    }

    private static string FullPath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static bool SameFile(string a, string? b) =>
        b != null && string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);
}
