namespace MaldaLang.Tests;

using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public class PropertyRunnerTests
{
    [Fact]
    public void RunProperty_SameSeed_IsDeterministic()
    {
        var source = @"
property positiveOnly(x) {
    assert(x > 0, ""x must be > 0"");
}
";
        var statements = Parse(source);
        var property = statements.OfType<PropertyDeclaration>().Single();
        var runner = new PropertyRunner();
        var options = new PropertyRunOptions
        {
            Iterations = 50,
            Seed = 999
        };

        var first = runner.RunProperty(statements, property, options);
        var second = runner.RunProperty(statements, property, options);

        Assert.Equal(first.Passed, second.Passed);
        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(first.Iterations, second.Iterations);
        Assert.Equal(first.FailedTrial, second.FailedTrial);
        Assert.Equal(first.Counterexample, second.Counterexample);
        Assert.Equal(first.ShrunkCounterexample, second.ShrunkCounterexample);
    }

    [Fact]
    public void RunProperty_StringShrink_MinimizesToSimpleNonEmptyValue()
    {
        var source = @"
property stringMustBeEmpty(name) {
    assert(name == """", ""name must be empty"");
}
";
        var statements = Parse(source);
        var property = statements.OfType<PropertyDeclaration>().Single();
        var runner = new PropertyRunner();
        var options = new PropertyRunOptions
        {
            Iterations = 100,
            Seed = 999
        };

        var result = runner.RunProperty(statements, property, options);

        Assert.False(result.Passed);
        Assert.NotNull(result.Counterexample);
        Assert.NotNull(result.ShrunkCounterexample);
        Assert.Equal("[\"a\"]", result.ShrunkCounterexample);
    }

    [Fact]
    public void RunProperty_ListShrink_MinimizesLengthAndElementComplexity()
    {
        var source = @"
property listMustBeEmpty(items) {
    assert(items == [], ""items must be empty"");
}
";
        var statements = Parse(source);
        var property = statements.OfType<PropertyDeclaration>().Single();
        var runner = new PropertyRunner();
        var options = new PropertyRunOptions
        {
            Iterations = 100,
            Seed = 1337
        };

        var result = runner.RunProperty(statements, property, options);

        Assert.False(result.Passed);
        Assert.NotNull(result.ShrunkCounterexample);
        Assert.Equal("[[]]", result.ShrunkCounterexample);
    }

    [Fact]
    public void RunProperty_ZeroShrinkBudget_LeavesOriginalCounterexampleUnchanged()
    {
        var source = @"
property stringMustBeEmpty(name) {
    assert(name == """", ""name must be empty"");
}
";
        var statements = Parse(source);
        var property = statements.OfType<PropertyDeclaration>().Single();
        var runner = new PropertyRunner();
        var options = new PropertyRunOptions
        {
            Iterations = 100,
            Seed = 999,
            MaxShrinkAttempts = 0,
            MaxShrinkPassesPerArgument = 0
        };

        var result = runner.RunProperty(statements, property, options);

        Assert.False(result.Passed);
        Assert.NotNull(result.Counterexample);
        Assert.Equal(result.Counterexample, result.ShrunkCounterexample);
    }

    [Fact]
    public void RunProperty_OneOfShrink_IsDeterministicAcrossRuns()
    {
        var source = @"
property anyMustBeNull(anyValue) {
    assert(anyValue == null, ""anyValue must be null"");
}
";
        var statements = Parse(source);
        var property = statements.OfType<PropertyDeclaration>().Single();
        var runner = new PropertyRunner();
        var options = new PropertyRunOptions
        {
            Iterations = 75,
            Seed = 2026,
            MaxShrinkAttempts = 300
        };

        var first = runner.RunProperty(statements, property, options);
        var second = runner.RunProperty(statements, property, options);

        Assert.False(first.Passed);
        Assert.False(second.Passed);
        Assert.Equal(first.Counterexample, second.Counterexample);
        Assert.Equal(first.ShrunkCounterexample, second.ShrunkCounterexample);
    }

    private static List<Statement> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
