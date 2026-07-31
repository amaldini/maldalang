// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Threading.Tasks;
using Xunit;
using InterpreterRuntime = MaldaLang.Interpreter.Interpreter;
using ParserRuntime = MaldaLang.Parser.Parser;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class IncludeStatementTests : TestBase
{
    [Fact]
    public async Task Include_ComposesStatementsFromRelativeFile()
    {
        var tempDir = CreateTempDirectory("include_compose_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            var libPath = Path.Combine(tempDir, "lib.malda");

            await File.WriteAllTextAsync(libPath, "var answer = 42;");
            await File.WriteAllTextAsync(mainPath, "include \"lib.malda\";\nprint(answer);");

            var source = await File.ReadAllTextAsync(mainPath);
            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.Tokenize();
            var parser = new ParserRuntime(tokens, mainPath);
            var statements = parser.Parse();

            Assert.Empty(parser.Errors);

            RedirectConsole();
            try
            {
                var interpreter = new InterpreterRuntime();
                await interpreter.InterpretAsync(statements);
                Assert.Equal("42", GetOutput());
            }
            finally
            {
                RestoreConsole();
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Include_CircularReference_ReportsParseError()
    {
        var tempDir = CreateTempDirectory("include_cycle_");
        try
        {
            var aPath = Path.Combine(tempDir, "a.malda");
            var bPath = Path.Combine(tempDir, "b.malda");

            await File.WriteAllTextAsync(aPath, "include \"b.malda\";");
            await File.WriteAllTextAsync(bPath, "include \"a.malda\";");

            var source = await File.ReadAllTextAsync(aPath);
            var lexer = new Lexer(source, aPath);
            var tokens = lexer.Tokenize();
            var parser = new ParserRuntime(tokens, aPath);
            parser.Parse();

            Assert.Contains(parser.Errors, error => error.Message.Contains("Circular include detected"));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Include_InsideBlock_ReportsTopLevelOnlyParseError()
    {
        var tempDir = CreateTempDirectory("include_block_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            await File.WriteAllTextAsync(mainPath, "if (true) { include \"lib.malda\"; }");

            var source = await File.ReadAllTextAsync(mainPath);
            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.Tokenize();
            var parser = new ParserRuntime(tokens, mainPath);
            parser.Parse();

            Assert.Contains(parser.Errors, error => error.Message.Contains("'include' is only allowed at top-level scope."));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Include_SyntaxErrorInIncludedFile_ReportsIncludedFileLocation()
    {
        var tempDir = CreateTempDirectory("include_error_location_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            var libPath = Path.Combine(tempDir, "lib.malda");

            await File.WriteAllTextAsync(mainPath, "include \"lib.malda\";\nprint(\"done\");");
            await File.WriteAllTextAsync(libPath, "var a = 1;\nvar b = ;");

            var source = await File.ReadAllTextAsync(mainPath);
            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.Tokenize();
            var parser = new ParserRuntime(tokens, mainPath);
            parser.Parse();

            var error = Assert.Single(parser.Errors);
            Assert.Equal(libPath, error.SourceFileName);
            Assert.Equal(2, error.Line);
            Assert.Contains("Expect expression.", error.Message);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
