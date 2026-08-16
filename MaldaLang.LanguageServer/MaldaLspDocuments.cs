// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Shared document selector so OmniSharp advertises static capabilities for <c>.malda</c> files.
/// </summary>
internal static class MaldaLspDocuments
{
    public const string LanguageId = "malda";

    public static TextDocumentSelector Selector { get; } = TextDocumentSelector.ForLanguage(LanguageId);
}
