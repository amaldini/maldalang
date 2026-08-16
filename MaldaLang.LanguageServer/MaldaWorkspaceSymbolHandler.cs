// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

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

    public WorkspaceSymbolRegistrationOptions GetRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities)
    {
        return new WorkspaceSymbolRegistrationOptions();
    }

    public Task<Container<WorkspaceSymbol>> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new Container<WorkspaceSymbol>());
        }

        var symbols = _index.GetSymbols(request.Query)
            .Select(ToWorkspaceSymbol);
        return Task.FromResult(new Container<WorkspaceSymbol>(symbols));
    }

    private static WorkspaceSymbol ToWorkspaceSymbol(SymbolInformation symbol)
    {
        return new WorkspaceSymbol
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            ContainerName = symbol.ContainerName,
            Location = symbol.Location
        };
    }
}
