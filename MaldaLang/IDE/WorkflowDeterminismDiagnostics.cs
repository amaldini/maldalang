// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Static WF1001/WF1002 checks for deny-listed built-in calls in a workflow body
/// outside <c>step</c> / <c>onReject</c>. Walks same-file user <c>function</c> callees
/// (bounded depth). Imported or unknown callees get one WF1005 Info, not a hard error.
/// Runtime still raises WF1001/WF1002 if a deny-listed built-in runs while the interpreter
/// is in a deterministic workflow section, including nested helpers.
/// </summary>
public static class WorkflowDeterminismDiagnostics
{
    internal const int MaxCalleeDepth = 16;

    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics) =>
        Validate(statements, diagnostics, sourceFileName: null);

    public static void Validate(
        IEnumerable<Statement> statements,
        List<Diagnostic> diagnostics,
        string? sourceFileName)
    {
        var list = statements as IList<Statement> ?? statements.ToList();
        var functions = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
        var dataConstructors = new HashSet<string>(StringComparer.Ordinal);
        CollectSameFileSymbols(list, functions, dataConstructors);

        var importedNames = new HashSet<string>(StringComparer.Ordinal);
        CollectImportedCalleeNames(list, sourceFileName, importedNames);

        foreach (var stmt in list)
        {
            if (stmt is WorkflowDeclaration workflow)
            {
                var walker = new Walker(
                    functions,
                    dataConstructors,
                    importedNames,
                    diagnostics);
                walker.VisitStatement(workflow.Body, checkCalls: true, depth: 0);
            }
        }
    }

    private static void CollectSameFileSymbols(
        IList<Statement> statements,
        Dictionary<string, FunctionDeclaration> functions,
        HashSet<string> dataConstructors)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case FunctionDeclaration func:
                    functions[func.Name] = func;
                    break;
                case TypeDeclaration typeDecl:
                    foreach (var ctor in typeDecl.Constructors)
                        dataConstructors.Add(ctor.Name);
                    break;
            }
        }
    }

    private static void CollectImportedCalleeNames(
        IList<Statement> statements,
        string? sourceFileName,
        HashSet<string> importedNames)
    {
        foreach (var stmt in statements)
        {
            if (stmt is ImportStatement import && import.SelectedNames != null)
            {
                foreach (var name in import.SelectedNames)
                    importedNames.Add(name);
            }
        }

        try
        {
            var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
            foreach (var func in imported.Functions)
                importedNames.Add(func.Name);
        }
        catch
        {
            // Best-effort: selected import names above are enough for WF1005.
        }
    }

    private sealed class Walker
    {
        private readonly Dictionary<string, FunctionDeclaration> _functions;
        private readonly HashSet<string> _dataConstructors;
        private readonly HashSet<string> _importedNames;
        private readonly List<Diagnostic> _diagnostics;
        private readonly List<string> _visiting = new();
        private readonly HashSet<string> _reportedUnknown = new(StringComparer.Ordinal);
        private readonly HashSet<(string Source, int Line, int Column)> _reportedErrors = new();

        public Walker(
            Dictionary<string, FunctionDeclaration> functions,
            HashSet<string> dataConstructors,
            HashSet<string> importedNames,
            List<Diagnostic> diagnostics)
        {
            _functions = functions;
            _dataConstructors = dataConstructors;
            _importedNames = importedNames;
            _diagnostics = diagnostics;
        }

        public void VisitStatement(Statement statement, bool checkCalls, int depth)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                        VisitStatement(inner, checkCalls, depth);
                    break;

                case WorkflowStepStatement:
                    // Step call + compensate run under step rules — not checked.
                    break;

                case WorkflowApprovalStatement approval:
                    VisitExpression(approval.ApprovalNameExpr, checkCalls, depth);
                    VisitExpression(approval.PayloadExpr, checkCalls, depth);
                    // onReject runs under step rules.
                    break;

                case WorkflowAwaitSignalStatement wait:
                    VisitExpression(wait.SignalNameExpr, checkCalls, depth);
                    VisitExpression(wait.PayloadExpr, checkCalls, depth);
                    break;

                case IfStatement ifStmt:
                    VisitExpression(ifStmt.Condition, checkCalls, depth);
                    VisitStatement(ifStmt.ThenBranch, checkCalls, depth);
                    if (ifStmt.ElseBranch != null)
                        VisitStatement(ifStmt.ElseBranch, checkCalls, depth);
                    break;

                case WhileStatement whileStmt:
                    VisitExpression(whileStmt.Condition, checkCalls, depth);
                    VisitStatement(whileStmt.Body, checkCalls, depth);
                    break;

                case ForStatement forStmt:
                    if (forStmt.Initializer != null)
                        VisitStatement(forStmt.Initializer, checkCalls, depth);
                    if (forStmt.Condition != null)
                        VisitExpression(forStmt.Condition, checkCalls, depth);
                    if (forStmt.Increment != null)
                        VisitExpression(forStmt.Increment, checkCalls, depth);
                    VisitStatement(forStmt.Body, checkCalls, depth);
                    break;

                case ForInStatement forIn:
                    VisitExpression(forIn.Collection, checkCalls, depth);
                    VisitStatement(forIn.Body, checkCalls, depth);
                    break;

                case TryStatement tryStmt:
                    VisitStatement(tryStmt.TryBlock, checkCalls, depth);
                    foreach (var clause in tryStmt.CatchClauses)
                        VisitStatement(clause.Body, checkCalls, depth);
                    if (tryStmt.FinallyBlock != null)
                        VisitStatement(tryStmt.FinallyBlock, checkCalls, depth);
                    break;

                case ExpressionStatement exprStmt:
                    VisitExpression(exprStmt.Expression, checkCalls, depth);
                    break;

                case ReturnStatement ret when ret.Value != null:
                    VisitExpression(ret.Value, checkCalls, depth);
                    break;

                case VarDeclStatement varDecl when varDecl.Initializer != null:
                    VisitExpression(varDecl.Initializer, checkCalls, depth);
                    break;

                case AssignmentStatement assign:
                    VisitExpression(assign.Target, checkCalls, depth);
                    VisitExpression(assign.Value, checkCalls, depth);
                    break;

                case ThrowStatement thr:
                    VisitExpression(thr.Exception, checkCalls, depth);
                    break;

                case PrintStatement print:
                    VisitExpression(print.Expression, checkCalls, depth);
                    break;

                case DeferStatement defer:
                    VisitStatement(defer.Body, checkCalls, depth);
                    break;

                case UsingResourceStatement usingRes:
                    VisitExpression(usingRes.Initializer, checkCalls, depth);
                    VisitStatement(usingRes.Body, checkCalls, depth);
                    break;

                case DestructuringVarDecl destVar:
                    VisitExpression(destVar.Initializer, checkCalls, depth);
                    break;

                case DestructuringAssignment destAssign:
                    VisitExpression(destAssign.Value, checkCalls, depth);
                    break;

                case SendStatement send:
                    VisitExpression(send.Target, checkCalls, depth);
                    foreach (var arg in send.Arguments)
                        VisitExpression(arg, checkCalls, depth);
                    if (send.TimeoutMilliseconds != null)
                        VisitExpression(send.TimeoutMilliseconds, checkCalls, depth);
                    if (send.Callback != null)
                        VisitStatement(send.Callback.Body, checkCalls, depth);
                    if (send.TimeoutErrorHandler != null)
                        VisitStatement(send.TimeoutErrorHandler.Body, checkCalls, depth);
                    break;

                case FunctionDeclaration:
                    // Nested function bodies are analyzed only when called (same-file map is top-level).
                    break;
            }
        }

        public void VisitExpression(Expression expression, bool checkCalls, int depth)
        {
            switch (expression)
            {
                case FunctionCallExpression call:
                    AnalyzeCallExpression(call, checkCalls, depth);
                    VisitExpression(call.Callee, checkCalls, depth);
                    foreach (var arg in call.Arguments)
                        VisitExpression(arg, checkCalls, depth);
                    break;

                case PipeExpression pipe:
                    VisitExpression(pipe.Left, checkCalls, depth);
                    AnalyzePipeRight(pipe.Right, checkCalls, depth);
                    VisitExpression(pipe.Right, checkCalls, depth);
                    break;

                case MemberAccessExpression member:
                    VisitExpression(member.Object, checkCalls, depth);
                    break;

                case BinaryExpression binary:
                    VisitExpression(binary.Left, checkCalls, depth);
                    VisitExpression(binary.Right, checkCalls, depth);
                    break;

                case UnaryExpression unary:
                    VisitExpression(unary.Right, checkCalls, depth);
                    break;

                case PostfixExpression postfix:
                    VisitExpression(postfix.Left, checkCalls, depth);
                    break;

                case TernaryExpression ternary:
                    VisitExpression(ternary.Condition, checkCalls, depth);
                    VisitExpression(ternary.ThenBranch, checkCalls, depth);
                    VisitExpression(ternary.ElseBranch, checkCalls, depth);
                    break;

                case AwaitExpression awaitExpr:
                    VisitExpression(awaitExpr.Expression, checkCalls, depth);
                    break;

                case ArrayAccessExpression arrayAccess:
                    VisitExpression(arrayAccess.Array, checkCalls, depth);
                    VisitExpression(arrayAccess.Index, checkCalls, depth);
                    break;

                case ArrayLiteralExpression arrayLit:
                    foreach (var element in arrayLit.Elements)
                        VisitExpression(element, checkCalls, depth);
                    break;

                case ObjectLiteralExpression objectLit:
                    foreach (var (key, value) in objectLit.Properties)
                    {
                        VisitExpression(key, checkCalls, depth);
                        VisitExpression(value, checkCalls, depth);
                    }
                    break;

                case DictionaryLiteralExpression dictLit:
                    foreach (var (key, value) in dictLit.Entries)
                    {
                        VisitExpression(key, checkCalls, depth);
                        VisitExpression(value, checkCalls, depth);
                    }
                    break;

                case LambdaExpression lambda:
                    if (lambda.ExpressionBody != null)
                        VisitExpression(lambda.ExpressionBody, checkCalls, depth);
                    if (lambda.BlockBody != null)
                        VisitStatement(lambda.BlockBody, checkCalls, depth);
                    break;

                case MatchExpression match:
                    VisitExpression(match.Value, checkCalls, depth);
                    foreach (var arm in match.Cases)
                    {
                        if (arm.Guard != null)
                            VisitExpression(arm.Guard, checkCalls, depth);
                        VisitStatement(arm.Body, checkCalls, depth);
                    }
                    if (match.DefaultCase != null)
                        VisitStatement(match.DefaultCase, checkCalls, depth);
                    break;

                case InterpolatedStringExpression interpolated:
                    foreach (var segment in interpolated.Segments)
                    {
                        if (segment.Expression != null)
                            VisitExpression(segment.Expression, checkCalls, depth);
                    }
                    break;

                case NewExpression newExpr:
                    foreach (var arg in newExpr.Arguments)
                        VisitExpression(arg, checkCalls, depth);
                    break;

                case AsyncExpression asyncExpr:
                    VisitExpression(asyncExpr.Expression, checkCalls, depth);
                    break;

                case SpawnExpression spawn:
                    foreach (var arg in spawn.Arguments)
                        VisitExpression(arg, checkCalls, depth);
                    break;

                case ListComprehensionExpression listComp:
                    VisitExpression(listComp.Element, checkCalls, depth);
                    VisitExpression(listComp.Iterable, checkCalls, depth);
                    if (listComp.Filter != null)
                        VisitExpression(listComp.Filter, checkCalls, depth);
                    break;

                case DictComprehensionExpression dictComp:
                    VisitExpression(dictComp.Key, checkCalls, depth);
                    VisitExpression(dictComp.Value, checkCalls, depth);
                    VisitExpression(dictComp.Iterable, checkCalls, depth);
                    if (dictComp.Filter != null)
                        VisitExpression(dictComp.Filter, checkCalls, depth);
                    break;

                case GraphLiteralExpression graph:
                    if (graph.NodesExpression != null)
                        VisitExpression(graph.NodesExpression, checkCalls, depth);
                    if (graph.EdgesExpression != null)
                        VisitExpression(graph.EdgesExpression, checkCalls, depth);
                    break;

                case NamedArgumentExpression named:
                    VisitExpression(named.Value, checkCalls, depth);
                    break;
            }
        }

        private void AnalyzePipeRight(Expression right, bool checkCalls, int depth)
        {
            if (!checkCalls)
                return;

            switch (right)
            {
                case IdentifierExpression id:
                    AnalyzeCalleeName(id.Name, namespaced: false, id.Line, id.Column, Math.Max(1, id.Name.Length), depth);
                    break;
                case MemberAccessExpression member when TryStdLibMemberName(member, out var memberName):
                    AnalyzeCalleeName(memberName, namespaced: true, member.Line, member.Column, Math.Max(1, memberName.Length), depth);
                    break;
            }
        }

        private void AnalyzeCallExpression(FunctionCallExpression call, bool checkCalls, int depth)
        {
            if (!checkCalls)
                return;

            if (call.Callee is IdentifierExpression id)
            {
                AnalyzeCalleeName(id.Name, namespaced: false, id.Line, id.Column, Math.Max(1, id.Name.Length), depth);
                return;
            }

            if (call.Callee is MemberAccessExpression member &&
                TryStdLibMemberName(member, out var memberName))
            {
                AnalyzeCalleeName(memberName, namespaced: true, member.Line, member.Column, Math.Max(1, memberName.Length), depth);
            }
        }

        private void AnalyzeCalleeName(string name, bool namespaced, int line, int column, int length, int depth)
        {
            var behavior = BuiltInRegistry.GetWorkflowBehavior(name);
            if (behavior == WorkflowBuiltInBehavior.NonDeterministic)
            {
                ReportDenyList(
                    "WF1001",
                    $"WF1001: Non-deterministic built-in '{name}' in deterministic workflow section{ViaHelper(depth)}. Move it inside a step.",
                    line,
                    column,
                    length);
                return;
            }

            if (behavior == WorkflowBuiltInBehavior.SideEffecting)
            {
                ReportDenyList(
                    "WF1002",
                    $"WF1002: Side-effecting operation '{name}' outside step boundary{ViaHelper(depth)}. Move it inside a step.",
                    line,
                    column,
                    length);
                return;
            }

            if (namespaced)
                return;

            if (_functions.TryGetValue(name, out var func))
            {
                WalkUserFunction(func, depth);
                return;
            }

            if (BuiltInRegistry.IsInterpreterBuiltIn(name) || _dataConstructors.Contains(name))
                return;

            if (!_reportedUnknown.Add(name))
                return;

            var kind = _importedNames.Contains(name) ? "imported" : "unknown";
            _diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message = $"WF1005: Call to '{name}' from a deterministic workflow section is {kind} and is not analyzed. Runtime still raises WF1001/WF1002 if a deny-listed built-in runs. Put effects inside a step.",
                Line = line - 1,
                Column = column - 1,
                Length = length,
                Source = "WF1005"
            });
        }

        private void WalkUserFunction(FunctionDeclaration func, int depth)
        {
            if (depth >= MaxCalleeDepth)
                return;
            if (_visiting.Contains(func.Name, StringComparer.Ordinal))
                return;

            _visiting.Add(func.Name);
            VisitStatement(func.Body, checkCalls: true, depth + 1);
            _visiting.RemoveAt(_visiting.Count - 1);
        }

        private void ReportDenyList(string source, string message, int line, int column, int length)
        {
            if (!_reportedErrors.Add((source, line, column)))
                return;

            _diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = message,
                Line = line - 1,
                Column = column - 1,
                Length = length,
                Source = source
            });
        }

        private string ViaHelper(int depth)
        {
            if (depth <= 0 || _visiting.Count == 0)
                return "";
            return $" (via helper '{_visiting[^1]}')";
        }
    }

    private static bool TryStdLibMemberName(MemberAccessExpression member, out string memberName)
    {
        memberName = member.Member;
        if (member.Object is not IdentifierExpression ns
            || !StdLibNamespaces.IsStdLibModuleMethod(ns.Name, member.Member))
            return false;

        if (ns.Name == StdLibNamespaces.CapModule)
            memberName = CapStdLib.ResolveWorkflowBuiltInName(member.Member);
        return true;
    }
}
