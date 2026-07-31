// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>
/// Handles textDocument/codeAction: expose GetAutoFix quick fixes as LSP Code Actions.
/// </summary>
public class MaldaCodeActionHandler : ICodeActionHandler
{
    private readonly DocumentStore _store;
    private readonly ILanguageService _languageService;

    public MaldaCodeActionHandler(DocumentStore store, ILanguageService languageService)
    {
        _store = store;
        _languageService = languageService;
    }

    public Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());
        }

        var actions = new List<CodeAction>();
        foreach (var d in request.Context.Diagnostics)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (d.Source != "parser") continue;

            var ideDiagnostic = new MaldaLang.IDE.Models.Diagnostic
            {
                Line = d.Range.Start.Line,
                Column = d.Range.Start.Character,
                Length = d.Range.End.Line == d.Range.Start.Line
                    ? d.Range.End.Character - d.Range.Start.Character
                    : 1,
                Message = d.Message ?? "",
                Source = d.Source
            };

            AutoFixInfo? fix;
            try
            {
                fix = _languageService.GetAutoFix(text, ideDiagnostic, null, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (fix == null) continue;

            var range = new Range(
                new Position(fix.Line, fix.Column),
                new Position(fix.Line, fix.Column + fix.LengthToReplace));
            var edit = new TextEdit { Range = range, NewText = fix.TextToInsert };
            var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [uri] = new[] { edit } };
            actions.Add(new CodeAction
            {
                Title = fix.Description,
                Edit = new WorkspaceEdit { Changes = changes }
            });
        }

        var commandOrActions = actions.Select<CodeAction, CommandOrCodeAction>(a => a);
        return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer(commandOrActions));
    }
}
