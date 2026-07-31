// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;

namespace MaldaLang.IDE.Services;

public interface ISymbolNavigationService
{
    List<DocumentSymbolInfo> GetDocumentSymbols(string source, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<WorkspaceSymbolInfo> GetWorkspaceSymbols(IEnumerable<WorkspaceDocumentInfo> documents, string? query, CancellationToken cancellationToken = default);
    SymbolLocation? GetWorkspaceDefinition(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    SymbolLocation? GetDefinition(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<SymbolLocation> GetWorkspaceReferences(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<SymbolLocation> GetReferences(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<TextSpanInfo> GetDocumentHighlights(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    RenameTargetInfo? PrepareRename(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<TextEditInfo>? Rename(string source, int line, int column, string newName, string? sourceFileName = null, CancellationToken cancellationToken = default);
    List<WorkspaceTextEditInfo>? RenameWorkspaceSymbol(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string newName, string? sourceFileName = null, CancellationToken cancellationToken = default);
}
