// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using MaldaLang.LanguageServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

var server = await LanguageServer.From(
    options =>
    {
        options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithServices(services =>
            {
                services.AddSingleton<DocumentStore>();
                services.AddSingleton<WorkspaceDocumentManager>();
                services.AddSingleton<MaldaLspTypeSettings>();
                services.AddSingleton<ILanguageService, LanguageService>();
                services.AddSingleton<ISymbolNavigationService, SymbolNavigationService>();
                services.AddSingleton<WorkspaceSymbolIndex>();
                services.AddSingleton<IDiagnosticsPublisher, DiagnosticsPublisher>();
            })
            .WithHandler<MaldaTextDocumentSyncHandler>()
            .WithHandler<MaldaCompletionHandler>()
            .WithHandler<MaldaHoverHandler>()
            .WithHandler<MaldaDocumentSymbolHandler>()
            .WithHandler<MaldaDefinitionHandler>()
            .WithHandler<MaldaReferencesHandler>()
            .WithHandler<MaldaRenameHandler>()
            .WithHandler<MaldaPrepareRenameHandler>()
            .WithHandler<MaldaDocumentHighlightHandler>()
            .WithHandler<MaldaCodeActionHandler>()
            .WithHandler<MaldaSignatureHelpHandler>()
            .WithHandler<MaldaDocumentFormattingHandler>()
            .WithHandler<MaldaWorkspaceSymbolHandler>()
            .WithHandler<MaldaConfigurationHandler>()
            .OnInitialize((server, request, token) =>
            {
                var typeSettings = server.Services.GetService(typeof(MaldaLspTypeSettings)) as MaldaLspTypeSettings;
                typeSettings?.ApplyFromInitializationOptions(request.InitializationOptions);
                return Task.CompletedTask;
            })
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    });

// Handlers are resolved during From() before the server exists; set the diagnostics publisher so they can publish.
if (server is OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade facade)
    DiagnosticsPublisher.InnerTextDocument = facade.TextDocument;

await server.WaitForExit;
