namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Scaffolding;

public class NewCommandOptionsParserTests
{
    [Fact]
    public void TryParse_HappyPath_WithFlags_ParsesAllOptions()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "my-dir", "--name", "SalesPortal", "--force", "--local-first", "--no-tests" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out var options);

        Assert.True(ok);
        Assert.NotNull(options);
        Assert.Equal("webapi", options!.TemplateName);
        Assert.Equal("my-dir", options.DestinationPath);
        Assert.Equal("SalesPortal", options.ProjectName);
        Assert.True(options.Force);
        Assert.True(options.LocalFirst);
        Assert.False(options.IncludeTests);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void TryParse_UnknownFlag_ReturnsError()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "--bogus" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out _);

        Assert.False(ok);
        Assert.Contains("Unknown option '--bogus'", error.ToString());
    }

    [Fact]
    public void TryParse_MissingNameValue_ReturnsError()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "--name" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out _);

        Assert.False(ok);
        Assert.Contains("requires a value", error.ToString());
    }

    [Fact]
    public void TryParse_DuplicateFlag_ReturnsError()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "--force", "--force" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out _);

        Assert.False(ok);
        Assert.Contains("Duplicate option '--force'", error.ToString());
    }

    [Fact]
    public void TryParse_TooManyPositionalArgs_ReturnsError()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "a", "b" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out _);

        Assert.False(ok);
        Assert.Contains("Unexpected extra argument", error.ToString());
    }

    [Fact]
    public void WriteUsage_IncludesGameTemplate()
    {
        var output = new StringWriter();
        NewCommandOptionsParser.WriteUsage(output);
        var text = output.ToString();
        Assert.Contains("webapi|fullstack|game", text);
        Assert.Contains("malda new game my-game", text);
        Assert.Contains("malda new game my-scores --fullstack", text);
        Assert.Contains("--fullstack", text);
    }

    [Fact]
    public void TryParse_GameFullstackFlag_SetsFullstack()
    {
        var error = new StringWriter();
        var args = new[] { "new", "game", "my-scores", "--fullstack" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out var options);

        Assert.True(ok);
        Assert.NotNull(options);
        Assert.Equal("game", options!.TemplateName);
        Assert.Equal("my-scores", options.DestinationPath);
        Assert.True(options.Fullstack);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void TryParse_GameFullstackAlias_SetsGameTemplateAndFlag()
    {
        var error = new StringWriter();
        var args = new[] { "new", "game-fullstack", "board" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out var options);

        Assert.True(ok);
        Assert.NotNull(options);
        Assert.Equal("game", options!.TemplateName);
        Assert.Equal("board", options.DestinationPath);
        Assert.True(options.Fullstack);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void TryParse_FullstackFlagOnWebApi_ReturnsError()
    {
        var error = new StringWriter();
        var args = new[] { "new", "webapi", "my-api", "--fullstack" };

        var ok = NewCommandOptionsParser.TryParse(args, error, out _);

        Assert.False(ok);
        Assert.Contains("only valid with 'malda new game'", error.ToString());
    }
}
