// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

public class ModuleSymbolResolverTests
{
    [Fact]
    public void ExpandFileImportsForTranspile_InlinesExportedFunctions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_expand_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var libPath = Path.Combine(tempDir, "lib.malda");
            File.WriteAllText(
                libPath,
                """
                function secret() { return 0; }
                export function visible() { return 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            File.WriteAllText(
                mainPath,
                """
                import "lib.malda";
                function host() { return visible(); }
                """);

            var parser = new Parser.Parser(
                new Lexer(File.ReadAllText(mainPath), mainPath).Tokenize(),
                mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var expanded = ModuleSymbolResolver.ExpandFileImportsForTranspile(statements, mainPath);
            Assert.Contains(expanded, s => s is MaldaLang.Parser.AST.Declarations.FunctionDeclaration f && f.Name == "visible");
            Assert.DoesNotContain(expanded, s => s is ImportStatement);
            Assert.Contains(expanded, s => s is MaldaLang.Parser.AST.Declarations.FunctionDeclaration f && f.Name == "host");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadImportedSymbols_ReturnsExportedFunctionsFromFileImport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_symbols_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "mod.malda"),
                """
                export function helper() { return 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import "mod.malda";
                function main() { return helper(); }
                """;
            File.WriteAllText(mainPath, source);

            var parser = new Parser.Parser(new Lexer(source, mainPath).Tokenize(), mainPath);
            var statements = parser.Parse();

            var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, mainPath);
            Assert.Single(imported.Imports);
            Assert.Equal("helper", imported.Functions[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetSymbols_WithFileImport_IncludesImportedFunction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_getsym_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "mod.malda"),
                """
                export function helper() { return 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            File.WriteAllText(
                mainPath,
                """
                import "mod.malda";
                function main() { return helper(); }
                """);

            var result = BuiltInFunctions.CallBuiltIn(
                "getSymbols",
                new List<RuntimeValue> { RuntimeValue.String(mainPath) },
                null);

            var obj = result.AsObject();
            var imports = obj.Get("imports", null)!.AsArray();
            Assert.Single(imports);

            var functions = obj.Get("functions", null)!.AsArray();
            var names = functions
                .Select(f => f.AsObject().Get("name", null)!.AsString())
                .ToList();
            Assert.Contains("main", names);
            Assert.Contains("helper", names);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
