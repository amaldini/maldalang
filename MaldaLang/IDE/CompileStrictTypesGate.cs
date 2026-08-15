// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;

/// <summary>
/// DT2 ship boundary: refuse C# emit when strict analysis has Errors.
/// Interpret stays dynamic. Not a full type checker.
/// </summary>
public static class CompileStrictTypesGate
{
    public static bool IsCSharpEmitMode(string compilationModeStr) =>
        compilationModeStr is "TranspileToCSharp" or "TranspileToDll";

    /// <summary>
    /// Transpile/DLL default to analysis unless <paramref name="lenientTypes"/>.
    /// <paramref name="strictTypes"/> forces analysis on any mode.
    /// Both flags together is a caller error (lenient wins only after the CLI rejects the combo).
    /// </summary>
    public static bool ShouldAnalyze(string compilationModeStr, bool strictTypes, bool lenientTypes)
    {
        if (lenientTypes)
            return false;
        if (strictTypes)
            return true;
        return IsCSharpEmitMode(compilationModeStr);
    }

    public static bool TryGetRejection(string source, string? sourceFileName, out string errorText)
    {
        errorText = "";
        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
            return false;

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics, sourceFileName);
        if (!StrictTypesAnalysis.HasErrors(diagnostics))
            return false;

        errorText = StrictTypesAnalysis.FormatErrorsForConsole(diagnostics);
        return true;
    }
}
