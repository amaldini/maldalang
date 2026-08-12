// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.IDE.Services;

public class SymbolNavigationService : ISymbolNavigationService
{
    public List<DocumentSymbolInfo> GetDocumentSymbols(string source, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!TryParseStatements(source, sourceFileName, cancellationToken, out var statements))
        {
            return new List<DocumentSymbolInfo>();
        }

        var symbols = new List<DocumentSymbolInfo>();
        foreach (var stmt in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (stmt)
            {
                case ClassDeclaration classDecl:
                    symbols.Add(MakeClassSymbol(classDecl));
                    break;
                case FunctionDeclaration functionDecl:
                    symbols.Add(MakeFunctionSymbol(functionDecl));
                    break;
                case ActorDeclaration actorDecl:
                    symbols.Add(MakeActorSymbol(actorDecl));
                    break;
                case PromptDeclaration promptDecl:
                    symbols.Add(MakePromptSymbol(promptDecl));
                    break;
                case WorkflowDeclaration workflowDecl:
                    symbols.Add(MakeWorkflowSymbol(workflowDecl));
                    break;
                case SchemaDeclaration schemaDecl:
                    symbols.Add(MakeSchemaSymbol(schemaDecl));
                    break;
            }
        }

        return symbols;
    }

    public List<WorkspaceSymbolInfo> GetWorkspaceSymbols(IEnumerable<WorkspaceDocumentInfo> documents, string? query, CancellationToken cancellationToken = default)
    {
        var symbols = new List<WorkspaceSymbolInfo>();
        var trimmedQuery = (query ?? string.Empty).Trim();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseStatements(document.Text, document.SourceKey, cancellationToken, out var statements))
            {
                continue;
            }

            foreach (var stmt in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (stmt)
                {
                    case ClassDeclaration classDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, classDecl.Name, SymbolItemKind.Class, classDecl.Line, classDecl.Column, null);
                        break;
                    case FunctionDeclaration functionDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, functionDecl.Name, SymbolItemKind.Function, functionDecl.Line, functionDecl.Column, null);
                        break;
                    case ActorDeclaration actorDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, actorDecl.Name, SymbolItemKind.Actor, actorDecl.Line, actorDecl.Column, null);
                        break;
                    case PromptDeclaration promptDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, promptDecl.Name, SymbolItemKind.Prompt, promptDecl.Line, promptDecl.Column, null);
                        break;
                    case WorkflowDeclaration workflowDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, workflowDecl.Name, SymbolItemKind.Workflow, workflowDecl.Line, workflowDecl.Column, null);
                        foreach (var bodyStmt in workflowDecl.Body.Statements)
                        {
                            switch (bodyStmt)
                            {
                                case WorkflowStepStatement stepStmt:
                                    AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, stepStmt.StepId, SymbolItemKind.Step, stepStmt.Line, stepStmt.Column, workflowDecl.Name);
                                    break;
                                case WorkflowApprovalStatement approvalStmt:
                                    AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, approvalStmt.ApprovalId, SymbolItemKind.Event, approvalStmt.Line, approvalStmt.Column, workflowDecl.Name);
                                    break;
                                case WorkflowAwaitSignalStatement waitStmt:
                                    AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, waitStmt.SignalId, SymbolItemKind.Event, waitStmt.Line, waitStmt.Column, workflowDecl.Name);
                                    break;
                            }
                        }
                        break;
                    case SchemaDeclaration schemaDecl:
                        AddWorkspaceSymbol(symbols, trimmedQuery, document.SourceKey, schemaDecl.Name, SymbolItemKind.Schema, schemaDecl.Line, schemaDecl.Column, null);
                        break;
                }
            }
        }

        return symbols;
    }

    public SymbolLocation? GetWorkspaceDefinition(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        var localDefinition = GetDefinition(source, line, column, sourceFileName, cancellationToken);
        if (localDefinition != null)
        {
            return localDefinition;
        }

        if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return null;
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return null;
        }

        var declarations = FindWorkspaceDeclarations(documents, token.Lexeme, cancellationToken);
        if (declarations.Count == 1)
        {
            return declarations[0].Location;
        }

        return declarations
            .FirstOrDefault(declaration => SourceKeysEqual(declaration.SourceKey, sourceFileName))
            ?.Location;
    }

    public SymbolLocation? GetDefinition(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens) ||
            !TryParseStatements(source, sourceFileName, cancellationToken, out var statements))
        {
            return null;
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return null;
        }

        var declaration = FindDeclaration(statements, token.Lexeme);
        if (declaration == null)
        {
            return null;
        }

        return CreateLocation(sourceFileName, declaration.Value.Name, declaration.Value.Line - 1, declaration.Value.Column - 1, declaration.Value.Name.Length);
    }

    public List<SymbolLocation> GetReferences(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return new List<SymbolLocation>();
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return new List<SymbolLocation>();
        }

        var locations = new List<SymbolLocation>();
        foreach (var current in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Type == TokenType.Identifier && current.Lexeme == token.Lexeme)
            {
                locations.Add(CreateLocation(sourceFileName, current.Lexeme, current.Line - 1, current.Column - 1, current.Lexeme.Length));
            }
        }

        return locations;
    }

    public List<SymbolLocation> GetWorkspaceReferences(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        var workspaceSymbolName = ResolveWorkspaceSymbolName(documents, source, line, column, sourceFileName, cancellationToken);
        if (workspaceSymbolName == null)
        {
            return GetReferences(source, line, column, sourceFileName, cancellationToken);
        }

        return CollectWorkspaceReferences(documents, workspaceSymbolName, cancellationToken);
    }

    public List<TextSpanInfo> GetDocumentHighlights(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        return GetReferences(source, line, column, sourceFileName, cancellationToken)
            .Select(location => location.Span)
            .ToList();
    }

    public RenameTargetInfo? PrepareRename(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return null;
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return null;
        }

        return new RenameTargetInfo
        {
            Name = token.Lexeme,
            Span = new TextSpanInfo
            {
                Line = token.Line - 1,
                Column = token.Column - 1,
                Length = token.Lexeme.Length
            }
        };
    }

    public List<TextEditInfo>? Rename(string source, int line, int column, string newName, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!IsValidIdentifier(newName) ||
            !TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return null;
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return null;
        }

        var edits = new List<TextEditInfo>();
        foreach (var current in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Type == TokenType.Identifier && current.Lexeme == token.Lexeme)
            {
                edits.Add(new TextEditInfo
                {
                    Span = new TextSpanInfo
                    {
                        Line = current.Line - 1,
                        Column = current.Column - 1,
                        Length = current.Lexeme.Length
                    },
                    NewText = newName
                });
            }
        }

        return edits.Count == 0 ? null : edits;
    }

    public List<WorkspaceTextEditInfo>? RenameWorkspaceSymbol(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string newName, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        if (!IsValidIdentifier(newName))
        {
            return null;
        }

        var workspaceSymbolName = ResolveWorkspaceSymbolName(documents, source, line, column, sourceFileName, cancellationToken);
        if (workspaceSymbolName == null)
        {
            return null;
        }

        var references = CollectWorkspaceReferences(documents, workspaceSymbolName, cancellationToken);
        if (references.Count == 0)
        {
            return null;
        }

        return references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.SourceKey))
            .Select(reference => new WorkspaceTextEditInfo
            {
                SourceKey = reference.SourceKey!,
                Span = reference.Span,
                NewText = newName
            })
            .ToList();
    }

    private static DocumentSymbolInfo MakeClassSymbol(ClassDeclaration declaration)
    {
        var children = new List<DocumentSymbolInfo>();
        foreach (var member in declaration.Members)
        {
            var kind = member.Type == MemberType.Method || member.Type == MemberType.Constructor
                ? SymbolItemKind.Method
                : SymbolItemKind.Field;

            var detail = string.Empty;
            var line = declaration.Line;
            var column = declaration.Column;
            if (member.Value is FunctionDeclaration functionDeclaration)
            {
                detail = $"function {member.Name}({string.Join(", ", functionDeclaration.Parameters)})";
                line = functionDeclaration.Line;
                column = functionDeclaration.Column;
            }

            children.Add(new DocumentSymbolInfo
            {
                Name = member.Name,
                Kind = kind,
                Detail = detail,
                Span = CreateSpan(line - 1, column - 1, member.Name.Length)
            });
        }

        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Class,
            Detail = declaration.Superclass != null ? $"extends {declaration.Superclass}" : "class",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length),
            Children = children
        };
    }

    private static DocumentSymbolInfo MakeFunctionSymbol(FunctionDeclaration declaration)
    {
        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Function,
            Detail = $"function {declaration.Name}({string.Join(", ", declaration.Parameters)})",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length)
        };
    }

    private static DocumentSymbolInfo MakeActorSymbol(ActorDeclaration declaration)
    {
        var children = new List<DocumentSymbolInfo>();
        foreach (var member in declaration.Members)
        {
            var kind = member.Type == MemberType.Method || member.Type == MemberType.Constructor
                ? SymbolItemKind.Method
                : SymbolItemKind.Field;

            var detail = string.Empty;
            var line = declaration.Line;
            var column = declaration.Column;
            if (member.Value is FunctionDeclaration functionDeclaration)
            {
                detail = $"function {member.Name}({string.Join(", ", functionDeclaration.Parameters)})";
                line = functionDeclaration.Line;
                column = functionDeclaration.Column;
            }

            children.Add(new DocumentSymbolInfo
            {
                Name = member.Name,
                Kind = kind,
                Detail = detail,
                Span = CreateSpan(line - 1, column - 1, member.Name.Length)
            });
        }

        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Actor,
            Detail = "actor",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length),
            Children = children
        };
    }

    private static DocumentSymbolInfo MakePromptSymbol(PromptDeclaration declaration)
    {
        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Prompt,
            Detail = $"prompt {declaration.Name}({string.Join(", ", declaration.Parameters)})",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length)
        };
    }

    private static DocumentSymbolInfo MakeSchemaSymbol(SchemaDeclaration declaration)
    {
        var children = declaration.Fields
            .Select(field => new DocumentSymbolInfo
            {
                Name = field.Name,
                Kind = SymbolItemKind.Field,
                Detail = field.Required ? field.TypeName : $"{field.TypeName} (optional)",
                Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, field.Name.Length)
            })
            .ToList();

        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Schema,
            Detail = $"schema ({declaration.Fields.Count} field{(declaration.Fields.Count == 1 ? "" : "s")})",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length),
            Children = children
        };
    }

    private static DocumentSymbolInfo MakeWorkflowSymbol(WorkflowDeclaration declaration)
    {
        var children = new List<DocumentSymbolInfo>();
        foreach (var statement in declaration.Body.Statements)
        {
            switch (statement)
            {
                case WorkflowStepStatement stepStatement:
                    children.Add(new DocumentSymbolInfo
                    {
                        Name = stepStatement.StepId,
                        Kind = SymbolItemKind.Step,
                        Detail = BuildWorkflowStepDetail(stepStatement),
                        Span = CreateSpan(stepStatement.Line - 1, stepStatement.Column - 1, stepStatement.StepId.Length)
                    });
                    break;
                case WorkflowApprovalStatement approvalStatement:
                    children.Add(new DocumentSymbolInfo
                    {
                        Name = approvalStatement.ApprovalId,
                        Kind = SymbolItemKind.Event,
                        Detail = approvalStatement.TimeoutMs.HasValue ? $"approval timeoutMs={approvalStatement.TimeoutMs.Value}" : "approval",
                        Span = CreateSpan(approvalStatement.Line - 1, approvalStatement.Column - 1, approvalStatement.ApprovalId.Length)
                    });
                    break;
                case WorkflowAwaitSignalStatement waitStatement:
                    children.Add(new DocumentSymbolInfo
                    {
                        Name = waitStatement.SignalId,
                        Kind = SymbolItemKind.Event,
                        Detail = waitStatement.TimeoutMs.HasValue ? $"awaitSignal timeoutMs={waitStatement.TimeoutMs.Value}" : "awaitSignal",
                        Span = CreateSpan(waitStatement.Line - 1, waitStatement.Column - 1, waitStatement.SignalId.Length)
                    });
                    break;
            }
        }

        return new DocumentSymbolInfo
        {
            Name = declaration.Name,
            Kind = SymbolItemKind.Workflow,
            Detail = $"workflow({string.Join(", ", declaration.Parameters)})",
            Span = CreateSpan(declaration.Line - 1, declaration.Column - 1, declaration.Name.Length),
            Children = children
        };
    }

    private static string BuildWorkflowStepDetail(WorkflowStepStatement statement)
    {
        var detail = "step";
        if (statement.Options != null)
        {
            detail += $" retry={statement.Options.RetryCount?.ToString() ?? "0"}";
            if (statement.Options.TimeoutMs.HasValue)
            {
                detail += $" timeoutMs={statement.Options.TimeoutMs.Value}";
            }
        }

        return detail;
    }

    private static void AddWorkspaceSymbol(List<WorkspaceSymbolInfo> symbols, string query, string sourceKey, string name, SymbolItemKind kind, int line, int column, string? containerName)
    {
        if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        symbols.Add(new WorkspaceSymbolInfo
        {
            Name = name,
            Kind = kind,
            ContainerName = containerName,
            Location = CreateLocation(sourceKey, name, line - 1, column - 1, name.Length)
        });
    }

    private static SymbolLocation CreateLocation(string? sourceKey, string name, int line, int column, int length)
    {
        return new SymbolLocation
        {
            SourceKey = sourceKey,
            Name = name,
            Span = CreateSpan(line, column, length)
        };
    }

    private static TextSpanInfo CreateSpan(int line, int column, int length)
    {
        return new TextSpanInfo
        {
            Line = Math.Max(0, line),
            Column = Math.Max(0, column),
            Length = Math.Max(0, length)
        };
    }

    private static (int Line, int Column, string Name)? FindDeclaration(List<Statement> statements, string name)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ClassDeclaration classDeclaration when classDeclaration.Name == name:
                    return (classDeclaration.Line, classDeclaration.Column, classDeclaration.Name);
                case FunctionDeclaration functionDeclaration when functionDeclaration.Name == name:
                    return (functionDeclaration.Line, functionDeclaration.Column, functionDeclaration.Name);
                case ActorDeclaration actorDeclaration when actorDeclaration.Name == name:
                    return (actorDeclaration.Line, actorDeclaration.Column, actorDeclaration.Name);
                case PromptDeclaration promptDeclaration when promptDeclaration.Name == name:
                    return (promptDeclaration.Line, promptDeclaration.Column, promptDeclaration.Name);
                case WorkflowDeclaration workflowDeclaration when workflowDeclaration.Name == name:
                    return (workflowDeclaration.Line, workflowDeclaration.Column, workflowDeclaration.Name);
                case SchemaDeclaration schemaDeclaration when schemaDeclaration.Name == name:
                    return (schemaDeclaration.Line, schemaDeclaration.Column, schemaDeclaration.Name);
                case VarDeclStatement variableDeclaration when variableDeclaration.Name == name:
                    return (variableDeclaration.Line, variableDeclaration.Column, variableDeclaration.Name);
            }
        }

        return null;
    }

    private string? ResolveWorkspaceSymbolName(IEnumerable<WorkspaceDocumentInfo> documents, string source, int line, int column, string? sourceFileName, CancellationToken cancellationToken)
    {
        if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return null;
        }

        var token = FindIdentifierTokenAtPosition(tokens, line, column);
        if (token == null)
        {
            return null;
        }

        var localDefinition = GetDefinition(source, line, column, sourceFileName, cancellationToken);
        if (localDefinition != null &&
            TryParseStatements(source, sourceFileName, cancellationToken, out var statements))
        {
            var localWorkspaceDeclaration = FindWorkspaceSearchableDeclaration(statements, localDefinition.Name, localDefinition.Span);
            if (localWorkspaceDeclaration != null)
            {
                return localWorkspaceDeclaration.Name;
            }
        }

        var workspaceDeclarations = FindWorkspaceDeclarations(documents, token.Lexeme, cancellationToken);
        return workspaceDeclarations.Count == 1 ? workspaceDeclarations[0].Name : null;
    }

    private List<SymbolLocation> CollectWorkspaceReferences(IEnumerable<WorkspaceDocumentInfo> documents, string name, CancellationToken cancellationToken)
    {
        var locations = new List<SymbolLocation>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetTokens(document.Text, document.SourceKey, cancellationToken, out var tokens))
            {
                continue;
            }

            foreach (var current in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current.Type == TokenType.Identifier && current.Lexeme == name)
                {
                    locations.Add(CreateLocation(document.SourceKey, current.Lexeme, current.Line - 1, current.Column - 1, current.Lexeme.Length));
                }
            }
        }

        return locations;
    }

    private List<WorkspaceDeclarationInfo> FindWorkspaceDeclarations(IEnumerable<WorkspaceDocumentInfo> documents, string name, CancellationToken cancellationToken)
    {
        var declarations = new List<WorkspaceDeclarationInfo>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseStatements(document.Text, document.SourceKey, cancellationToken, out var statements))
            {
                continue;
            }

            foreach (var declaration in GetWorkspaceSearchableDeclarations(statements, document.SourceKey))
            {
                if (declaration.Name == name)
                {
                    declarations.Add(declaration);
                }
            }
        }

        return declarations;
    }

    private static WorkspaceDeclarationInfo? FindWorkspaceSearchableDeclaration(List<Statement> statements, string name, TextSpanInfo span)
    {
        return GetWorkspaceSearchableDeclarations(statements, sourceKey: null)
            .FirstOrDefault(declaration => declaration.Name == name && SpansEqual(declaration.Location.Span, span));
    }

    private static IEnumerable<WorkspaceDeclarationInfo> GetWorkspaceSearchableDeclarations(List<Statement> statements, string? sourceKey)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ClassDeclaration classDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, classDeclaration.Name, SymbolItemKind.Class, classDeclaration.Line, classDeclaration.Column);
                    break;
                case FunctionDeclaration functionDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, functionDeclaration.Name, SymbolItemKind.Function, functionDeclaration.Line, functionDeclaration.Column);
                    break;
                case ActorDeclaration actorDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, actorDeclaration.Name, SymbolItemKind.Actor, actorDeclaration.Line, actorDeclaration.Column);
                    break;
                case PromptDeclaration promptDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, promptDeclaration.Name, SymbolItemKind.Prompt, promptDeclaration.Line, promptDeclaration.Column);
                    break;
                case WorkflowDeclaration workflowDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, workflowDeclaration.Name, SymbolItemKind.Workflow, workflowDeclaration.Line, workflowDeclaration.Column);
                    break;
                case SchemaDeclaration schemaDeclaration:
                    yield return CreateWorkspaceDeclaration(sourceKey, schemaDeclaration.Name, SymbolItemKind.Schema, schemaDeclaration.Line, schemaDeclaration.Column);
                    break;
            }
        }
    }

    private static WorkspaceDeclarationInfo CreateWorkspaceDeclaration(string? sourceKey, string name, SymbolItemKind kind, int line, int column)
    {
        return new WorkspaceDeclarationInfo
        {
            SourceKey = sourceKey,
            Name = name,
            Kind = kind,
            Location = CreateLocation(sourceKey, name, line - 1, column - 1, name.Length)
        };
    }

    private static bool TryParseStatements(string source, string? sourceFileName, CancellationToken cancellationToken, out List<Statement> statements)
    {
        statements = new List<Statement>();
        try
        {
            if (!TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
            {
                return false;
            }

            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            statements = parser.Parse();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetTokens(string source, string? sourceFileName, CancellationToken cancellationToken, out List<Token> tokens)
    {
        tokens = new List<Token>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lexer = new Lexer(source, sourceFileName);
            tokens = lexer.Tokenize();
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Token? FindIdentifierTokenAtPosition(List<Token> tokens, int line, int column)
    {
        var line1 = line + 1;
        var column1 = column + 1;
        var token = tokens.FirstOrDefault(t =>
            t.Line == line1 &&
            t.Column <= column1 &&
            t.Column + t.Lexeme.Length >= column1);

        return token != null && token.Type == TokenType.Identifier ? token : null;
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var first = name[0];
        if (first != '_' && !char.IsLetter(first))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var current = name[i];
            if (current != '_' && !char.IsLetterOrDigit(current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SpansEqual(TextSpanInfo left, TextSpanInfo right)
    {
        return left.Line == right.Line &&
            left.Column == right.Column &&
            left.Length == right.Length;
    }

    private static bool SourceKeysEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WorkspaceDeclarationInfo
    {
        public string? SourceKey { get; set; }
        public string Name { get; set; } = string.Empty;
        public SymbolItemKind Kind { get; set; }
        public SymbolLocation Location { get; set; } = new();
    }
}
