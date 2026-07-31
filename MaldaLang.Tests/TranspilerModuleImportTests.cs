// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

public class TranspilerModuleImportTests
{
    [Fact]
    public void Transpile_FileImport_InlinesExportedCallee()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_tr_imp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export function addOne(x) { return x + 1; }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                import "lib.malda";
                function main() { return addOne(41); }
                """;
            File.WriteAllText(mainPath, source);

            var parser = new Parser.Parser(new Lexer(source, mainPath).Tokenize(), mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var transpiler = new CSharpTranspiler();
            var csharp = transpiler.Transpile(statements, isLibrary: false, sourceFilePath: mainPath);

            Assert.Contains("addOne(", csharp, StringComparison.Ordinal);
            Assert.Contains("main(", csharp, StringComparison.Ordinal);
            Assert.DoesNotContain("ImportStatement", csharp, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
