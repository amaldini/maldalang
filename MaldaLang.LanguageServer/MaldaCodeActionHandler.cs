// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using System.IO;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Handles textDocument/codeAction: expose GetAutoFix quick fixes as LSP Code Actions,
/// plus "create module file" for unresolved <c>include</c> / file <c>import</c> paths.
/// </summary>
public class MaldaCodeActionHandler : ICodeActionHandler
{
    private readonly DocumentStore _store;
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ILanguageService _languageService;

    public MaldaCodeActionHandler(DocumentStore store, ILanguageService languageService)
        : this(store, new WorkspaceDocumentManager(), languageService)
    {
    }

    public MaldaCodeActionHandler(DocumentStore store, WorkspaceDocumentManager workspaceDocuments, ILanguageService languageService)
    {
        _store = store;
        _workspaceDocuments = workspaceDocuments;
        _languageService = languageService;
    }

    public CodeActionRegistrationOptions GetRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector
        };
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
        var sourceKey = WorkspaceDocumentManager.GetSourceKey(uri);
        foreach (var d in request.Context.Diagnostics)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (d.Source == ImportDiagnostics.Source)
            {
                var moduleAction = TryCreateModuleFileAction(text, sourceKey, d, cancellationToken);
                if (moduleAction != null)
                {
                    actions.Add(moduleAction);
                }

                continue;
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

    /// <summary>
    /// "Create module file" for a <see cref="ImportDiagnostics"/> warning: the diagnostic
    /// names the resolved target, so the quick fix creates exactly that file. Uses a
    /// resource operation (<c>CreateFile</c>) so the client applies it without a buffer edit.
    /// </summary>
    private CodeAction? TryCreateModuleFileAction(
        string text,
        string? sourceKey,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var modules = ImportedModuleResolver.CollectModules(text, sourceKey, cancellationToken);
            if (!ImportedModuleResolver.TryGetModuleAt(modules, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character, out var module))
            {
                return null;
            }

            var resolvedPath = ImportedModuleResolver.ResolvePath(module, sourceKey);
            if (resolvedPath == null || File.Exists(resolvedPath))
            {
                return null;
            }

            var targetUri = _workspaceDocuments.CreateDocumentUri(resolvedPath);
            var documentChanges = new List<WorkspaceEditDocumentChange>
            {
                new(new CreateFile
                {
                    Uri = targetUri,
                    Options = new CreateFileOptions { Overwrite = false, IgnoreIfExists = true }
                })
            };

            return new CodeAction
            {
                Title = $"Create module file '{Path.GetFileName(resolvedPath)}'",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit { DocumentChanges = documentChanges }
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            // Quick fixes must never take down the language service.
            return null;
        }
    }
}
