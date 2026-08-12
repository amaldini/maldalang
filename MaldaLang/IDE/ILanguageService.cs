// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using System.Threading;

namespace MaldaLang.IDE.Services;

public interface ILanguageService
{
    List<Diagnostic> GetDiagnostics(
        string source,
        string? sourceFileName = null,
        CancellationToken cancellationToken = default,
        MaldaLang.IDE.StrictTypesOptions? strictTypesOptions = null);
    List<CompletionItem> GetCompletions(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    SignatureHelpInfo? GetSignatureHelp(string source, int line, int column, CancellationToken cancellationToken = default);
    string? GetHoverInformation(string source, int line, int column, CancellationToken cancellationToken = default);
    AutoFixInfo? GetAutoFix(string source, Diagnostic diagnostic, MaldaLang.Parser.ParseException? parseException = null, CancellationToken cancellationToken = default);
}