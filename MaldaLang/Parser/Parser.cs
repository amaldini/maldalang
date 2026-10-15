// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public partial class Parser
{
    private readonly List<Token> _tokens;
    private int _current = 0;
    private readonly List<ParseException> _errors = new();
    private readonly List<Token> _syntheticTokens = new();
    private readonly string? _sourceFileName;
    private readonly HashSet<string> _includeResolutionStack;
    private int _blockDepth = 0;
    private bool _inWorkflowBlock;
    private HashSet<string>? _workflowStepIds;
    
    public Parser(List<Token> tokens, string? sourceFileName = null, HashSet<string>? includeResolutionStack = null)
    {
        _tokens = tokens;
        _sourceFileName = sourceFileName;
        _includeResolutionStack = includeResolutionStack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    
    public List<ParseException> Errors => _errors;
    
    public List<Statement> Parse()
    {
        var statements = new List<Statement>();
        while (!IsAtEnd())
        {
            CollectDeclaration(statements, applySourceFile: true);
        }
        ClassAugmentation.Merge(statements, _errors, _sourceFileName);
        return statements;
    }

    /// <summary>
    /// Parser-only wrapper so a <c>const A, B;</c> list can be flattened into the
    /// enclosing statement list without introducing a <see cref="BlockStatement"/> scope.
    /// </summary>
    private sealed class StatementSequence : Statement
    {
        public List<Statement> Statements { get; }

        public StatementSequence(List<Statement> statements, int line, int column)
            : base(line, column)
        {
            Statements = statements;
        }
    }

    private void CollectDeclaration(List<Statement> statements, bool applySourceFile)
    {
        var stmt = Declaration();
        AppendFlattened(statements, stmt, applySourceFile);
    }

    private void AppendFlattened(List<Statement> statements, Statement? stmt, bool applySourceFile)
    {
        if (stmt == null)
            return;

        if (stmt is StatementSequence sequence)
        {
            foreach (var inner in sequence.Statements)
                AppendFlattened(statements, inner, applySourceFile);
            return;
        }

        if (applySourceFile)
            ApplySourceFileRecursive(stmt);

        if (stmt is IncludeStatement includeStmt)
        {
            try
            {
                statements.AddRange(ParseIncludedFile(includeStmt));
            }
            catch (ParseException ex)
            {
                _errors.Add(ex);
            }
            return;
        }

        statements.Add(stmt);
    }

    private static Statement AsEmbeddedStatement(Statement stmt)
    {
        if (stmt is StatementSequence sequence)
        {
            if (sequence.Statements.Count == 1)
                return sequence.Statements[0];
            return new BlockStatement(sequence.Statements, stmt.Line, stmt.Column);
        }

        return stmt;
    }

    private Statement EmbeddedStatement() => AsEmbeddedStatement(Statement());

    private void ApplySourceFileRecursive(Node node)
    {
        ApplySourceFileRecursive(node, new HashSet<Node>());
    }

    private void ApplySourceFileRecursive(Node? node, HashSet<Node> visited)
    {
        if (node == null || !visited.Add(node))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(node.SourceFile))
        {
            node.SourceFile = _sourceFileName;
        }

        var properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            switch (value)
            {
                case Node child:
                    ApplySourceFileRecursive(child, visited);
                    break;
                case IEnumerable sequence when value is not string:
                    foreach (var item in sequence)
                    {
                        if (item is Node childNode)
                        {
                            ApplySourceFileRecursive(childNode, visited);
                        }
                    }
                    break;
            }
        }
    }
    
    private static string RemovedFunctionAliasMessage(string lexeme) =>
        $"'{lexeme}' is not a function keyword. Use 'function'.";

    private void RejectRemovedFunctionAlias()
    {
        if (!Check(TokenType.RemovedFunctionAlias))
            return;
        var token = Peek();
        throw Error(token, RemovedFunctionAliasMessage(token.Lexeme));
    }
}
