// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

/// <summary>
/// Reads <c>malda.types.strict</c> from workspace configuration when the client pushes changes.
/// </summary>
public sealed class MaldaConfigurationHandler : IDidChangeConfigurationHandler
{
    private readonly MaldaLspTypeSettings _typeSettings;

    public MaldaConfigurationHandler(MaldaLspTypeSettings typeSettings)
    {
        _typeSettings = typeSettings;
    }

    public void SetCapability(DidChangeConfigurationCapability capability, ClientCapabilities clientCapabilities)
    {
        // No server-side capability flags required for type-strict settings.
    }

    public Task<Unit> Handle(DidChangeConfigurationParams request, CancellationToken cancellationToken)
    {
        try
        {
            var settings = request.Settings;
            if (settings == null)
                return Task.FromResult(Unit.Value);

            // Clients may send the whole settings tree or a section.
            var typeStrict = TryReadBool(settings, "malda", "types", "strict")
                             ?? TryReadBool(settings, "types", "strict")
                             ?? TryReadBool(settings, "typeStrict");
            _typeSettings.ApplyConfigurationValue(typeStrict);
        }
        catch
        {
            // Keep previous setting
        }

        return Task.FromResult(Unit.Value);
    }

    private static bool? TryReadBool(object? node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current == null)
                return null;

            if (current is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind != System.Text.Json.JsonValueKind.Object ||
                    !element.TryGetProperty(segment, out element))
                {
                    return null;
                }

                current = element;
                continue;
            }

            var prop = current.GetType().GetProperty(segment);
            if (prop == null)
                return null;
            current = prop.GetValue(current);
        }

        return current switch
        {
            bool b => b,
            System.Text.Json.JsonElement je when je.ValueKind is System.Text.Json.JsonValueKind.True
                or System.Text.Json.JsonValueKind.False => je.GetBoolean(),
            _ => null
        };
    }
}
