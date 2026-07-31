// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Compatibility shim for OmniSharp LSP 0.19.9 types not exposed in referenced assemblies.
// These types mirror the OmniSharp.Extensions.LanguageServer handler interfaces and options.

using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>Save options for text document sync (compatibility type).</summary>
public class SaveOptions
{
    public bool IncludeText { get; set; }
}

/// <summary>Text document sync options (compatibility type).</summary>
public class TextDocumentSyncOptions
{
    public int Change { get; set; }  // 1 = Full, matches TextDocumentSyncKind.Full
    public bool OpenClose { get; set; }
    public SaveOptions? Save { get; set; }
}

/// <summary>Text document sync handler (compatibility interface).</summary>
public interface ITextDocumentSyncHandler : IJsonRpcHandler
{
    TextDocumentSyncOptions Options { get; }
    Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken);
    Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken);
    Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken);
    Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken);
}

/// <summary>Completion handler (compatibility interface).</summary>
public interface ICompletionHandler : IJsonRpcHandler
{
    Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken);
}

/// <summary>Hover handler (compatibility interface).</summary>
public interface IHoverHandler : IJsonRpcHandler
{
    Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken);
}

/// <summary>Document symbol handler (compatibility interface).</summary>
public interface IDocumentSymbolHandler : IJsonRpcHandler
{
    Task<Container<DocumentSymbol>?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken);
}

/// <summary>Definition handler (compatibility interface).</summary>
public interface IDefinitionHandler : IJsonRpcHandler
{
    Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken);
}

/// <summary>References handler (compatibility interface).</summary>
public interface IReferencesHandler : IJsonRpcHandler
{
    Task<Container<Location>?> Handle(ReferenceParams request, CancellationToken cancellationToken);
}

/// <summary>Rename handler (compatibility interface).</summary>
public interface IRenameHandler : IJsonRpcHandler
{
    Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken);
}

/// <summary>Prepare rename handler (compatibility interface).</summary>
public interface IPrepareRenameHandler : IJsonRpcHandler
{
    Task<LspRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken);
}

/// <summary>Code action handler (compatibility interface).</summary>
public interface ICodeActionHandler : IJsonRpcHandler
{
    Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken);
}

/// <summary>Signature help handler (compatibility interface).</summary>
public interface ISignatureHelpHandler : IJsonRpcHandler
{
    Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken);
}

/// <summary>Document formatting handler (compatibility interface).</summary>
public interface IDocumentFormattingHandler : IJsonRpcHandler
{
    Task<Container<TextEdit>?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken);
}

/// <summary>Document range formatting handler (compatibility interface).</summary>
public interface IDocumentRangeFormattingHandler : IJsonRpcHandler
{
    Task<Container<TextEdit>?> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken);
}

/// <summary>Workspace symbols handler (compatibility interface).</summary>
public interface IWorkspaceSymbolsHandler : IJsonRpcHandler
{
    Task<Container<SymbolInformation>> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken);
}

/// <summary>Document highlight handler (compatibility interface).</summary>
public interface IDocumentHighlightHandler : IJsonRpcHandler
{
    Task<Container<DocumentHighlight>?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken);
}
