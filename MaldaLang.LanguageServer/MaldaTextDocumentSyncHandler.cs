// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using MaldaLang.IDE.Services;
using MaldaLang.IDE.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using Unit = MediatR.Unit;

/// <summary>
/// Handles textDocument/didOpen, didChange, didClose and publishes diagnostics (debounced).
/// </summary>
public class MaldaTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentStore _store;
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ILanguageService _languageService;
    private readonly IDiagnosticsPublisher _diagnosticsPublisher;
    private readonly WorkspaceSymbolIndex _workspaceSymbolIndex;
    private readonly MaldaLspTypeSettings _typeSettings;
    private readonly ConcurrentDictionary<DocumentUri, CancellationTokenSource> _diagnosticCancellation = new();
    private readonly ConcurrentDictionary<DocumentUri, CancellationTokenSource> _workspaceDiagnosticCancellation = new();

    public MaldaTextDocumentSyncHandler(
        DocumentStore store,
        WorkspaceDocumentManager workspaceDocuments,
        ILanguageService languageService,
        IDiagnosticsPublisher diagnosticsPublisher,
        WorkspaceSymbolIndex workspaceSymbolIndex,
        MaldaLspTypeSettings typeSettings)
    {
        _store = store;
        _workspaceDocuments = workspaceDocuments;
        _languageService = languageService;
        _diagnosticsPublisher = diagnosticsPublisher;
        _workspaceSymbolIndex = workspaceSymbolIndex;
        _typeSettings = typeSettings;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, MaldaLspDocuments.LanguageId);
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false }
        };
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Unit.Value);
        }

        var uri = request.TextDocument.Uri;
        var text = request.TextDocument.Text ?? string.Empty;
        _store.Set(uri, text);
        _workspaceDocuments.SetOpenDocument(uri, text);
        _workspaceSymbolIndex.Update(uri, text);
        SchedulePublishDiagnostics(uri, 0);
        SchedulePublishWorkspaceDiagnostics(uri, 200);
        return Task.FromResult(Unit.Value);
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Unit.Value);
        }

        var uri = request.TextDocument.Uri;
        // Full sync: take the full text from the first change if available
        var change = request.ContentChanges.FirstOrDefault();
        if (change != null)
        {
            var fullText = change.Text;
            if (fullText != null)
            {
                _store.Set(uri, fullText);
                _workspaceDocuments.SetOpenDocument(uri, fullText);
                _workspaceSymbolIndex.Update(uri, fullText);
                SchedulePublishDiagnostics(uri, 300);
                SchedulePublishWorkspaceDiagnostics(uri, 900);
            }
        }
        return Task.FromResult(Unit.Value);
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Unit.Value);
        }

        var uri = request.TextDocument.Uri;
        _store.Remove(uri);
        _workspaceDocuments.RemoveOpenDocument(uri);
        _workspaceSymbolIndex.Remove(uri);
        CancelDiagnosticSchedule(uri);
        CancelWorkspaceDiagnosticSchedule(uri);
        // Clear diagnostics for closed document
        PublishDiagnostics(uri, new Container<LspDiagnostic>());
        SchedulePublishWorkspaceDiagnostics(uri, 0);
        return Task.FromResult(Unit.Value);
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Unit.Value);
        }

        PublishDiagnosticsForWorkspace(request.TextDocument.Uri, cancellationToken);
        return Task.FromResult(Unit.Value);
    }

    private void SchedulePublishDiagnostics(DocumentUri uri, int delayMs)
    {
        CancelDiagnosticSchedule(uri);
        var cts = new CancellationTokenSource();
        _diagnosticCancellation[uri] = cts;
        _ = Task.Run(async () =>
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
            if (cts.Token.IsCancellationRequested)
                return;
            PublishDiagnosticsForDocument(uri, cts.Token);
            _diagnosticCancellation.TryRemove(uri, out _);
        }, cts.Token);
    }

    private void SchedulePublishWorkspaceDiagnostics(DocumentUri uri, int delayMs)
    {
        CancelWorkspaceDiagnosticSchedule(uri);
        var cts = new CancellationTokenSource();
        _workspaceDiagnosticCancellation[uri] = cts;
        _ = Task.Run(async () =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
            }

            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            PublishDiagnosticsForWorkspace(uri, cts.Token);
            _workspaceDiagnosticCancellation.TryRemove(uri, out _);
        }, cts.Token);
    }

    private void PublishDiagnostics(DocumentUri uri, Container<LspDiagnostic> diagnostics)
    {
        _diagnosticsPublisher.Publish(uri, diagnostics);
    }

    private void CancelDiagnosticSchedule(DocumentUri uri)
    {
        if (_diagnosticCancellation.TryRemove(uri, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }
    }

    private void CancelWorkspaceDiagnosticSchedule(DocumentUri uri)
    {
        if (_workspaceDiagnosticCancellation.TryRemove(uri, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }
    }

    private void PublishDiagnosticsForDocument(DocumentUri uri, CancellationToken cancellationToken)
    {
        var text = _store.Get(uri);
        if (text == null)
            return;
        if (cancellationToken.IsCancellationRequested)
            return;

        // Full local path so ModuleSymbolResolver can resolve relative imports for type analysis.
        var sourcePath = ResolveSourcePath(uri);
        List<MaldaLang.IDE.Models.Diagnostic> maldaDiagnostics;
        try
        {
            maldaDiagnostics = _languageService.GetDiagnostics(
                text,
                sourcePath,
                cancellationToken,
                _typeSettings.ToOptions());
        }
        catch (OperationCanceledException)
        {
            return;
        }
        var lspDiagnostics = maldaDiagnostics.Select(d => ToLspDiagnostic(d)).ToList();

        PublishDiagnostics(uri, new Container<LspDiagnostic>(lspDiagnostics));
    }

    private void PublishDiagnosticsForWorkspace(DocumentUri uri, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaldaLang.IDE.Models.WorkspaceDocumentInfo> documents;
        try
        {
            documents = _workspaceDocuments.GetWorkspaceDocumentsFor(uri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var typeOptions = _typeSettings.ToOptions();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<MaldaLang.IDE.Models.Diagnostic> diagnostics;
            try
            {
                diagnostics = _languageService.GetDiagnostics(
                    document.Text,
                    document.SourceKey,
                    cancellationToken,
                    typeOptions);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            var lspDiagnostics = diagnostics.Select(ToLspDiagnostic).ToList();
            PublishDiagnostics(
                _workspaceDocuments.CreateDocumentUri(document.SourceKey),
                new Container<LspDiagnostic>(lspDiagnostics));
        }
    }

    private static LspDiagnostic ToLspDiagnostic(MaldaLang.IDE.Models.Diagnostic d)
    {
        var severity = d.Severity switch
        {
            MaldaLang.IDE.Models.DiagnosticSeverity.Error => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
            MaldaLang.IDE.Models.DiagnosticSeverity.Warning => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
            MaldaLang.IDE.Models.DiagnosticSeverity.Info => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
            _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error
        };
        var range = LspPositionHelper.ToRange(d.Line, d.Column, d.Length > 0 ? d.Length : 1);
        return new LspDiagnostic
        {
            Range = range,
            Severity = severity,
            Message = d.Message,
            Source = d.Source ?? "parser"
        };
    }

    private static string? ResolveSourcePath(DocumentUri uri)
    {
        try
        {
            var uriString = uri.ToString();
            if (Uri.TryCreate(uriString, UriKind.Absolute, out var created) && created.IsFile)
                return created.LocalPath;
        }
        catch
        {
            // fall through
        }

        return uri.Path;
    }
}
