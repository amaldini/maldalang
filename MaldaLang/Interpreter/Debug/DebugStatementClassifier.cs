// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Classifies AST statements the interpret-mode debugger may stop on.
/// Lines are 1-based; <see cref="BlockStatement"/> and declaration collect-pass
/// nodes are not stoppable.
/// </summary>
public static class DebugStatementClassifier
{
    public static bool IsStoppable(Statement stmt)
    {
        return stmt switch
        {
            VarDeclStatement => true,
            DestructuringVarDecl => true,
            AssignmentStatement => true,
            DestructuringAssignment => true,
            ExpressionStatement => true,
            ReturnStatement => true,
            ThrowStatement => true,
            IfStatement => true,
            WhileStatement => true,
            ForStatement => true,
            ForInStatement => true,
            PrintStatement => true,
            SendStatement => true,
            TryStatement => true,
            UsingStatement => true,
            UsingResourceStatement => true,
            DeferStatement => true,
            WorkflowStepStatement => true,
            WorkflowApprovalStatement => true,
            WorkflowAwaitSignalStatement => true,
            BreakStatement => true,
            ContinueStatement => true,
            BlockStatement => false,
            FunctionDeclaration => false,
            ClassDeclaration => false,
            ActorDeclaration => false,
            PromptDeclaration => false,
            TypeDeclaration => false,
            SchemaDeclaration => false,
            ApiDeclaration => false,
            WorkflowDeclaration => false,
            PropertyDeclaration => false,
            ImportStatement => false,
            IncludeStatement => false,
            _ => false
        };
    }
}
