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
using System.Collections.Generic;

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
    // Inside an @shader() body: locals may be fixed-size GLSL arrays (`var s: float[8];`).
    private bool _inShaderBody;
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

    /// <summary>
    /// Stamps <see cref="_sourceFileName"/> on every node under <paramref name="node"/> that has
    /// no file yet, including nodes held by class members, interpolation segments and
    /// object/dict literal entries (see <see cref="AstNodeWalker"/>).
    /// </summary>
    private void ApplySourceFileRecursive(Node node)
    {
        foreach (var descendant in AstNodeWalker.Descendants(node))
        {
            if (string.IsNullOrWhiteSpace(descendant.SourceFile))
                descendant.SourceFile = _sourceFileName;
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
