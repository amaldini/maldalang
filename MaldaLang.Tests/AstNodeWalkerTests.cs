// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <see cref="AstNodeWalker"/> must reach nodes held outside plain node properties: the
/// <c>(Key, Value)</c> tuples of object and dictionary literals, class members, match cases,
/// catch clauses, interpolation segments, decorator (named) arguments and graph literals.
/// </summary>
public class AstNodeWalkerTests
{
    private const string Source = """
        var o = { handler: function(x) { probeObjectValue(x); } };
        var nested = { outer: { inner: [() => probeNestedValue()] } };
        var d = dict { probeDictKey(): probeDictValue() };
        var g = graph directed { nodes: [probeGraphNode()], edges: [{ from: 1, to: probeGraphEdge() }] };
        class Box {
            var field = probeClassField();
            function method() { return probeClassMethod(); }
        }
        match 1 {
            case 1 if probeMatchGuard(): probeMatchBody();
            default: probeMatchDefault();
        }
        try { probeTry(); } catch (e) { probeCatch(); }
        var s = $"value {probeInterpolation()}";
        @budget(tokens: probeNamedDecoratorArg())
        function decorated() { }
        """;

    private static readonly string[] Probes =
    [
        "probeObjectValue", "probeNestedValue", "probeDictKey", "probeDictValue",
        "probeGraphNode", "probeGraphEdge", "probeClassField", "probeClassMethod", "probeMatchGuard",
        "probeMatchBody", "probeMatchDefault", "probeTry", "probeCatch", "probeInterpolation",
        "probeNamedDecoratorArg",
    ];

    private static List<Statement> Parse(string source)
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.True(parser.Errors.Count == 0, string.Join(Environment.NewLine, parser.Errors.Select(e => e.Message)));
        return statements;
    }

    private static HashSet<string> CalledNames(IEnumerable<Node> nodes) => nodes
        .OfType<FunctionCallExpression>()
        .Select(call => call.Callee)
        .OfType<IdentifierExpression>()
        .Select(id => id.Name)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Descendants_VisitsNodesInEveryHolderShape()
    {
        var statements = Parse(Source);

        var called = CalledNames(statements.SelectMany(s => AstNodeWalker.Descendants(s)));

        var missing = Probes.Where(p => !called.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "Not visited: " + string.Join(", ", missing));
    }

    [Fact]
    public void Descendants_VisitsObjectLiteralKeysAndValues()
    {
        var statements = Parse("var o = { name: probe() };");
        var literal = AstNodeWalker.Descendants(statements[0]).OfType<ObjectLiteralExpression>().Single();

        var visited = AstNodeWalker.Descendants(literal).ToList();

        var (key, value) = literal.Properties.Single();
        Assert.Contains(key, visited);
        Assert.Contains(value, visited);
        Assert.Contains("probe", CalledNames(visited));
    }

    [Fact]
    public void Descendants_DescendIntoFilter_StopsInsideLiteralValues()
    {
        var statements = Parse("var o = { run: function() { probeInside(); } };\nprobeOutside();");

        var called = CalledNames(statements.SelectMany(s =>
            AstNodeWalker.Descendants(s, n => n is not LambdaExpression and not FunctionDeclaration)));

        Assert.Contains("probeOutside", called);
        Assert.DoesNotContain("probeInside", called);
    }

    [Fact]
    public void Descendants_YieldsEachNodeOnce()
    {
        var statements = Parse(Source);

        var nodes = statements.SelectMany(s => AstNodeWalker.Descendants(s)).ToList();

        Assert.Equal(nodes.Count, nodes.Distinct<Node>(ReferenceEqualityComparer.Instance).Count());
    }
}
