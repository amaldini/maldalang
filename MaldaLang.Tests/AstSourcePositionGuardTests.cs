// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guard: every AST node the parser builds carries a source position (1-based line and
/// column) and the source file. Runtime errors, <c>malda check</c> diagnostics, C# <c>#line</c>
/// mapping and the JS source map all read these, and a node built without them reports
/// "line 0".
/// </summary>
public class AstSourcePositionGuardTests
{
    /// <summary>
    /// Node types the parser may legitimately leave without a position. Empty today: every
    /// desugared node copies the position of the construct it stands for. Add an entry with a
    /// comment saying why, rather than weakening the guard.
    /// </summary>
    private static readonly HashSet<Type> Exempt = new()
    {
    };

    /// <summary>One construct per line where practical, so a failure names the culprit.</summary>
    private const string EveryExpressionKind = """
        var o = { "a": { "b": 1 }, c: [1, 2, 3] };
        var d = dict { "k": 1, "j": 2 };
        var arr = [1, 2, 3,];
        var m1 = o.a.b;
        var m2 = o?.a?.b;
        var i1 = arr[0];
        var i2 = arr?[1];
        var i3 = o["a"]["b"];
        var call = io.print(str.upper("x"));
        var chain = o.a.b.toString().length;
        var bin = 1 + 2 * 3 - 4 / 5 % 6;
        var cmp = 1 < 2 and 2 >= 1 or not (3 == 4) && 5 != 6;
        var nc = null ?? 3;
        var tern = true ? [1] : [2];
        var un = -bin;
        var lam1 = (a, b) => a + b;
        var lam2 = x => x * 2;
        var lam3 = function (a) { return a; };
        var lam4 = () => { return 1; };
        var interp = $"v {arr[0]} {o.a.b} {math.sqrt(4)}";
        var lc = [x * 2 for x in arr if x > 1];
        var dc = { k: v for k in arr };
        var g = graph directed { nodes: ["a", "b"], edges: [["a", "b"]] };
        var p = arr |> str.text;
        var mt = match 3 { case 1: "one"; case n if n > 2: "big"; default: "other"; };
        const ONE, TWO;
        var [fa, fb] = arr;
        var { a, c } = o;
        bin = 2;
        bin += 1;
        o.a.b = 5;
        arr[0] = 9;
        arr[1] += 1;
        bin++;
        ++bin;
        class Point(x, y) function total() this.x + this.y;
        class Base { var v = 1; function Base() { } function get() { return this.v; } }
        class Child extends Base { function Child() { super(); } function get() { return super.get() + 1; } }
        var pt = new Point(1, 2);
        type Shape = Circle(r) | Square(side);
        var shp = Circle(1);
        match shp {
            case Circle(r): io.print(r);
            case Square(s): io.print(s);
        }
        function f(a, b,) {
            if (a > b) { return a; } else if (a == b) { return 0; } else { return b; }
        }
        for (var k = 0; k < 2; k = k + 1) { continue; }
        foreach (var item in arr) { break; }
        while (false) { }
        try { throw "x"; } catch (e) { io.print(e); } finally { io.print("done"); }
        print("legacy");
        """;

    [Fact]
    public void SyntheticSnippet_EveryNodeHasAPosition()
    {
        const string path = "every_expression_kind.malda";
        var statements = ParseClean(EveryExpressionKind, path);
        var missing = MissingPositions(statements, path).ToList();
        Assert.True(missing.Count == 0, "Nodes without a source position:" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        var kinds = AllNodes(statements).Select(n => n.GetType()).ToHashSet();
        foreach (var expected in new[]
                 {
                     typeof(MemberAccessExpression), typeof(ArrayAccessExpression), typeof(FunctionCallExpression),
                     typeof(ObjectLiteralExpression), typeof(DictionaryLiteralExpression), typeof(AssignmentStatement),
                     typeof(LambdaExpression), typeof(InterpolatedStringExpression), typeof(NewExpression),
                 })
        {
            Assert.Contains(expected, kinds);
        }
    }

    [Fact]
    public void Examples_EveryNodeHasAPosition()
    {
        var root = PlanningPaths.RepoRoot;
        var files = ExamplesParseGuardTests.EnumerateExamples(Path.Combine(root, "Examples"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs", "llm", "few-shot"), "*.malda"))
            .ToList();
        Assert.True(files.Count > 100, $"Expected the Examples catalog, found only {files.Count} files.");

        var failures = files
            .AsParallel()
            .SelectMany(f =>
            {
                List<Statement> statements;
                try
                {
                    var parser = new MaldaLang.Parser.Parser(new Lexer(File.ReadAllText(f), f).Tokenize(), f);
                    statements = parser.Parse();
                }
                catch (Exception)
                {
                    return []; // parse failures are ExamplesParseGuardTests' job
                }

                return MissingPositions(statements, f)
                    .Select(m => $"{ExamplesParseGuardTests.RelativePath(root, f)}: {m}");
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} node(s) without a source position ({files.Count} files checked):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Take(200)));
    }

    private static List<Statement> ParseClean(string source, string path)
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(source, path).Tokenize(), path);
        var statements = parser.Parse();
        Assert.True(parser.Errors.Count == 0, string.Join(Environment.NewLine, parser.Errors.Select(e => e.Message)));
        return statements;
    }

    /// <summary>
    /// Every reachable node, including object/dict literal keys and values, which
    /// <see cref="AstNodeWalker"/> does not visit (they live in tuple fields).
    /// </summary>
    private static IEnumerable<Node> AllNodes(IEnumerable<Statement> statements)
    {
        var seen = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        var roots = new Stack<Node>(statements);
        while (roots.Count > 0)
        {
            foreach (var node in AstNodeWalker.Descendants(roots.Pop()))
            {
                if (!seen.Add(node))
                    continue;
                yield return node;

                IEnumerable<(Expression Key, Expression Value)> entries = node switch
                {
                    ObjectLiteralExpression obj => obj.Properties,
                    DictionaryLiteralExpression dict => dict.Entries,
                    _ => [],
                };
                foreach (var (key, value) in entries)
                {
                    roots.Push(key);
                    roots.Push(value);
                }
            }
        }
    }

    /// <summary>Descriptions of nodes with line or column 0, or without the source file.</summary>
    private static IEnumerable<string> MissingPositions(IEnumerable<Statement> statements, string path)
    {
        foreach (var node in AllNodes(statements))
        {
            if (Exempt.Contains(node.GetType()))
                continue;
            if (node.Line <= 0 || node.Column <= 0)
                yield return $"{node.GetType().Name} at line {node.Line}, column {node.Column}{Describe(node)}";
            else if (string.IsNullOrEmpty(node.SourceFile))
                yield return $"{node.GetType().Name} at {node.Line}:{node.Column} has no SourceFile{Describe(node)}";
        }
    }

    private static string Describe(Node node) => node switch
    {
        MemberAccessExpression m => $" (.{m.Member})",
        IdentifierExpression id => $" ({id.Name})",
        _ => "",
    };
}
