// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>
/// Handles workspace/symbol: return symbols (classes, functions, actors, prompts) across open documents.
/// </summary>
public class MaldaWorkspaceSymbolHandler : IWorkspaceSymbolsHandler
{
    private readonly WorkspaceSymbolIndex _index;

    public MaldaWorkspaceSymbolHandler(WorkspaceSymbolIndex index)
    {
        _index = index;
    }

    public Task<Container<SymbolInformation>> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new Container<SymbolInformation>());
        }

        var symbols = _index.GetSymbols(request.Query);
        return Task.FromResult(new Container<SymbolInformation>(symbols));
    }
}
