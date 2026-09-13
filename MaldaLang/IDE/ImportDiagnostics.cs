// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using MaldaLang.IDE.Models;

/// <summary>
/// Loud replacement for <see cref="ModuleSymbolResolver"/>'s best-effort catch: a
/// <c>include</c> / file <c>import</c> whose target is missing used to degrade to an
/// empty symbol set with no feedback. The parser already throws for a missing
/// <c>include</c>; this covers file imports and makes the IDE / <c>malda check</c> loop
/// report both before the program runs.
/// </summary>
public static class ImportDiagnostics
{
    public const string Source = "malda-import";

    public static void Validate(
        string source,
        string? sourceFileName,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        try
        {
            foreach (var (module, resolvedPath) in ImportedModuleResolver.CollectUnresolvedModules(
                source,
                sourceFileName,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(Create(module, resolvedPath));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Diagnostics must never take down the language service.
        }
    }

    public static Diagnostic Create(ImportedModuleResolver.ImportedModuleReference module, string resolvedPath)
    {
        var keyword = module.IsInclude ? "include" : "import";
        return new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Source = Source,
            Message =
                $"{(module.IsInclude ? "Included" : "Imported")} module not found: '{module.ModulePath}'. " +
                $"Resolved to '{resolvedPath}'. The {keyword} is ignored by tooling; the program fails when it runs.",
            Line = module.Line,
            Column = module.Column,
            Length = module.Length,
            SuggestedFix = $"Create {resolvedPath} or fix the path in the {keyword} statement."
        };
    }
}
