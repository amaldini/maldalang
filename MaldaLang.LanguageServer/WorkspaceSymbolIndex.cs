// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using MaldaLang.IDE.Services;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Index of top-level symbols (class, function, actor, prompt, workflow) per document for workspace/symbol.
/// </summary>
public class WorkspaceSymbolIndex
{
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public WorkspaceSymbolIndex()
        : this(new WorkspaceDocumentManager(), new SymbolNavigationService())
    {
    }

    public WorkspaceSymbolIndex(WorkspaceDocumentManager workspaceDocuments, ISymbolNavigationService symbolNavigationService)
    {
        _workspaceDocuments = workspaceDocuments;
        _symbolNavigationService = symbolNavigationService;
    }

    public void Update(DocumentUri uri, string text)
    {
        _workspaceDocuments.SetOpenDocument(uri, text);
    }

    public void Remove(DocumentUri uri)
    {
        _workspaceDocuments.RemoveOpenDocument(uri);
    }

    public List<SymbolInformation> GetSymbols(string? query)
    {
        var documents = _workspaceDocuments.GetWorkspaceDocuments();
        return _symbolNavigationService
            .GetWorkspaceSymbols(documents, query)
            .Select(ToSymbolInformation)
            .ToList();
    }

    private SymbolInformation ToSymbolInformation(MaldaLang.IDE.Models.WorkspaceSymbolInfo symbol)
    {
        return new SymbolInformation
        {
            Name = symbol.Name,
            Kind = SymbolNavigationLspMapper.ToSymbolKind(symbol.Kind),
            ContainerName = symbol.ContainerName,
            Location = new Location
            {
                Uri = _workspaceDocuments.CreateDocumentUri(symbol.Location.SourceKey),
                Range = SymbolNavigationLspMapper.ToRange(symbol.Location.Span)
            }
        };
    }
}
