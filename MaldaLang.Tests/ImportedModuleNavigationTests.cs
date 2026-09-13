// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Coverage for <c>include "…"</c> / file-form <c>import "…"</c> navigation and for the
/// <c>malda-import</c> diagnostic that replaces the resolver's silent catch.
/// </summary>
public class ImportedModuleNavigationTests : TestBase
{
    private readonly SymbolNavigationService _service = new();

    [Fact]
    public void CollectModules_FindsIncludeAndFileImports()
    {
        const string source = """
include "lib.malda";
import "helpers/math_lib.malda";
import { add, VERSION } from "shared/math_utils.malda";
import malda.democontracts;
print(1);
""";

        var modules = ImportedModuleResolver.CollectModules(source, "main.malda");

        Assert.Equal(3, modules.Count);
        Assert.True(modules[0].IsInclude);
        Assert.Equal("lib.malda", modules[0].ModulePath);
        Assert.False(modules[1].IsInclude);
        Assert.Equal("helpers/math_lib.malda", modules[1].ModulePath);
        Assert.Equal("shared/math_utils.malda", modules[2].ModulePath);
    }

    [Fact]
    public void CollectModules_IgnoresStringThatIsNotAModulePath()
    {
        const string source = """
var text = "lib.malda";
io.print(text);
""";

        Assert.Empty(ImportedModuleResolver.CollectModules(source, "main.malda"));
    }

    [Fact]
    public void CollectModules_IgnoresPackageImportWithoutPath()
    {
        const string source = "import malda.democontracts;\n";

        Assert.Empty(ImportedModuleResolver.CollectModules(source, "main.malda"));
    }

    [Fact]
    public void CollectModules_SurvivesUnparseableSource()
    {
        const string source = "include \"lib.malda\";\nfunction broken( {\n";

        var modules = ImportedModuleResolver.CollectModules(source, "main.malda");

        Assert.Equal("lib.malda", Assert.Single(modules).ModulePath);
    }

    [Fact]
    public void CollectModules_SkipsVisibleStringInsideIncludedFile()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("main.malda", "include \"lib.malda\";\nio.print(answer);\n"));

        var main = File.ReadAllText(workspace.GetPath("main.malda"));

        var modules = ImportedModuleResolver.CollectModules(main, workspace.GetPath("main.malda"));

        Assert.Equal("lib.malda", Assert.Single(modules).ModulePath);
    }

    [Fact]
    public void GetDefinition_IncludePathLiteral_ReturnsTargetFile()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("main.malda", "include \"lib.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var definition = _service.GetDefinition(source, 0, 12, mainPath);

        Assert.NotNull(definition);
        Assert.Equal(mainPath.Replace("main.malda", "lib.malda"), definition!.SourceKey);
        Assert.Equal(0, definition.Span.Line);
        Assert.Equal(0, definition.Span.Column);
    }

    [Fact]
    public void GetDefinition_FileImportPathLiteral_ReturnsTargetFile()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("helpers/math_lib.malda", "var answer = 42;\n"),
            ("main.malda", "import \"helpers/math_lib.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var definition = _service.GetDefinition(source, 0, 12, mainPath);

        Assert.NotNull(definition);
        Assert.Equal(workspace.GetPath(Path.Combine("helpers", "math_lib.malda")), definition!.SourceKey);
    }

    [Fact]
    public void GetDefinition_MissingIncludeTarget_ReturnsNull()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("main.malda", "include \"nope.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        Assert.Null(_service.GetDefinition(source, 0, 12, mainPath));
    }

    [Fact]
    public void GetDefinition_AwayFromModulePath_StillResolvesLocalDeclaration()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("main.malda", "include \"lib.malda\";\nfunction helper() { return 1; }\nvar x = helper();\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var definition = _service.GetDefinition(source, 2, 9, mainPath);

        Assert.NotNull(definition);
        Assert.Equal("helper", definition!.Name);
        Assert.Equal(1, definition.Span.Line);
    }

    [Fact]
    public void GetDocumentSymbols_ListsIncludeAndImportModules()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("helpers/math_lib.malda", "var shared = 1;\n"),
            ("main.malda", """
include "lib.malda";
import "helpers/math_lib.malda";
function helper() {
    return 1;
}
"""));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var symbols = _service.GetDocumentSymbols(source, mainPath);

        var lib = Assert.Single(symbols, symbol => symbol.Name == "lib.malda");
        Assert.Equal(SymbolItemKind.Module, lib.Kind);
        Assert.Equal(0, lib.Span.Line);
        var mathLib = Assert.Single(symbols, symbol => symbol.Name == "math_lib.malda");
        Assert.Equal(1, mathLib.Span.Line);
    }

    [Fact]
    public void GetDiagnostics_MissingFileImport_IsWarningWithResolvedPath()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("main.malda", "import \"helpers/missing.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var diagnostics = new LanguageService().GetDiagnostics(source, mainPath);

        var diagnostic = Assert.Single(diagnostics, item => item.Source == ImportDiagnostics.Source);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(0, diagnostic.Line);
        Assert.Equal(7, diagnostic.Column);
        Assert.Contains("helpers/missing.malda", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(workspace.GetPath(Path.Combine("helpers", "missing.malda")), diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDiagnostics_MissingIncludeTarget_IsWarning()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("main.malda", "include \"nope.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var diagnostics = new LanguageService().GetDiagnostics(source, mainPath);

        Assert.Contains(diagnostics, item =>
            item.Source == ImportDiagnostics.Source &&
            item.Message.Contains("nope.malda", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ResolvableModules_AreQuiet()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("main.malda", "include \"lib.malda\";\nimport \"lib.malda\";\n"));

        var mainPath = workspace.GetPath("main.malda");
        var source = File.ReadAllText(mainPath);

        var diagnostics = new LanguageService().GetDiagnostics(source, mainPath);

        Assert.DoesNotContain(diagnostics, item => item.Source == ImportDiagnostics.Source);
    }

    [Fact]
    public void GetDiagnostics_UnsavedBuffer_DoesNotReportUnresolvedModules()
    {
        const string source = "import \"helpers/missing.malda\";\n";

        var diagnostics = new LanguageService().GetDiagnostics(source, "main.malda");

        Assert.DoesNotContain(diagnostics, item => item.Source == ImportDiagnostics.Source);
    }

    [Fact]
    public void GetDiagnostics_RalphWiggumIncludeTree_IsQuiet()
    {
        var mainPath = PlanningPaths.ResolveRepoPath("Examples", "RalphWiggum", "RalphWiggum.malda");
        Assert.True(File.Exists(mainPath), $"Missing RalphWiggum entry: {mainPath}");

        var source = File.ReadAllText(mainPath);
        var diagnostics = new LanguageService().GetDiagnostics(source, mainPath);

        Assert.DoesNotContain(diagnostics, item => item.Source == ImportDiagnostics.Source);
    }

    [Fact]
    public void GetDefinition_RalphWiggumIncludeTree_ResolvesFirstInclude()
    {
        var mainPath = PlanningPaths.ResolveRepoPath("Examples", "RalphWiggum", "RalphWiggum.malda");
        Assert.True(File.Exists(mainPath), $"Missing RalphWiggum entry: {mainPath}");

        var source = File.ReadAllText(mainPath);
        var modules = ImportedModuleResolver.CollectModules(source, mainPath);
        Assert.Equal(8, modules.Count);
        Assert.All(modules, module => Assert.True(module.IsInclude));

        var first = modules[0];
        var definition = _service.GetDefinition(source, first.Line, first.Column + 1, mainPath);

        Assert.NotNull(definition);
        Assert.EndsWith("00-env.malda", definition!.SourceKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckCommand_ReportsUnresolvedImportAsWarning()
    {
        var directory = CreateTempDirectory("malda_check_import_");
        try
        {
            var path = Path.Combine(directory, "main.malda");
            File.WriteAllText(path, "import \"missing_lib.malda\";\nio.print(1);\n");

            var runner = new CheckCommandRunner();
            var output = new StringWriter();
            var error = new StringWriter();
            var code = runner.Run(new[] { path, "--json" }, output, error);

            Assert.Equal(CheckCommandRunner.ExitOk, code);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("code").GetString() == ImportDiagnostics.Source &&
                              diagnostic.GetProperty("severity").GetString() == "warning");
        }
        finally
        {
            SafeDeleteDirectory(directory);
        }
    }
}
