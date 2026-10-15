// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService
{
    public SignatureHelpInfo? GetSignatureHelp(string source, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
            {
                return null;
            }

            var openParen = FindCallOpenParen(lines, line, column);
            if (openParen == null)
            {
                return null;
            }

            var (callLine, callCol, name, receiver) = openParen.Value;
            var activeParam = CountCommasBeforePosition(lines, callLine, callCol, line, column);

            var receiverType = ResolveCallReceiverType(source, receiver, line, cancellationToken);
            List<string>? parameters = NeuralCompletionCatalog.TryGetMemberParameters(receiverType, name);
            if (parameters == null)
            {
                try
                {
                    var statements = ParseStatements(source, sourceFileName: null, cancellationToken);
                    parameters = FindFunctionParameters(statements, name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Incomplete buffers still offer built-in signatures.
                }
            }

            parameters ??= NeuralCompletionCatalog.TryGetConstructorParameters(name);
            parameters ??= GetBuiltInParameters(name);
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            return new SignatureHelpInfo
            {
                SignatureLabel = $"{name}({string.Join(", ", parameters)})",
                Parameters = parameters,
                ActiveParameter = Math.Min(activeParam, parameters.Count - 1)
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveCallReceiverType(string source, string? receiver, int line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(receiver))
            return null;
        if (receiver == StdLibNamespaces.NnModule)
            return StdLibNamespaces.NnModule;

        try
        {
            var statements = ParseStatements(source, sourceFileName: null, cancellationToken);
            var resolved = FindVariableType(statements, receiver, line);
            if (resolved != null)
                return resolved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The open call may be unfinished. Declarations above the cursor still count.
        }

        if (line <= 0)
            return null;

        try
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            if (line > lines.Length)
                return null;
            var prefix = string.Join('\n', lines.Take(line));
            if (string.IsNullOrWhiteSpace(prefix))
                return null;
            var statements = ParseStatements(prefix, sourceFileName: null, cancellationToken);
            return FindVariableType(statements, receiver, line);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static (int line, int col, string name, string? receiver)? FindCallOpenParen(string[] lines, int line0, int char0)
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
                    {
                        col--;
                    }

                    var name = lines[line].Substring(col, parenCol - col).Trim();
                    if (name.Length > 0)
                    {
                        return (line, parenCol, name, ReadReceiverBefore(lines[line], col));
                    }

                    return null;
                }

                depth--;
            }
        }

        return null;
    }

    private static string? ReadReceiverBefore(string lineText, int nameStart)
    {
        var index = nameStart;
        while (index > 0 && char.IsWhiteSpace(lineText[index - 1]))
            index--;
        if (index <= 0 || lineText[index - 1] != '.')
            return null;

        index--;
        while (index > 0 && char.IsWhiteSpace(lineText[index - 1]))
            index--;
        var end = index;
        while (index > 0 && (char.IsLetterOrDigit(lineText[index - 1]) || lineText[index - 1] == '_'))
            index--;
        if (index >= end)
            return null;

        return lineText.Substring(index, end - index);
    }

    private static int CountCommasBeforePosition(string[] lines, int openLine, int openCol, int line0, int char0)
    {
        var count = 0;
        var depth = 0;
        for (var line = openLine; line <= line0; line++)
        {
            var currentLine = lines[line];
            var start = line == openLine ? openCol + 1 : 0;
            var end = line == line0 ? char0 : currentLine.Length;
            for (var i = start; i < end && i < currentLine.Length; i++)
            {
                var c = currentLine[i];
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

            if (stmt is ClassDeclaration cd)
            {
                foreach (var member in cd.Members)
                {
                    if (member.Type == MemberType.Method && member.Name == name && member.Value is FunctionDeclaration methodDecl)
                    {
                        return methodDecl.Parameters;
                    }
                }
            }
        }

        return null;
    }

    private static List<string>? GetBuiltInParameters(string name)
    {
        return name switch
        {
            "print" => new List<string> { "value" },
            "getSymbols" => new List<string> { "sourceOrFilePath" },
            "getParseErrors" => new List<string> { "sourceOrFilePath" },
            "createGetParseErrorsTool" => new List<string> { "workingDirectory?" },
            "checkMalda" => new List<string> { "sourceOrFilePath", "typeMode?", "workingDir?" },
            "createCheckMaldaTool" => new List<string> { "workingDirectory?" },
            "createValidateJsonTool" => new List<string>(),
            "createTestMaldaTool" => new List<string> { "workingDirectory?" },
            "createWebFetchTool" => new List<string>(),
            "webFetch" => new List<string> { "url", "maxBytes?", "timeoutMs?" },
            "createDeleteFileTool" => new List<string> { "workingDirectory?" },
            "createCopyFileTool" => new List<string> { "workingDirectory?" },
            "createEnsureDirTool" => new List<string> { "workingDirectory?" },
            "createSubmitPlanTool" => new List<string>(),
            "createUpdatePlanTool" => new List<string>(),
            "createMarkStepTool" => new List<string>(),
            "formatNumber" => new List<string> { "value", "format" },
            "string" => new List<string> { "value" },
            "normalizeText" => new List<string> { "text", "options?" },
            "tokenize" => new List<string> { "text", "options?" },
            "tokenOverlap" => new List<string> { "left", "right", "options?" },
            "similarity" => new List<string> { "left", "right", "method?", "options?" },
            "extractNumbers" => new List<string> { "text" },
            "sleep" => new List<string> { "milliseconds" },
            "runPrompt" => new List<string> { "prompt", "client?", "options?" },
            "evalPrompt" => new List<string> { "prompt", "fixture", "typeName?" },
            "withExamples" => new List<string> { "prompt", "examples", "options?" },
            "parseJson" => new List<string> { "value", "schemaRef", "options?" },
            "validate" => new List<string> { "schema", "value" },
            "asVariant" => new List<string> { "typeName", "value" },
            "loadDocuments" => new List<string> { "pattern", "dirPath?" },
            "splitDocuments" => new List<string> { "documents", "chunkSize?", "overlap?" },
            "formatRetrievedDocs" => new List<string> { "documents" },
            "composePipe" => new List<string> { "step1", "step2", "..." },
            "parallelRun" => new List<string> { "input", "branches" },
            "mergeRetrievedDocs" => new List<string> { "docArrays..." },
            "indexInto" => new List<string> { "vectorDb", "documents" },
            "wrap" => new List<string> { "value", "citations?" },
            "define" => new List<string> { "spec", "client?" },
            "team" => new List<string> { "specs", "topology", "client?" },
            "handoff" => new List<string> { "from", "to", "payload", "think?" },
            "review" => new List<string> { "from", "to", "payload", "think?" },
            "reject" => new List<string> { "from", "to", "payload", "think?" },
            "consult" => new List<string> { "from", "to", "payload", "think?" },
            "decompose" => new List<string> { "instruction", "client?" },
            "run" => new List<string> { "plan" },
            "decomposeTask" => new List<string> { "instruction", "client?", "team?" },
            "fileRead" => new List<string> { "path" },
            "fileWrite" => new List<string> { "path" },
            "dirList" => new List<string> { "path" },
            "httpGet" => new List<string> { "origin" },
            "mcpCall" => new List<string> { "server", "tool?" },
            "shell" => new List<string> { "prefix" },
            "confine" => new List<string> { "token", "relative" },
            "fetch" => new List<string> { "token", "urlOrPath?", "maxBytes?", "timeoutMs?" },
            "invoke" => new List<string> { "token", "server", "args?" },
            "ask" => new List<string> { "query", "maxResults?", "options?" },
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
            _ => null
        };
    }
}
