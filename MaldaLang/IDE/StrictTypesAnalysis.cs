// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 4.3 static analysis entry point (type hints + match exhaustiveness).
/// </summary>
public static class StrictTypesAnalysis
{
    public static void Analyze(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics,
        string? sourceFileName = null)
    {
        TypeHintDiagnostics.Validate(statements, diagnostics, options, sourceFileName);
        TypeCompatibilityDiagnostics.Validate(statements, diagnostics, options, sourceFileName);
        SchemaDeclarationDiagnostics.Validate(statements, diagnostics, options, sourceFileName);
        PromptGatherDiagnostics.Validate(statements, diagnostics);
        var index = SumTypeIndex.Build(statements);
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            try
            {
                var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
                index.MergeImported(imported);
            }
            catch
            {
                // Best-effort
            }
        }

        MatchExhaustivenessDiagnostics.Validate(statements, index, options, diagnostics);
        PureEffectsDiagnostics.Validate(statements, options, diagnostics);
        BoundsDiagnostics.Validate(statements, options, diagnostics);
        ConstImmutabilityDiagnostics.Validate(statements, options, diagnostics);
    }

    public static bool HasErrors(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                return true;
        }

        return false;
    }

    public static string FormatErrorsForConsole(IEnumerable<Diagnostic> diagnostics)
    {
        var lines = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;

            var source = string.IsNullOrEmpty(diagnostic.Source) ? "strict-types" : diagnostic.Source;
            // Diagnostic.Line/Column are 0-based (LanguageService / LSP); console output is 1-based.
            lines.Add($"{source}: line {diagnostic.Line + 1}, column {diagnostic.Column + 1}: {diagnostic.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
