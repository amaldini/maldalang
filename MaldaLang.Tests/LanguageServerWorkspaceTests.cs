// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Concurrent;
using MaldaLang.IDE;
using MaldaLang.IDE.Services;
using MaldaLang.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace MaldaLang.Tests;

public class LanguageServerWorkspaceTests
{
    [Fact]
    public async Task MaldaDefinitionHandler_CrossFileFunctionUsage_ReturnsExternalDocument()
    {
        using var workspace = new TemporaryWorkspace(
            ("lib.malda", """
function sharedHelper() {
    return 1;
}
"""),
            ("main.malda", "var result = sharedHelper();"));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var symbolNavigationService = new SymbolNavigationService();
        var mainPath = workspace.GetPath("main.malda");
        var mainUri = CreateUri(mainPath);
        var mainText = File.ReadAllText(mainPath);

        store.Set(mainUri, mainText);
        workspaceDocuments.SetOpenDocument(mainUri, mainText);

        var handler = new MaldaDefinitionHandler(store, workspaceDocuments, symbolNavigationService);
        var result = await handler.Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(mainUri),
            Position = new Position(0, mainText.IndexOf("sharedHelper", StringComparison.Ordinal) + 1)
        }, CancellationToken.None);

        var location = result.FirstOrDefault();
        Assert.NotNull(location);
        Assert.EndsWith("/lib.malda", GetUriPath(location!.IsLocation ? location.Location!.Uri : location.LocationLink!.TargetUri));
    }

    [Fact]
    public async Task MaldaDefinitionHandler_MultiFileIncludeTree_ReturnsIncludedDocument()
    {
        using var workspace = new TemporaryWorkspace(
            ("ralph/00-env.malda", "var answer = 42;\n"),
            ("RalphWiggum.malda", """
include "ralph/00-env.malda";
io.print(answer);
"""));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var symbolNavigationService = new SymbolNavigationService();
        var mainPath = workspace.GetPath("RalphWiggum.malda");
        var mainUri = CreateUri(mainPath);
        var mainText = File.ReadAllText(mainPath);

        store.Set(mainUri, mainText);
        workspaceDocuments.SetOpenDocument(mainUri, mainText);

        var handler = new MaldaDefinitionHandler(store, workspaceDocuments, symbolNavigationService);
        var result = await handler.Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(mainUri),
            Position = new Position(0, mainText.IndexOf("ralph/00-env", StringComparison.Ordinal) + 1)
        }, CancellationToken.None);

        var location = result.FirstOrDefault();
        Assert.NotNull(location);
        Assert.EndsWith("/ralph/00-env.malda", GetUriPath(location!.IsLocation ? location.Location!.Uri : location.LocationLink!.TargetUri));
    }

    [Fact]
    public async Task MaldaDocumentSymbolHandler_IncludeTree_ListsModules()
    {
        using var workspace = new TemporaryWorkspace(
            ("ralph/00-env.malda", "var answer = 42;\n"),
            ("RalphWiggum.malda", """
include "ralph/00-env.malda";
function helper() {
    return 1;
}
"""));

        var store = new DocumentStore();
        var uri = CreateUri(workspace.GetPath("RalphWiggum.malda"));
        store.Set(uri, File.ReadAllText(workspace.GetPath("RalphWiggum.malda")));

        var handler = new MaldaDocumentSymbolHandler(store, new SymbolNavigationService());
        var symbols = await handler.Handle(
            new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
            CancellationToken.None);

        Assert.NotNull(symbols);
        var moduleSymbol = Assert.Single(
            symbols!,
            symbol => symbol.IsDocumentSymbol && symbol.DocumentSymbol!.Name == "00-env.malda");
        Assert.Equal(SymbolKind.Module, moduleSymbol.DocumentSymbol!.Kind);
    }

    [Fact]
    public async Task MaldaReferencesHandler_CrossFileFunction_ReturnsWorkspaceReferences()
    {
        using var workspace = new TemporaryWorkspace(
            ("lib.malda", """
function sharedHelper() {
    return 1;
}
"""),
            ("main.malda", """
var first = sharedHelper();
var second = sharedHelper();
"""));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var symbolNavigationService = new SymbolNavigationService();
        var libPath = workspace.GetPath("lib.malda");
        var mainPath = workspace.GetPath("main.malda");
        var libUri = CreateUri(libPath);
        var mainUri = CreateUri(mainPath);
        var libText = File.ReadAllText(libPath);
        var mainText = File.ReadAllText(mainPath);

        store.Set(libUri, libText);
        store.Set(mainUri, mainText);
        workspaceDocuments.SetOpenDocument(libUri, libText);
        workspaceDocuments.SetOpenDocument(mainUri, mainText);

        var handler = new MaldaReferencesHandler(store, workspaceDocuments, symbolNavigationService);
        var result = await handler.Handle(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(libUri),
            Position = new Position(0, libText.IndexOf("sharedHelper", StringComparison.Ordinal) + 1)
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count());
        Assert.Equal(2, result.Select(location => GetUriPath(location.Uri)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task MaldaRenameHandler_CrossFileFunction_ReturnsWorkspaceEditForAllFiles()
    {
        using var workspace = new TemporaryWorkspace(
            ("lib.malda", """
function sharedHelper() {
    return 1;
}
"""),
            ("main.malda", """
var first = sharedHelper();
var second = sharedHelper();
"""));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var symbolNavigationService = new SymbolNavigationService();
        var libPath = workspace.GetPath("lib.malda");
        var mainPath = workspace.GetPath("main.malda");
        var libUri = CreateUri(libPath);
        var mainUri = CreateUri(mainPath);
        var libText = File.ReadAllText(libPath);
        var mainText = File.ReadAllText(mainPath);

        store.Set(libUri, libText);
        store.Set(mainUri, mainText);
        workspaceDocuments.SetOpenDocument(libUri, libText);
        workspaceDocuments.SetOpenDocument(mainUri, mainText);

        var handler = new MaldaRenameHandler(store, workspaceDocuments, symbolNavigationService);
        var result = await handler.Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(libUri),
            Position = new Position(0, libText.IndexOf("sharedHelper", StringComparison.Ordinal) + 1),
            NewName = "renamedHelper"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Changes);
        Assert.Equal(2, result.Changes!.Count);
        Assert.Equal(3, result.Changes.Sum(change => change.Value.Count()));
    }

    [Fact]
    public async Task MaldaWorkspaceSymbolHandler_FindsSymbolsFromWorkspaceFilesOnDisk()
    {
        using var workspace = new TemporaryWorkspace(
            ("lib.malda", """
function sharedHelper() {
    return 1;
}
"""),
            ("main.malda", "var result = 1;"));

        var workspaceDocuments = new WorkspaceDocumentManager();
        var index = new WorkspaceSymbolIndex(workspaceDocuments, new SymbolNavigationService());
        var handler = new MaldaWorkspaceSymbolHandler(index);
        var mainPath = workspace.GetPath("main.malda");
        var mainUri = CreateUri(mainPath);
        index.Update(mainUri, File.ReadAllText(mainPath));

        var result = await handler.Handle(new WorkspaceSymbolParams { Query = "sharedHelper" }, CancellationToken.None);

        Assert.Contains(result, symbol => symbol.Name == "sharedHelper" && GetUriPath(GetWorkspaceSymbolUri(symbol)).EndsWith("/lib.malda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaldaTextDocumentSyncHandler_PublishesWorkspaceDiagnosticsForSiblingFiles()
    {
        using var workspace = new TemporaryWorkspace(
            ("main.malda", "var result = 1;"),
            ("broken.malda", "function broken( {"));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var symbolNavigationService = new SymbolNavigationService();
        var diagnosticsPublisher = new RecordingDiagnosticsPublisher();
        var index = new WorkspaceSymbolIndex(workspaceDocuments, symbolNavigationService);
        var handler = new MaldaTextDocumentSyncHandler(
            store,
            workspaceDocuments,
            new LanguageService(),
            diagnosticsPublisher,
            index,
            new MaldaLspTypeSettings());
        var mainPath = workspace.GetPath("main.malda");
        var brokenPath = workspace.GetPath("broken.malda");
        var mainUri = CreateUri(mainPath);
        var brokenUri = CreateUri(brokenPath);

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = mainUri,
                LanguageId = "malda",
                Version = 1,
                Text = File.ReadAllText(mainPath)
            }
        }, CancellationToken.None);

        await handler.Handle(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier(mainUri)
        }, CancellationToken.None);

        var diagnostics = await WaitForDiagnosticsAsync(diagnosticsPublisher, brokenUri);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public async Task MaldaCodeActionHandler_MissingImport_PublishesCreateFileAction()
    {
        using var workspace = new TemporaryWorkspace(
            ("main.malda", "import \"helpers/missing.malda\";\n"));

        var store = new DocumentStore();
        var workspaceDocuments = new WorkspaceDocumentManager();
        var mainPath = workspace.GetPath("main.malda");
        var mainUri = CreateUri(mainPath);
        var mainText = File.ReadAllText(mainPath);
        store.Set(mainUri, mainText);

        var handler = new MaldaCodeActionHandler(store, workspaceDocuments, new LanguageService());
        var result = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(mainUri),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 7), new Position(0, 30)),
            Context = new CodeActionContext
            {
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
                    {
                        Source = ImportDiagnostics.Source,
                        Message = "Imported module not found",
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 7), new Position(0, 30))
                    })
            }
        }, CancellationToken.None);

        Assert.NotNull(result);
        var action = Assert.Single(result!);
        Assert.True(action.IsCodeAction);
        Assert.Equal(CodeActionKind.QuickFix, action.CodeAction!.Kind);
        Assert.Contains("missing.malda", action.CodeAction.Title, StringComparison.Ordinal);

        var documentChanges = action.CodeAction.Edit!.DocumentChanges;
        Assert.NotNull(documentChanges);
        var change = Assert.Single(documentChanges!);
        Assert.True(change.IsCreateFile);
        Assert.EndsWith(
            "/helpers/missing.malda",
            change.CreateFile!.Uri.Path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaldaCodeActionHandler_ResolvableImport_NoCodeAction()
    {
        using var workspace = new TemporaryWorkspace(
            ("lib.malda", "var answer = 42;\n"),
            ("main.malda", "import \"lib.malda\";\n"));

        var store = new DocumentStore();
        var mainPath = workspace.GetPath("main.malda");
        var mainUri = CreateUri(mainPath);
        store.Set(mainUri, File.ReadAllText(mainPath));

        var handler = new MaldaCodeActionHandler(store, new WorkspaceDocumentManager(), new LanguageService());
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 7), new Position(0, 20));
        var result = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(mainUri),
            Range = range,
            Context = new CodeActionContext
            {
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
                    {
                        Source = ImportDiagnostics.Source,
                        Message = "Imported module not found",
                        Range = range
                    })
            }
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task MaldaDocumentSymbolHandler_ResolvableInclude_MarksResolvedNotMissing()
    {
        using var workspace = new TemporaryWorkspace(
            ("ralph/00-env.malda", "var answer = 42;\n"),
            ("RalphWiggum.malda", "include \"ralph/00-env.malda\";\n"));

        var store = new DocumentStore();
        var uri = CreateUri(workspace.GetPath("RalphWiggum.malda"));
        store.Set(uri, File.ReadAllText(workspace.GetPath("RalphWiggum.malda")));

        var handler = new MaldaDocumentSymbolHandler(store, new SymbolNavigationService());
        var symbols = await handler.Handle(
            new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
            CancellationToken.None);

        Assert.NotNull(symbols);
        var moduleSymbol = Assert.Single(symbols!, symbol => symbol.IsDocumentSymbol && symbol.DocumentSymbol!.Name == "00-env.malda");
        Assert.Contains("00-env.malda", moduleSymbol.DocumentSymbol!.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", moduleSymbol.DocumentSymbol!.Detail!, StringComparison.Ordinal);
    }

    private static DocumentUri CreateUri(string path)
    {
        var normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return new DocumentUri("file", "", normalizedPath, null, null, null);
    }

    private static string GetUriPath(DocumentUri uri) => NormalizeUriPath(uri).Replace('\\', '/');

    private static DocumentUri GetWorkspaceSymbolUri(WorkspaceSymbol symbol)
    {
        Assert.True(symbol.Location.IsLocation);
        Assert.NotNull(symbol.Location.Location);
        return symbol.Location.Location!.Uri;
    }

    private static async Task<Container<Diagnostic>> WaitForDiagnosticsAsync(RecordingDiagnosticsPublisher publisher, DocumentUri uri)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (publisher.TryGetDiagnostics(uri, out var diagnostics) && diagnostics.Any())
            {
                return diagnostics;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for diagnostics for {uri}. Published keys: {string.Join(", ", publisher.GetPublishedPaths())}");
    }

    private sealed class RecordingDiagnosticsPublisher : IDiagnosticsPublisher
    {
        private readonly ConcurrentDictionary<string, Container<Diagnostic>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);

        public void Publish(DocumentUri uri, Container<Diagnostic> diagnostics)
        {
            _diagnostics[NormalizeUriPath(uri)] = diagnostics;
        }

        public bool TryGetDiagnostics(DocumentUri uri, out Container<Diagnostic> diagnostics)
        {
            return _diagnostics.TryGetValue(NormalizeUriPath(uri), out diagnostics!);
        }

        public IReadOnlyCollection<string> GetPublishedPaths()
        {
            return _diagnostics.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "malda-lsp-tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace(params (string RelativePath, string Text)[] files)
        {
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(Path.Combine(_rootPath, ".git"));
            foreach (var (relativePath, text) in files)
            {
                var fullPath = GetPath(relativePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(fullPath, text);
            }
        }

        public string GetPath(string relativePath) => Path.Combine(_rootPath, relativePath);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test workspaces.
            }
        }
    }

    private static string NormalizeUriPath(DocumentUri uri)
    {
        var path = Uri.UnescapeDataString(uri.Path);
        if (path.Length > 2 && path[0] == '/' && path[2] == ':')
        {
            path = path[1..];
        }

        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(path);
    }
}
