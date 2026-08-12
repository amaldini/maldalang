// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using MaldaLang.Compiler;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SelectiveImportTests : TestBase
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static List<Statement> Parse(string source, string? path = null)
    {
        var lexer = new Lexer(source, path);
        var parser = new Parser.Parser(lexer.Tokenize(), path);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    [Fact]
    public void Parse_SelectiveFileImport_CapturesNames()
    {
        var statements = Parse("""
            import { add, VERSION } from "math_utils.malda";
            print(add(1, 2));
            """);
        var importStmt = Assert.IsType<ImportStatement>(statements[0]);
        Assert.True(importStmt.IsSelective);
        Assert.True(importStmt.IsFileImport);
        Assert.Equal("math_utils.malda", importStmt.FilePath);
        Assert.Equal(new[] { "add", "VERSION" }, importStmt.SelectedNames);
    }

    [Fact]
    public void Parse_EmptySelectiveList_IsError()
    {
        var lexer = new Lexer("""
            import { } from "lib.malda";
            """);
        var parser = new Parser.Parser(lexer.Tokenize());
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public async Task Runtime_SelectiveFileImport_MergesOnlyNamedExports()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export function visible() { return 1; }
                export function other() { return 2; }
                var secret = 9;
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import { visible } from "lib.malda";
                print(visible());
                """;
            File.WriteAllText(mainPath, source);

            var statements = Parse(source, mainPath);
            var interpreter = new Interpreter.Interpreter();
            typeof(Interpreter.Interpreter)
                .GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(interpreter, new ModuleLoader());

            RedirectConsole();
            try
            {
                await interpreter.InterpretAsync(statements);
                Assert.Equal("1", GetOutput().Trim());
            }
            finally
            {
                RestoreConsole();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Runtime_SelectiveMissingExport_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sel_miss_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export function visible() { return 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import { missing } from "lib.malda";
                print(missing());
                """;
            File.WriteAllText(mainPath, source);

            var statements = Parse(source, mainPath);
            var interpreter = new Interpreter.Interpreter();
            typeof(Interpreter.Interpreter)
                .GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(interpreter, new ModuleLoader());

            var ex = await Assert.ThrowsAsync<RuntimeException>(() => interpreter.InterpretAsync(statements));
            Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadImportedSymbols_Selective_OnlySelectedFunctions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sel_sym_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export function visible() { return 1; }
                export function other() { return 2; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import { visible } from "lib.malda";
                """;
            File.WriteAllText(mainPath, source);

            var imported = ModuleSymbolResolver.LoadImportedSymbols(Parse(source, mainPath), mainPath);
            Assert.Single(imported.Functions);
            Assert.Equal("visible", imported.Functions[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExpandFileImportsForTranspile_Selective_InlinesOnlySelected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sel_tp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export function visible() { return 1; }
                export function other() { return 2; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import { visible } from "lib.malda";
                function host() { return visible(); }
                """;
            File.WriteAllText(mainPath, source);

            var expanded = ModuleSymbolResolver.ExpandFileImportsForTranspile(Parse(source, mainPath), mainPath);
            Assert.Contains(expanded, s => s is FunctionDeclaration f && f.Name == "visible");
            Assert.DoesNotContain(expanded, s => s is FunctionDeclaration f && f.Name == "other");
            Assert.Contains(expanded, s => s is FunctionDeclaration f && f.Name == "host");
            Assert.DoesNotContain(expanded, s => s is ImportStatement);

            var csharp = new CSharpTranspiler().Transpile(Parse(source, mainPath), isLibrary: false, sourceFilePath: mainPath);
            Assert.Contains("visible(", csharp, StringComparison.Ordinal);
            Assert.DoesNotContain("other(", csharp, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Example_SelectiveImport_Runs()
    {
        var path = Path.Combine(RepoRoot, "Examples", "Modules", "selective_import.malda");
        Assert.True(File.Exists(path));
        var source = File.ReadAllText(path);
        var statements = Parse(source, path);

        var interpreter = new Interpreter.Interpreter();
        typeof(Interpreter.Interpreter)
            .GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(interpreter, new ModuleLoader());

        RedirectConsole();
        try
        {
            await interpreter.InterpretAsync(statements);
            var lines = GetOutput().Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("5", lines[0]);
            Assert.Equal("1.0", lines[1]);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
