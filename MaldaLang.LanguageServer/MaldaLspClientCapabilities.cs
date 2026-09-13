// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System;
using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Client declarations the server has to respect, captured at initialization. Currently
/// which <c>workspace/applyEdit</c> resource operations the client can apply, so a quick
/// fix is only offered when the client would carry it out.
/// </summary>
public sealed class MaldaLspClientCapabilities
{
    private readonly HashSet<string> _resourceOperations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resource operations from <c>workspace.workspaceEdit.resourceOperations</c>, as
    /// declared. Empty when the client declares no <c>workspaceEdit</c> capability, in
    /// which case it only applies plain text edits.
    /// </summary>
    public IReadOnlyCollection<string> ResourceOperations => _resourceOperations;

    /// <summary>True when the client declared it can apply the given resource operation.</summary>
    public bool SupportsResourceOperation(ResourceOperationKind operation)
    {
        return _resourceOperations.Contains(operation.ToString());
    }

    public void Apply(ClientCapabilities? clientCapabilities)
    {
        _resourceOperations.Clear();

        var workspace = clientCapabilities?.Workspace;
        if (workspace == null)
        {
            return;
        }

        var resourceOperations = workspace.WorkspaceEdit.Value?.ResourceOperations;
        if (resourceOperations == null)
        {
            return;
        }

        foreach (var operation in resourceOperations)
        {
            if (operation == null)
            {
                continue;
            }

            _resourceOperations.Add(operation.ToString());
        }
    }
}
