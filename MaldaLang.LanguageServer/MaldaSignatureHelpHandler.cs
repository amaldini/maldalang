// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Handles textDocument/signatureHelp: show function signature and active parameter at call site.
/// </summary>
public class MaldaSignatureHelpHandler : ISignatureHelpHandler
{
    private readonly DocumentStore _store;

    public MaldaSignatureHelpHandler(DocumentStore store)
    {
        _store = store;
    }

    public SignatureHelpRegistrationOptions GetRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector,
            TriggerCharacters = new Container<string>("(", ",")
        };
    }

    public Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        try
        {
            var (line0, char0) = (request.Position.Line, request.Position.Character);
            var lines = text.Split('\n');
            if (line0 < 0 || line0 >= lines.Length)
            {
                return Task.FromResult<SignatureHelp?>(null);
            }

            // Find the call that contains the cursor: innermost '(' such that cursor is before its matching ')'
            var openParen = FindCallOpenParen(lines, line0, char0);
            if (openParen == null)
            {
                return Task.FromResult<SignatureHelp?>(null);
            }

            var (callLine, callCol, name) = openParen.Value;
            var activeParam = CountCommasBeforePosition(lines, callLine, callCol, line0, char0);

            // Resolve callee: user function from AST or built-in
            if (!_store.TryGetTokens(uri, cancellationToken, out var tokens) || tokens == null)
            {
                return Task.FromResult<SignatureHelp?>(null);
            }
            var parser = new Parser(tokens);
            var statements = parser.Parse();
            cancellationToken.ThrowIfCancellationRequested();

            List<string>? parameters = FindFunctionParameters(statements, name);
            if (parameters == null)
            {
                parameters = GetBuiltInParameters(name);
            }

            if (parameters == null)
            {
                return Task.FromResult<SignatureHelp?>(null);
            }

            var sigLabel = name + "(" + string.Join(", ", parameters) + ")";
            var paramInfos = parameters.ConvertAll(p => new ParameterInformation { Label = p });
            var sigInfo = new SignatureInformation
            {
                Label = sigLabel,
                Parameters = new Container<ParameterInformation>(paramInfos)
            };

            var activeParamIndex = Math.Min(activeParam, parameters.Count > 0 ? parameters.Count - 1 : 0);
            return Task.FromResult<SignatureHelp?>(new SignatureHelp
            {
                Signatures = new Container<SignatureInformation>(new[] { sigInfo }),
                ActiveSignature = 0,
                ActiveParameter = activeParamIndex
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }
        catch
        {
            return Task.FromResult<SignatureHelp?>(null);
        }
    }

    /// <summary>
    /// Finds the '(' that starts the call containing (line0, char0). Returns (line, col, calleeName) or null.
    /// Scans backward from cursor, balancing parens; when at depth 0 we see '(', take identifier before it.
    /// </summary>
    private static (int line, int col, string name)? FindCallOpenParen(string[] lines, int line0, int char0)
    {
        var line = line0;
        var col = char0;
        if (line < 0 || line >= lines.Length) return null;
        if (col > lines[line].Length) col = lines[line].Length;

        var depth = 0;
        for (var iter = 0; iter < 2000; iter++)
        {
            if (col <= 0)
            {
                line--;
                if (line < 0) return null;
                col = lines[line].Length;
                continue;
            }
            col--;
            var c = lines[line][col];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0)
                {
                    var parenCol = col;
                    while (col > 0 && (char.IsLetterOrDigit(lines[line][col - 1]) || lines[line][col - 1] == '_'))
                        col--;
                    var name = lines[line].Substring(col, parenCol - col).Trim();
                    if (name.Length > 0)
                        return (line, parenCol, name);
                    return null;
                }
                depth--;
            }
        }
        return null;
    }

    private static int CountCommasBeforePosition(string[] lines, int openLine, int openCol, int line0, int char0)
    {
        var count = 0;
        var depth = 0;
        for (var L = openLine; L <= line0; L++)
        {
            var line = lines[L];
            var start = (L == openLine) ? openCol + 1 : 0;
            var end = (L == line0) ? char0 : line.Length;
            for (var i = start; i < end && i < line.Length; i++)
            {
                var c = line[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && c == ',') count++;
            }
        }
        return count;
    }

    private static List<string>? FindFunctionParameters(List<Statement> statements, string name)
    {
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclaration fd && fd.Name == name)
            {
                return fd.Parameters;
            }
            if (stmt is PromptDeclaration pd && pd.Name == name)
            {
                return pd.Parameters;
            }
        }
        return null;
    }

    private static List<string>? GetBuiltInParameters(string name)
    {
        // Minimal list for common built-ins; extend as needed
        return name switch
        {
            "print" => new List<string> { "value" },
            "sum" => new List<string> { "values" },
            "average" => new List<string> { "values" },
            "max" => new List<string> { "valueOrValues", "other?" },
            "min" => new List<string> { "valueOrValues", "other?" },
            "getSymbols" => new List<string> { "sourceOrFilePath" },
            "getParseErrors" => new List<string> { "sourceOrFilePath" },
            "createGetParseErrorsTool" => new List<string> { "workingDirectory?" },
            "checkMalda" => new List<string> { "sourceOrFilePath", "typeMode?", "workingDir?" },
            "createCheckMaldaTool" => new List<string> { "workingDirectory?" },
            "createDeleteFileTool" => new List<string> { "workingDirectory?" },
            "createCopyFileTool" => new List<string> { "workingDirectory?" },
            "createEnsureDirTool" => new List<string> { "workingDirectory?" },
            "formatNumber" => new List<string> { "value", "format" },
            "string" => new List<string> { "value" },
            "sleep" => new List<string> { "milliseconds" },
            "uiOnInit" => new List<string> { "componentId", "sessionId?" },
            "uiOnPreRender" => new List<string> { "componentId", "sessionId?" },
            "uiOnLoad" => new List<string> { "componentId", "sessionId?" },
            "uiOnDispose" => new List<string> { "componentId", "sessionId?" },
            "uiOnMount" => new List<string> { "componentId", "sessionId?" },
            "uiOnUpdate" => new List<string> { "componentId", "sessionId?" },
            "uiOnUnmount" => new List<string> { "componentId", "sessionId?" },
            "uiOnError" => new List<string> { "componentId", "sessionId?" },
            "onInit" => new List<string> { "componentId", "sessionId?" },
            "onPreRender" => new List<string> { "componentId", "sessionId?" },
            "onLoad" => new List<string> { "componentId", "sessionId?" },
            "onDispose" => new List<string> { "componentId", "sessionId?" },
            "onMount" => new List<string> { "componentId", "sessionId?" },
            "onUpdate" => new List<string> { "componentId", "sessionId?" },
            "onUnmount" => new List<string> { "componentId", "sessionId?" },
            "onError" => new List<string> { "componentId", "sessionId?" },
            _ => new List<string> { "..." }
        };
    }
}
