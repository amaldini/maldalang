// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>
/// Handles textDocument/rename: return WorkspaceEdit with TextEdit for all references (single-file).
/// </summary>
public class MaldaRenameHandler : IRenameHandler
{
    private readonly DocumentStore _store;
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaRenameHandler(DocumentStore store)
        : this(store, new WorkspaceDocumentManager(), new SymbolNavigationService())
    {
    }

    public MaldaRenameHandler(DocumentStore store, WorkspaceDocumentManager workspaceDocuments, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _workspaceDocuments = workspaceDocuments;
        _symbolNavigationService = symbolNavigationService;
    }

    public Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var newName = request.NewName;
        if (string.IsNullOrEmpty(newName))
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        try
        {
            var workspaceDocuments = _workspaceDocuments.GetWorkspaceDocumentsFor(uri, cancellationToken);
            var workspaceRenameEdits = workspaceDocuments.Count > 1
                ? _symbolNavigationService.RenameWorkspaceSymbol(workspaceDocuments, text, request.Position.Line, request.Position.Character, newName, uri.Path, cancellationToken)
                : null;
            if (workspaceRenameEdits != null && workspaceRenameEdits.Count > 0)
            {
                var workspaceChanges = workspaceRenameEdits
                    .GroupBy(edit => edit.SourceKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => _workspaceDocuments.CreateDocumentUri(group.Key),
                        group => (IEnumerable<TextEdit>)group.Select(edit => new TextEdit
                        {
                            Range = SymbolNavigationLspMapper.ToRange(edit.Span),
                            NewText = edit.NewText
                        }).ToList());

                return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit { Changes = workspaceChanges });
            }

            var renameEdits = _symbolNavigationService.Rename(text, request.Position.Line, request.Position.Character, newName, uri.Path, cancellationToken);
            if (renameEdits == null)
            {
                return Task.FromResult<WorkspaceEdit?>(null);
            }

            var edits = renameEdits.Select(edit => new TextEdit
            {
                Range = SymbolNavigationLspMapper.ToRange(edit.Span),
                NewText = edit.NewText
            }).ToList();

            if (edits.Count == 0)
            {
                return Task.FromResult<WorkspaceEdit?>(null);
            }

            var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
            {
                [uri] = edits
            };
            return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit { Changes = changes });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }
        catch
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }
    }
}
