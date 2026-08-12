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
public class ExportTypeSchemaTests : TestBase
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

    private static void AttachModuleLoader(Interpreter.Interpreter interpreter)
    {
        typeof(Interpreter.Interpreter)
            .GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(interpreter, new ModuleLoader());
    }

    [Fact]
    public void Parse_ExportTypeAndSchema_SetsIsExported()
    {
        var statements = Parse("""
            export type Result = Ok(value) | Err(msg);
            export schema Contact {
                name: string;
            }
            """);
        var typeDecl = Assert.IsType<TypeDeclaration>(statements[0]);
        Assert.True(typeDecl.IsExported);
        Assert.Equal("Result", typeDecl.TypeName);
        var schemaDecl = Assert.IsType<SchemaDeclaration>(statements[1]);
        Assert.True(schemaDecl.IsExported);
        Assert.Equal("Contact", schemaDecl.Name);
    }

    [Fact]
    public void CollectExplicitExports_IncludesTypeCtorsAndSchema()
    {
        var statements = Parse("""
            export type Result = Ok(value) | Err(msg);
            export schema Contact {
                name: string;
            }
            export function helper() { return 1; }
            """);
        var exports = ModuleExports.CollectExplicitExports(statements);
        Assert.NotNull(exports);
        Assert.Contains("Result", exports!);
        Assert.Contains("Ok", exports);
        Assert.Contains("Err", exports);
        Assert.Contains("Contact", exports);
        Assert.Contains("helper", exports);
    }

    [Fact]
    public void GetExportedStatements_WithExports_RequiresExportOnTypeSchema()
    {
        var statements = Parse("""
            export function helper() { return 1; }
            type Hidden = A | B;
            schema Secret { x: string; }
            export type Visible = Ok(v) | Err(e);
            export schema Contact { name: string; }
            """);
        var exported = ModuleSymbolResolver.GetExportedStatements(statements);
        Assert.Contains(exported, s => s is FunctionDeclaration f && f.Name == "helper");
        Assert.Contains(exported, s => s is TypeDeclaration t && t.TypeName == "Visible");
        Assert.Contains(exported, s => s is SchemaDeclaration sc && sc.Name == "Contact");
        Assert.DoesNotContain(exported, s => s is TypeDeclaration t && t.TypeName == "Hidden");
        Assert.DoesNotContain(exported, s => s is SchemaDeclaration sc && sc.Name == "Secret");
    }

    [Fact]
    public async Task Runtime_SelectiveImport_ExportType_MergesConstructors()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var typeName = "Result_" + id;
        var ok = "Ok_" + id;
        var err = "Err_" + id;
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_exp_type_" + id);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                $$"""
                export type {{typeName}} = {{ok}}(value) | {{err}}(msg);
                export function secret() { return 9; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = $$"""
                import { {{typeName}} } from "lib.malda";
                var r = {{ok}}(42);
                match r {
                    case {{ok}}(v): print(v);
                    case {{err}}(m): print(m);
                }
                """;
            File.WriteAllText(mainPath, source);

            var interpreter = new Interpreter.Interpreter(currentFile: mainPath);
            AttachModuleLoader(interpreter);
            RedirectConsole();
            try
            {
                await interpreter.InterpretAsync(Parse(source, mainPath));
                Assert.Equal("42", GetOutput().Trim());
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
    public async Task Runtime_SelectiveImport_ExportSchema_ValidateWorks()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var schemaName = "Contact_" + id;
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_exp_schema_" + id);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                $$"""
                export schema {{schemaName}} {
                    name: string;
                }
                export function unused() { return 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = $$"""
                import { {{schemaName}} } from "lib.malda";
                var good = dict { "name": "Ada" };
                var check = validate("{{schemaName}}", good);
                print(check.ok);
                """;
            File.WriteAllText(mainPath, source);

            var interpreter = new Interpreter.Interpreter(currentFile: mainPath);
            AttachModuleLoader(interpreter);
            RedirectConsole();
            try
            {
                await interpreter.InterpretAsync(Parse(source, mainPath));
                Assert.Contains("true", GetOutput(), StringComparison.OrdinalIgnoreCase);
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
    public void ExpandFileImportsForTranspile_SelectiveType_InlinesTypeDecl()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var typeName = "Result_" + id;
        var ok = "Ok_" + id;
        var err = "Err_" + id;
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_exp_tp_" + id);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                $$"""
                export type {{typeName}} = {{ok}}(value) | {{err}}(msg);
                export function other() { return 2; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = $$"""
                import { {{typeName}} } from "lib.malda";
                function host() {
                    var r = {{ok}}(1);
                    return r;
                }
                """;
            File.WriteAllText(mainPath, source);

            var expanded = ModuleSymbolResolver.ExpandFileImportsForTranspile(Parse(source, mainPath), mainPath);
            Assert.Contains(expanded, s => s is TypeDeclaration t && t.TypeName == typeName);
            Assert.DoesNotContain(expanded, s => s is FunctionDeclaration f && f.Name == "other");

            var csharp = new CSharpTranspiler().Transpile(Parse(source, mainPath), isLibrary: false, sourceFilePath: mainPath);
            Assert.Contains(ok, csharp, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Example_ExportTypeSchema_Runs()
    {
        var path = Path.Combine(RepoRoot, "Examples", "Modules", "export_type_schema.malda");
        Assert.True(File.Exists(path));
        var source = File.ReadAllText(path);
        var interpreter = new Interpreter.Interpreter(currentFile: path);
        AttachModuleLoader(interpreter);
        RedirectConsole();
        try
        {
            await interpreter.InterpretAsync(Parse(source, path));
            var lines = GetOutput().Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("42", lines[0]);
            Assert.Contains("true", lines[1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
