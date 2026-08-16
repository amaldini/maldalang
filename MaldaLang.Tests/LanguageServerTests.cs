// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Linq;
using MaldaLang.IDE.Services;
using MaldaLang.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Unit tests for MaldaLang.LanguageServer components: DocumentStore, LspPositionHelper, and handlers.
/// </summary>
public class LanguageServerTests
{
    private static DocumentUri CreateUri(string path = "/test.malda") =>
        new DocumentUri("file", "", path, null, null, null);

    [Fact]
    public void DocumentStore_Set_Get_ReturnsSameText()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var text = "var x = 1;";
        store.Set(uri, text);
        Assert.Equal(text, store.Get(uri));
    }

    [Fact]
    public void DocumentStore_Get_UnknownUri_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri("/missing.malda");
        Assert.Null(store.Get(uri));
    }

    [Fact]
    public void DocumentStore_Remove_ThenGet_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "content");
        store.Remove(uri);
        Assert.Null(store.Get(uri));
    }

    [Fact]
    public void DocumentStore_TryGet_Existing_ReturnsTrueAndText()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "hello");
        var ok = store.TryGet(uri, out var text);
        Assert.True(ok);
        Assert.Equal("hello", text);
    }

    [Fact]
    public void DocumentStore_TryGet_Missing_ReturnsFalse()
    {
        var store = new DocumentStore();
        var uri = CreateUri("/missing.malda");
        var ok = store.TryGet(uri, out var text);
        Assert.False(ok);
        Assert.Null(text);
    }

    [Fact]
    public void DocumentStore_Set_OverwritesPrevious()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "first");
        store.Set(uri, "second");
        Assert.Equal("second", store.Get(uri));
    }

    [Fact]
    public void DocumentStore_TryGetTokens_CachesAndInvalidatesOnSet()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "var foo = 1;");
        var ok1 = store.TryGetTokens(uri, CancellationToken.None, out var tokens1);
        Assert.True(ok1);
        Assert.NotNull(tokens1);
        Assert.Contains(tokens1!, t => t.Lexeme == "foo");

        store.Set(uri, "var bar = 1;");
        var ok2 = store.TryGetTokens(uri, CancellationToken.None, out var tokens2);
        Assert.True(ok2);
        Assert.NotNull(tokens2);
        Assert.Contains(tokens2!, t => t.Lexeme == "bar");
        Assert.DoesNotContain(tokens2!, t => t.Lexeme == "foo");
        Assert.NotSame(tokens1, tokens2);
    }

    [Fact]
    public void LspPositionHelper_ToPosition_CreatesCorrectPosition()
    {
        var pos = LspPositionHelper.ToPosition(2, 5);
        Assert.Equal(2, (int)pos.Line);
        Assert.Equal(5, (int)pos.Character);
    }

    [Fact]
    public void LspPositionHelper_ToRange_CreatesCorrectRange()
    {
        var range = LspPositionHelper.ToRange(1, 3, 4);
        Assert.Equal(1, (int)range.Start.Line);
        Assert.Equal(3, (int)range.Start.Character);
        Assert.Equal(1, (int)range.End.Line);
        Assert.Equal(7, (int)range.End.Character);
    }

    [Fact]
    public void LspPositionHelper_ToNameRange_UsesNameLength()
    {
        var range = LspPositionHelper.ToNameRange(0, 0, "hello");
        Assert.Equal(0, (int)range.Start.Line);
        Assert.Equal(0, (int)range.Start.Character);
        Assert.Equal(5, (int)range.End.Character);
    }

    [Fact]
    public void LspPositionHelper_ToNameRange_NullName_LengthZero()
    {
        var range = LspPositionHelper.ToNameRange(0, 0, null);
        Assert.Equal(0, (int)range.End.Character);
    }

    [Fact]
    public void LanguageServerHandlers_ImplementOfficialOmniSharpInterfaces()
    {
        Assert.Contains(
            typeof(OmniSharp.Extensions.LanguageServer.Protocol.Document.ICompletionHandler),
            typeof(MaldaCompletionHandler).GetInterfaces());
        Assert.Contains(
            typeof(OmniSharp.Extensions.LanguageServer.Protocol.Document.IHoverHandler),
            typeof(MaldaHoverHandler).GetInterfaces());
        Assert.Contains(
            typeof(OmniSharp.Extensions.LanguageServer.Protocol.Document.IDefinitionHandler),
            typeof(MaldaDefinitionHandler).GetInterfaces());
        Assert.True(
            typeof(OmniSharp.Extensions.LanguageServer.Protocol.Document.TextDocumentSyncHandlerBase)
                .IsAssignableFrom(typeof(MaldaTextDocumentSyncHandler)));
    }

    [Fact]
    public async Task MaldaCompletionHandler_EmptyDocument_ReturnsEmptyList()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task MaldaCompletionHandler_ValidDocument_ReturnsCompletions()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "var x = 1;\nprint(";
        store.Set(uri, source);
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 5)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task MaldaCompletionHandler_ArrayVariableMemberCompletion_IncludesAggregationMethods()
    {
        var store = new DocumentStore();
        var uri = CreateUri("/array_completion.malda");
        var source = "var scores = [1, 2, 3];\nscores.";
        store.Set(uri, source);
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 7)
        };

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Items, item => item.Label == "sum");
        Assert.Contains(result.Items, item => item.Label == "average");
        Assert.Contains(result.Items, item => item.Label == "min");
        Assert.Contains(result.Items, item => item.Label == "max");
        Assert.Contains(result.Items, item => item.Label == "append");
        Assert.Contains(result.Items, item => item.Label == "length");
    }

    [Fact]
    public async Task MaldaCompletionHandler_ArrayProducingExpressions_RetainArrayMemberCompletion()
    {
        var store = new DocumentStore();
        var uri = CreateUri("/array_expression_completion.malda");
        var source = """
            var parts = split("a,b,c", ",");
            var doubled = [1, 2, 3].map(x => x * 2);
            function makeValues() {
                return [4, 5, 6];
            }
            var fromFn = makeValues();
            parts.
            doubled.
            fromFn.
            """;
        store.Set(uri, source);
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var lines = source.Split('\n');
        var partsLine = Array.FindIndex(lines, line => line.Contains("parts.", StringComparison.Ordinal));
        var doubledLine = Array.FindIndex(lines, line => line.Contains("doubled.", StringComparison.Ordinal));
        var fromFnLine = Array.FindIndex(lines, line => line.Contains("fromFn.", StringComparison.Ordinal));

        var partsResult = await handler.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(partsLine, lines[partsLine].IndexOf('.') + 1)
        }, CancellationToken.None);

        var doubledResult = await handler.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(doubledLine, lines[doubledLine].IndexOf('.') + 1)
        }, CancellationToken.None);

        var fromFnResult = await handler.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(fromFnLine, lines[fromFnLine].IndexOf('.') + 1)
        }, CancellationToken.None);

        Assert.Contains(partsResult.Items, item => item.Label == "sum");
        Assert.Contains(partsResult.Items, item => item.Label == "map");
        Assert.Contains(doubledResult.Items, item => item.Label == "average");
        Assert.Contains(doubledResult.Items, item => item.Label == "filter");
        Assert.Contains(fromFnResult.Items, item => item.Label == "min");
        Assert.Contains(fromFnResult.Items, item => item.Label == "max");
    }

    [Fact]
    public async Task MaldaCompletionHandler_UnknownUri_ReturnsEmptyList()
    {
        var store = new DocumentStore();
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var uri = CreateUri("/missing.malda");
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task MaldaCompletionHandler_CancelledRequest_ReturnsEmptyList()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "print(");
        var languageService = new LanguageService();
        var handler = new MaldaCompletionHandler(store, languageService);
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 6)
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.Handle(request, cts.Token);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task MaldaHoverHandler_EmptyDocument_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var languageService = new LanguageService();
        var handler = new MaldaHoverHandler(store, languageService);
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaHoverHandler_ValidDocument_MayReturnHover()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nfoo";
        store.Set(uri, source);
        var languageService = new LanguageService();
        var handler = new MaldaHoverHandler(store, languageService);
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 1)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
    }

    [Fact]
    public async Task MaldaDocumentSymbolHandler_EmptyDocument_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaDocumentSymbolHandler(store);
        var request = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(uri)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaDocumentSymbolHandler_ValidDocument_ReturnsSymbols()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nclass Bar { }";
        store.Set(uri, source);
        var handler = new MaldaDocumentSymbolHandler(store);
        var request = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(uri)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        var names = result!.Select(s => AsDocumentSymbol(s).Name).ToList();
        Assert.Contains("foo", names);
        Assert.Contains("Bar", names);
    }

    [Fact]
    public async Task MaldaDocumentSymbolHandler_InvalidSyntax_ReturnsEmptyContainer()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "{{{ unclosed");
        var handler = new MaldaDocumentSymbolHandler(store);
        var request = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(uri)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // MaldaTextDocumentSyncHandler tests require a mock ILanguageServerFacade (OmniSharp interface).
    // Consider integration tests that run the LSP server and send LSP requests to verify sync + diagnostics.

    [Fact]
    public async Task MaldaDefinitionHandler_EmptyDocument_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaDefinitionHandler(store);
        var request = new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task MaldaDefinitionHandler_DefinitionAtFunction_ReturnsLocation()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nvar x = foo();";
        store.Set(uri, source);
        var handler = new MaldaDefinitionHandler(store);
        var request = new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 9) // on "foo" in foo()
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        var first = result!.FirstOrDefault();
        Assert.NotNull(first);
        Assert.Equal(uri, first!.IsLocation ? first.Location!.Uri : first.LocationLink!.TargetUri);
    }

    [Fact]
    public async Task MaldaReferencesHandler_EmptyDocument_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaReferencesHandler(store);
        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task MaldaReferencesHandler_ValidSymbol_ReturnsReferences()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nvar x = foo();";
        store.Set(uri, source);
        var handler = new MaldaReferencesHandler(store);
        var request = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 10) // on "foo"
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        Assert.True(result!.Count() >= 2); // declaration + usage
    }

    [Fact]
    public async Task MaldaRenameHandler_EmptyDocument_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaRenameHandler(store);
        var request = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0),
            NewName = "bar"
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaRenameHandler_ValidSymbol_ReturnsWorkspaceEdit()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nvar x = foo();";
        store.Set(uri, source);
        var handler = new MaldaRenameHandler(store);
        var request = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 10),
            NewName = "bar"
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result!.Changes);
        Assert.True(result.Changes!.ContainsKey(uri));
        Assert.NotEmpty(result.Changes[uri]);
    }

    [Fact]
    public async Task MaldaRenameHandler_InvalidNewName_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "function foo() { }");
        var handler = new MaldaRenameHandler(store);
        var request = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 10),
            NewName = "123invalid"
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaPrepareRenameHandler_ValidIdentifier_ReturnsRange()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nfoo();";
        store.Set(uri, source);
        var handler = new MaldaPrepareRenameHandler(store);
        var request = new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 1)
        };

        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        var range = result!.Range;
        Assert.Equal(1, (int)range.Start.Line);
        Assert.Equal(0, (int)range.Start.Character);
        Assert.Equal(3, (int)range.End.Character);
    }

    [Fact]
    public async Task MaldaPrepareRenameHandler_InvalidTarget_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }";
        store.Set(uri, source);
        var handler = new MaldaPrepareRenameHandler(store);
        var request = new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0) // "function" keyword
        };

        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaDocumentHighlightHandler_Identifier_ReturnsHighlights()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nvar x = foo();";
        store.Set(uri, source);
        var handler = new MaldaDocumentHighlightHandler(store);
        var request = new DocumentHighlightParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 10) // on "foo"
        };

        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result!.Count() >= 2); // declaration + usage
    }

    [Fact]
    public async Task MaldaDocumentHighlightHandler_NonIdentifier_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() { return 1; }\nvar x = foo();";
        store.Set(uri, source);
        var handler = new MaldaDocumentHighlightHandler(store);
        var request = new DocumentHighlightParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 8) // on whitespace before foo
        };

        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task MaldaCodeActionHandler_NoDiagnostics_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "var x = 1;");
        var languageService = new LanguageService();
        var handler = new MaldaCodeActionHandler(store, languageService);
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 10)),
            Context = new CodeActionContext { Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>() }
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task MaldaSignatureHelpHandler_EmptyDocument_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaSignatureHelpHandler(store);
        var request = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MaldaSignatureHelpHandler_InsideCall_ReturnsSignatureHelp()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo(a, b) { return a + b; }\nfoo(1,";
        store.Set(uri, source);
        var handler = new MaldaSignatureHelpHandler(store);
        var request = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, 6) // inside foo(1,
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Signatures!);
        var firstSig = result.Signatures!.FirstOrDefault();
        Assert.NotNull(firstSig);
        Assert.Contains("foo", firstSig!.Label);
    }

    [Fact]
    public async Task MaldaDocumentFormattingHandler_EmptyDocument_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        store.Set(uri, "");
        var handler = new MaldaDocumentFormattingHandler(store);
        var request = new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Options = new FormattingOptions { TabSize = 4, InsertSpaces = true }
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task MaldaDocumentFormattingHandler_ValidDocument_MayReturnEdits()
    {
        var store = new DocumentStore();
        var uri = CreateUri();
        var source = "function foo() {\nreturn 1;\n}";
        store.Set(uri, source);
        var handler = new MaldaDocumentFormattingHandler(store);
        var request = new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Options = new FormattingOptions { TabSize = 4, InsertSpaces = true }
        };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public void WorkspaceSymbolIndex_Update_GetSymbols_ReturnsSymbols()
    {
        var index = new WorkspaceSymbolIndex();
        var uri = CreateUri();
        index.Update(uri, "function foo() { }\nclass Bar { }");
        var symbols = index.GetSymbols(null);
        Assert.NotEmpty(symbols);
        var names = symbols.Select(s => s.Name).ToList();
        Assert.Contains("foo", names);
        Assert.Contains("Bar", names);
    }

    [Fact]
    public void WorkspaceSymbolIndex_Remove_ClearsSymbols()
    {
        var index = new WorkspaceSymbolIndex();
        var uri = CreateUri();
        index.Update(uri, "function foo() { }");
        index.Remove(uri);
        var symbols = index.GetSymbols(null);
        Assert.Empty(symbols);
    }

    [Fact]
    public void WorkspaceSymbolIndex_GetSymbols_Query_Filters()
    {
        var index = new WorkspaceSymbolIndex();
        var uri = CreateUri();
        index.Update(uri, "function foo() { }\nfunction bar() { }\nclass FooClass { }");
        var symbols = index.GetSymbols("foo");
        Assert.NotEmpty(symbols);
        Assert.All(symbols, s => Assert.Contains("foo", s.Name!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaldaWorkspaceSymbolHandler_EmptyIndex_ReturnsEmpty()
    {
        var index = new WorkspaceSymbolIndex();
        var handler = new MaldaWorkspaceSymbolHandler(index);
        var request = new WorkspaceSymbolParams { Query = "" };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task MaldaWorkspaceSymbolHandler_WithIndex_ReturnsSymbols()
    {
        var index = new WorkspaceSymbolIndex();
        var uri = CreateUri();
        index.Update(uri, "function foo() { }");
        var handler = new MaldaWorkspaceSymbolHandler(index);
        var request = new WorkspaceSymbolParams { Query = "" };
        var result = await handler.Handle(request, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, s => s.Name == "foo");
    }

    [Fact]
    public async Task WorkflowSymbols_AreIndexedInDocumentAndWorkspace()
    {
        var uri = CreateUri("/workflow_symbols.malda");
        var source = """
            function doWork(x) { return x; }
            workflow Onboarding(input) {
                step provision = doWork(1) retry 2 timeout 1000;
                approval managerGate = approval("manager", {"id":1}) timeout 5000;
                wait docs = awaitSignal("docs_uploaded", {"id":1}) timeout 5000;
                return provision;
            }
            """;

        var store = new DocumentStore();
        store.Set(uri, source);
        var docHandler = new MaldaDocumentSymbolHandler(store);
        var docResult = await docHandler.Handle(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(uri)
        }, CancellationToken.None);
        Assert.NotNull(docResult);
        var workflow = docResult!.Select(AsDocumentSymbol).FirstOrDefault(s => s.Name == "Onboarding");
        Assert.NotNull(workflow);
        Assert.NotNull(workflow!.Children);
        Assert.Contains(workflow.Children!, s => s.Name == "provision");
        Assert.Contains(workflow.Children!, s => s.Name == "managerGate");
        Assert.Contains(workflow.Children!, s => s.Name == "docs");

        var index = new WorkspaceSymbolIndex();
        index.Update(uri, source);
        var wsSymbols = index.GetSymbols("Onboard");
        Assert.Contains(wsSymbols, s => s.Name == "Onboarding");
        var stepSymbols = index.GetSymbols("provision");
        Assert.Contains(stepSymbols, s => s.Name == "provision");
    }

    [Fact]
    public async Task WorkflowHover_And_DiagnosticCodes_AreSurfaced()
    {
        var store = new DocumentStore();
        var uri = CreateUri("/workflow_hover_diag.malda");
        var source = """
            workflow ReviewFlow(input) {
                step first = string(input) backoff "linear";
                return first;
            }
            """;
        store.Set(uri, source);

        var languageService = new LanguageService();
        var hoverHandler = new MaldaHoverHandler(store, languageService);
        var hover = await hoverHandler.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        }, CancellationToken.None);
        Assert.NotNull(hover);
        var markdown = hover!.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("workflow", markdown, StringComparison.OrdinalIgnoreCase);

        var diagnostics = languageService.GetDiagnostics(source, "workflow_hover_diag.malda");
        Assert.Contains(diagnostics, d => string.Equals(d.Source, "WF1004", StringComparison.Ordinal));
    }

    [Fact]
    public void LanguageService_GetDiagnostics_RejectsFnAndDefFunctionAliases()
    {
        var languageService = new LanguageService();
        var fnErrors = languageService.GetDiagnostics("fn add(a, b) { return a + b; }\n", "fn_removed.malda");
        var defErrors = languageService.GetDiagnostics("def sub(a, b) { return a - b; }\n", "def_removed.malda");

        Assert.Contains(fnErrors, d =>
            d.Severity == MaldaLang.IDE.Models.DiagnosticSeverity.Error &&
            d.Message.Contains("'fn' is not a function keyword", StringComparison.Ordinal));
        Assert.Contains(defErrors, d =>
            d.Severity == MaldaLang.IDE.Models.DiagnosticSeverity.Error &&
            d.Message.Contains("'def' is not a function keyword", StringComparison.Ordinal));
    }

    private static DocumentSymbol AsDocumentSymbol(SymbolInformationOrDocumentSymbol item)
    {
        Assert.True(item.IsDocumentSymbol);
        Assert.NotNull(item.DocumentSymbol);
        return item.DocumentSymbol!;
    }
}
