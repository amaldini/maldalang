// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void TranspileStatement(Statement statement)
    {
        if (_emitLineDirectives)
        {
            EmitLineDirective(statement.Line, statement.SourceFile ?? _sourceFilePath);
        }
        WriteIndent();
        
        switch (statement)
        {
            case VarDeclStatement varDecl:
                string? varDeclProfile = null;
                if (ProfilingEnabled)
                {
                    varDeclProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileVarDecl(varDecl);
                if (varDeclProfile != null)
                {
                    EmitStatementProfileExit(varDeclProfile);
                }
                break;
            case SendStatement sendStmt:
                string? sendProfile = null;
                if (ProfilingEnabled)
                {
                    sendProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileSend(sendStmt);
                if (sendProfile != null)
                {
                    EmitStatementProfileExit(sendProfile);
                }
                break;
            case AssignmentStatement assignment:
                string? assignmentProfile = null;
                if (ProfilingEnabled)
                {
                    assignmentProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileAssignment(assignment);
                if (assignmentProfile != null)
                {
                    EmitStatementProfileExit(assignmentProfile);
                }
                break;
            case IfStatement ifStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileIf(ifStmt));
                break;
            case WhileStatement whileStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileWhile(whileStmt));
                break;
            case ForStatement forStmt:
                TranspileProfiledStructuredStatement(statement, () => WithOwnContinue(() => TranspileFor(forStmt)));
                break;
            case ForInStatement forInStmt:
                TranspileProfiledStructuredStatement(statement, () => WithOwnContinue(() => TranspileForIn(forInStmt)));
                break;
            case FunctionDeclaration funcDecl:
                TranspileLocalFunction(funcDecl);
                break;
            case ReturnStatement returnStmt:
                if (!ProfilingEnabled)
                {
                    TranspileReturn(returnStmt);
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var returnProfile = EmitStatementProfileStart(statement);
                WriteIndent();
                // object? so `= null` is valid (var would be CS0815)
                _output.Append("object? __maldaReturnValue = ");
                if (returnStmt.Value != null)
                {
                    TranspileExpression(returnStmt.Value);
                }
                else
                {
                    _output.Append("null");
                }
                _output.AppendLine(";");
                EmitStatementProfileExit(returnProfile);
                WriteIndent();
                _output.AppendLine("return __maldaReturnValue;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case PrintStatement printStmt:
                string? printProfile = null;
                if (ProfilingEnabled)
                {
                    printProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspilePrint(printStmt);
                if (printProfile != null)
                {
                    EmitStatementProfileExit(printProfile);
                }
                break;
            case ExpressionStatement exprStmt:
                // A super(...) call can only be a constructor initializer, so it must be the first
                // statement of the constructor body (see TranspileClassMember). Anything else has
                // no valid C# representation and would otherwise emit the invalid `base(...)`.
                if (exprStmt.Expression is FunctionCallExpression { Callee: SuperExpression })
                {
                    throw new NotSupportedException(
                        "'super(...)' must be the first statement of a constructor when transpiling to C#. "
                        + "Move it to the start of the constructor body (the interpreter allows it later, but C# constructor initializers always run first).");
                }
                string? expressionProfile = null;
                if (ProfilingEnabled)
                {
                    expressionProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                if (exprStmt.Expression is UnaryExpression { Operator: TokenType.Increment or TokenType.Decrement } prefixStep)
                {
                    // As a statement `++x` is `x++`; the prefix form is emitted parenthesized,
                    // which C# rejects as a statement (CS0201).
                    TranspilePostfix(new PostfixExpression(prefixStep.Right, prefixStep.Operator, prefixStep.Line, prefixStep.Column));
                }
                else
                {
                    TranspileExpression(exprStmt.Expression);
                }
                _output.AppendLine(";");
                if (expressionProfile != null)
                {
                    EmitStatementProfileExit(expressionProfile);
                }
                break;
            case BlockStatement block:
                TranspileProfiledStructuredStatement(statement, () => TranspileBlock(block));
                break;
            case BreakStatement:
                if (!ProfilingEnabled)
                {
                    _output.AppendLine("break;");
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var breakProfile = EmitStatementProfileStart(statement);
                EmitStatementProfileExit(breakProfile);
                WriteIndent();
                _output.AppendLine("break;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case ContinueStatement:
                if (_desugaredForContinueLabels.TryPeek(out var continueLabel) && continueLabel != null)
                {
                    WriteIndent();
                    _output.Append("goto ");
                    _output.AppendLine($"{continueLabel};");
                    break;
                }

                if (!ProfilingEnabled)
                {
                    _output.AppendLine("continue;");
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var continueProfile = EmitStatementProfileStart(statement);
                EmitStatementProfileExit(continueProfile);
                WriteIndent();
                _output.AppendLine("continue;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case TryStatement tryStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileTry(tryStmt));
                break;
            case ThrowStatement throwStmt:
                if (!ProfilingEnabled)
                {
                    TranspileThrow(throwStmt);
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var throwProfile = EmitStatementProfileStart(statement);
                WriteIndent();
                _output.Append("var __maldaThrownValue = ");
                TranspileExpression(throwStmt.Exception);
                _output.AppendLine(";");
                EmitStatementProfileExit(throwProfile);
                WriteIndent();
                _output.AppendLine("throw new Exception(RuntimeHelpers.CoerceToString(__maldaThrownValue));");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case DestructuringVarDecl destVarDecl:
                string? destructuringVarProfile = null;
                if (ProfilingEnabled)
                {
                    destructuringVarProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileDestructuringVarDecl(destVarDecl);
                if (destructuringVarProfile != null)
                {
                    EmitStatementProfileExit(destructuringVarProfile);
                }
                break;
            case DestructuringAssignment destAssign:
                string? destructuringAssignProfile = null;
                if (ProfilingEnabled)
                {
                    destructuringAssignProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileDestructuringAssignment(destAssign);
                if (destructuringAssignProfile != null)
                {
                    EmitStatementProfileExit(destructuringAssignProfile);
                }
                break;
            case ClassDeclaration classDecl:
                // Classes are handled separately
                break;
            case PropertyDeclaration:
                // Properties are transpiled into dedicated property methods.
                break;
            case SuiteDeclaration:
            case PolicyDeclaration:
            case ContextDeclaration:
                break;
            case WithinStatement withinStmt:
                TranspileWithin(withinStmt);
                break;
            case WorkflowDeclaration:
                // Workflow transpilation deferred to Sprint 5
                break;
            case ImportStatement:
            case UsingStatement:
                // Resolved at runtime or inlined via ExpandFileImportsForTranspile
                break;
            case UsingResourceStatement usingResource:
                TranspileProfiledStructuredStatement(statement, () => TranspileUsingResource(usingResource));
                break;
            case DeferStatement deferStmt:
                TranspileDefer(deferStmt);
                break;
            case WorkflowStepStatement workflowStep when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowStep(workflowStep));
                break;
            case WorkflowApprovalStatement workflowApproval when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowApproval(workflowApproval));
                break;
            case WorkflowAwaitSignalStatement workflowSignal when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowAwaitSignal(workflowSignal));
                break;
        }
    }

    private void TranspileVarDecl(VarDeclStatement varDecl)
    {
        var declaredType = ResolveVarDeclType(varDecl);
        RegisterTypedVariable(varDecl.Name, declaredType);
        if (varDecl.IsConst)
            RegisterConstBinding(varDecl.Name);
        _output.Append(GetClrTypeName(declaredType));
        _output.Append(" ");
        _output.Append(EscapeIdentifier(varDecl.Name));
        _output.Append(" = ");
        EmitCoercedValue(varDecl.Initializer, declaredType);
        _output.Append(";");
        AppendComment(nameof(TranspileVarDecl));
        _output.AppendLine();
    }

    private void TranspileAssignment(AssignmentStatement assignment)
    {
        // Handle array assignment specially
        if (assignment.Target is ArrayAccessExpression arrayAccess)
        {
            if (ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray)
            {
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("{ var __typedArr = RuntimeHelpers.CoerceToDoubleList(");
                    TranspileExpression(arrayAccess.Array);
                    _output.Append("); var __typedIdx = (int)RuntimeHelpers.CoerceToInt(");
                    TranspileExpression(arrayAccess.Index);
                    _output.Append("); if (__typedIdx < 0 || __typedIdx >= __typedArr.Count) throw new InvalidOperationException(\"Array index out of bounds.\"); __typedArr[__typedIdx] = (double)RuntimeHelpers.CoerceToFloat(");
                    TranspileExpression(assignment.Value);
                    _output.Append("); }");
                }
                else
                {
                    var opString = GetOperatorString(assignment.Operator);
                    _output.Append("{ var __typedArr = RuntimeHelpers.CoerceToDoubleList(");
                    TranspileExpression(arrayAccess.Array);
                    _output.Append("); var __typedIdx = (int)RuntimeHelpers.CoerceToInt(");
                    TranspileExpression(arrayAccess.Index);
                    _output.Append("); if (__typedIdx < 0 || __typedIdx >= __typedArr.Count) throw new InvalidOperationException(\"Array index out of bounds.\"); __typedArr[__typedIdx] = __typedArr[__typedIdx] ");
                    _output.Append(opString);
                    _output.Append(" (double)RuntimeHelpers.CoerceToFloat(");
                    TranspileExpression(assignment.Value);
                    _output.Append("); }");
                }
                AppendComment(nameof(TranspileAssignment) + " (typed indexed)");
                _output.AppendLine();
                return;
            }
            if (assignment.Operator == TokenType.Assign)
            {
                _output.Append("RuntimeHelpers.SetIndexed(");
                TranspileExpression(arrayAccess.Array);
                _output.Append(", ");
                TranspileExpression(arrayAccess.Index);
                _output.Append(", ");
                TranspileExpression(assignment.Value);
                _output.Append(");");
            }
            else
            {
                // Compound assignment for indexed targets: expand to get, operate, set
                var opString = GetOperatorString(assignment.Operator);
                _output.Append("{ var __indexedTarget = ");
                TranspileExpression(arrayAccess.Array);
                _output.Append("; var __indexedKey = ");
                TranspileExpression(arrayAccess.Index);
                _output.Append("; var __current = RuntimeHelpers.GetIndexed(__indexedTarget, __indexedKey); __current = (");
                _output.Append("__current ");
                _output.Append(opString);
                _output.Append(" ");
                TranspileExpression(assignment.Value);
                _output.Append("); RuntimeHelpers.SetIndexed(__indexedTarget, __indexedKey, __current); }");
            }
            AppendComment(nameof(TranspileAssignment) + " (indexed)");
            _output.AppendLine();
        }
        else if (assignment.Target is MemberAccessExpression memberAccess)
        {
            var setHelper = IsClassReference(memberAccess.Object)
                ? "RuntimeHelpers.SetStaticMember"
                : "RuntimeHelpers.SetObjectMember";
            var getHelper = IsClassReference(memberAccess.Object)
                ? "RuntimeHelpers.GetStaticMember"
                : "RuntimeHelpers.GetObjectMember";
            if (assignment.Operator == TokenType.Assign && IsDirectThisField(memberAccess.Object, memberAccess.Member))
            {
                _output.Append("this.");
                _output.Append(memberAccess.Member);
                _output.Append(" = ");
                TranspileExpression(assignment.Value);
                _output.Append(";");
            }
            else if (assignment.Operator == TokenType.Assign)
            {
                // Handle ObjectInstance property assignment (e.g., JsonObject properties)
                _output.Append($"{setHelper}(");
                TranspileReceiver(memberAccess.Object);
                _output.Append(", \"");
                _output.Append(memberAccess.Member);
                _output.Append("\", ");
                TranspileExpression(assignment.Value);
                _output.Append(");");
            }
            else
            {
                // Compound assignment for members: expand to get, operate, set.
                // Member values are boxed as object, so arithmetic must go through the
                // RuntimeHelpers.Operator* functions rather than a raw C# `+=`.
                var boxedHelper = GetBoxedBinaryOperatorHelper(assignment.Operator);
                var opString = GetOperatorString(assignment.Operator);
                _output.Append($"{setHelper}(");
                TranspileReceiver(memberAccess.Object);
                _output.Append(", \"");
                _output.Append(memberAccess.Member);
                _output.Append("\", ");
                if (boxedHelper != null)
                {
                    _output.Append($"RuntimeHelpers.{boxedHelper}(");
                    _output.Append($"{getHelper}(");
                    TranspileReceiver(memberAccess.Object);
                    _output.Append(", \"");
                    _output.Append(memberAccess.Member);
                    _output.Append("\"), ");
                    TranspileExpression(assignment.Value);
                    _output.Append(")");
                }
                else
                {
                    _output.Append($"{getHelper}(");
                    TranspileReceiver(memberAccess.Object);
                    _output.Append(", \"");
                    _output.Append(memberAccess.Member);
                    _output.Append("\") ");
                    _output.Append(opString);
                    _output.Append(" ");
                    TranspileExpression(assignment.Value);
                }
                _output.Append(");");
            }
            AppendComment(nameof(TranspileAssignment) + " (member)");
            _output.AppendLine();
        }
        else
        {
            // Simple variable assignment
            if (assignment.Target is IdentifierExpression identifierTarget)
            {
                EmitConstAssignGuard(identifierTarget.Name);
                var targetType = ResolveVariableTypeOrDefault(identifierTarget.Name);
                _output.Append(EscapeIdentifier(identifierTarget.Name));
                _output.Append(" ");
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("= ");
                    EmitCoercedValue(assignment.Value, targetType);
                }
                else
                {
                    // x op= v becomes x = op(x, v): `object += int` does not compile, ints need
                    // checked arithmetic, and float division must raise on zero.
                    var boxedHelper = GetBoxedBinaryOperatorHelper(assignment.Operator);
                    var compoundHelper = targetType switch
                    {
                        TranspiledClrType.Int => assignment.Operator switch
                        {
                            TokenType.PlusAssign => "CheckedIntAdd",
                            TokenType.MinusAssign => "CheckedIntSubtract",
                            TokenType.MultiplyAssign => "CheckedIntMultiply",
                            _ => null
                        },
                        TranspiledClrType.Double => assignment.Operator == TokenType.DivideAssign ? "DivideDouble" : null,
                        TranspiledClrType.Object => boxedHelper,
                        _ => null
                    };
                    if (compoundHelper != null)
                    {
                        _output.Append("= RuntimeHelpers.").Append(compoundHelper).Append("(");
                        _output.Append(EscapeIdentifier(identifierTarget.Name));
                        _output.Append(", ");
                        EmitCoercedValue(assignment.Value, targetType == TranspiledClrType.Object ? TranspiledClrType.Object : targetType);
                        _output.Append(")");
                    }
                    else
                    {
                        _output.Append(GetOperatorString(assignment.Operator));
                        _output.Append(" ");
                        EmitCoercedValue(assignment.Value, targetType == TranspiledClrType.Double ? TranspiledClrType.Double : targetType);
                    }
                }
            }
            else
            {
                TranspileExpression(assignment.Target);
                _output.Append(" ");
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("=");
                }
                else
                {
                    var opString = GetOperatorString(assignment.Operator);
                    _output.Append(opString);
                }
                _output.Append(" ");
                TranspileExpression(assignment.Value);
            }
            _output.Append(";");
            AppendComment(nameof(TranspileAssignment));
            _output.AppendLine();
        }
    }

    private void TranspileIf(IfStatement ifStmt)
    {
        _output.Append("if (");
        EmitCondition(ifStmt.Condition);
        _output.Append(")");
        AppendComment(nameof(TranspileIf));
        _output.AppendLine();
        TranspileStatement(ifStmt.ThenBranch);
        
        if (ifStmt.ElseBranch != null)
        {
            WriteIndent();
            _output.Append("else");
            AppendComment(nameof(TranspileIf) + " (else)");
            _output.AppendLine();
            TranspileStatement(ifStmt.ElseBranch);
        }
    }

    private void TranspileWhile(WhileStatement whileStmt)
    {
        _output.Append("while (");
        EmitCondition(whileStmt.Condition);
        _output.Append(")");
        AppendComment(nameof(TranspileWhile));
        _output.AppendLine();

        // Parser desugars `for` into `while` with body Block { userBody, increment }.
        // `continue` in userBody must still run increment (see Interpreter.ExecuteWhileAsync).
        if (whileStmt.DesugaredForIncrement != null && whileStmt.Body is BlockStatement desugaredForBody)
        {
            var incrementLabel = $"__forIncrement{_desugaredForContinueLabels.Count}";
            _desugaredForContinueLabels.Push(incrementLabel);
            try
            {
                WriteIndent();
                _output.AppendLine("{");
                _indentLevel++;
                TranspileStatement(desugaredForBody.Statements[0]);
                WriteIndent();
                _output.AppendLine($"{incrementLabel}: ;");
                TranspileStatement(desugaredForBody.Statements[1]);
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
            }
            finally
            {
                _desugaredForContinueLabels.Pop();
            }

            return;
        }

        WithOwnContinue(() => TranspileStatement(whileStmt.Body));
    }

    /// <summary>
    /// Emits a loop whose <c>continue</c> is a plain C# <c>continue</c>, so it does not jump
    /// to the increment label of an enclosing desugared <c>for</c>.
    /// </summary>
    private void WithOwnContinue(Action emitLoop)
    {
        _desugaredForContinueLabels.Push(null);
        try
        {
            emitLoop();
        }
        finally
        {
            _desugaredForContinueLabels.Pop();
        }
    }

    private void TranspileFor(ForStatement forStmt)
    {
        _output.Append("for (");
        
        if (forStmt.Initializer != null)
        {
            if (forStmt.Initializer is VarDeclStatement varDecl)
            {
                _output.Append("object ");
                _output.Append(EscapeIdentifier(varDecl.Name));
                _output.Append(" = ");
                TranspileExpression(varDecl.Initializer);
            }
            else if (forStmt.Initializer is ExpressionStatement exprStmt)
            {
                TranspileExpression(exprStmt.Expression);
            }
            else
            {
                // For other statement types, we need to handle them differently
                // This is a limitation - for loop initializer should be var decl or expression
                TranspileStatement(forStmt.Initializer);
                // Remove trailing semicolon and newline
                var currentLength = _output.Length;
                while (currentLength > 0 && (_output[currentLength - 1] == ';' || _output[currentLength - 1] == '\n' || _output[currentLength - 1] == '\r'))
                {
                    currentLength--;
                }
                _output.Length = currentLength;
            }
        }
        
        _output.Append("; ");
        
        if (forStmt.Condition != null)
        {
            EmitCondition(forStmt.Condition);
        }
        
        _output.Append("; ");
        
        if (forStmt.Increment != null)
        {
            TranspileExpression(forStmt.Increment);
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspileFor));
        _output.AppendLine();
        TranspileStatement(forStmt.Body);
    }

    private void TranspileForIn(ForInStatement forInStmt)
    {
        _output.Append("foreach (object ");
        _output.Append(EscapeIdentifier(forInStmt.VariableName));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(forInStmt.Collection);
        _output.Append("))");
        AppendComment(nameof(TranspileForIn));
        _output.AppendLine();
        TranspileStatement(forInStmt.Body);
    }

    private void TranspileReturn(ReturnStatement returnStmt)
    {
        var returnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        _output.Append("return ");
        if (returnStmt.Value != null && returnType is TranspiledClrType.Int or TranspiledClrType.Double)
        {
            EmitCoercedValue(returnStmt.Value, returnType);
            _output.Append(";");
            AppendComment(nameof(TranspileReturn));
            _output.AppendLine();
            return;
        }
        _output.Append(GetCoercionExpressionPrefix(returnType));
        if (returnStmt.Value != null)
        {
            TranspileExpression(returnStmt.Value);
        }
        else
        {
            _output.Append("null");
        }
        _output.Append(GetCoercionExpressionSuffix(returnType));
        _output.Append(";");
        AppendComment(nameof(TranspileReturn));
        _output.AppendLine();
    }

    private void TranspilePrint(PrintStatement printStmt)
    {
        _output.Append("Console.WriteLine(RuntimeHelpers.CoerceToString(");
        TranspileExpression(printStmt.Expression);
        _output.Append("));");
        AppendComment(nameof(TranspilePrint));
        _output.AppendLine();
    }

    /// <summary>
    /// Last-expression-wins returns must not wrap void C# calls (print / MarkupLine, etc.).
    /// </summary>
    private void EmitLastExpressionAsReturn(Expression expr, TranspiledClrType blockReturnType)
    {
        if (ExpressionTranspilesToVoid(expr))
        {
            WriteIndent();
            TranspileExpression(expr);
            _output.AppendLine(";");
            WriteIndent();
            if (blockReturnType == TranspiledClrType.Double)
                _output.AppendLine("return 0d;");
            else
                _output.AppendLine("return null;");
            return;
        }

        WriteIndent();
        _output.Append("return ");
        EmitCoercedValue(expr, blockReturnType);
        _output.AppendLine(";");
    }

    private static bool ExpressionTranspilesToVoid(Expression expr)
    {
        if (expr is not FunctionCallExpression call)
            return false;

        if (call.Callee is IdentifierExpression id)
            return id.Name is "print";

        if (call.Callee is MemberAccessExpression member &&
            member.Object is IdentifierExpression obj)
        {
            if (obj.Name == "AnsiConsole" && member.Member is "markup" or "markupLine")
                return true;
            if (obj.Name == StdLibNamespaces.IoModule && member.Member == "print")
                return true;
        }

        return false;
    }

    private void TranspileFunctionBlock(
        BlockStatement block,
        string functionName,
        int line,
        bool appendImplicitNullReturn = false,
        ResourceBudget? budget = null,
        bool skipLeadingStatement = false)
    {
        PushTypedScope();
        PushConstScope();
        PushLocalFunctionScope();
        var blockReturnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileFunctionBlock) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        string? functionProfile = null;
        var emitBudget = budget != null && budget.HasAnyBound;

        if (emitBudget)
        {
            WriteIndent();
            EmitResourceBudgetPush(budget!, functionName);
            WriteIndent();
            _output.AppendLine("try");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
        }

        if (ProfilingEnabled)
        {
            functionProfile = EmitFunctionProfileStart(functionName, line, block.SourceFile);
            WriteIndent();
            _output.AppendLine("try");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
        }

        var needsDeferFrame = _deferScan.ContainsDefer(block);
        EmitDeferFramePrologue(needsDeferFrame);

        var lastIsExpression = block.Statements.Count > 0 && block.Statements[^1] is ExpressionStatement;
        var useLastExprWins = appendImplicitNullReturn && lastIsExpression;

        for (int i = 0; i < block.Statements.Count; i++)
        {
            if (skipLeadingStatement && i == 0)
                continue;

            var stmt = block.Statements[i];
            var isLast = i == block.Statements.Count - 1;
            if (useLastExprWins && isLast && stmt is ExpressionStatement exprStmt)
            {
                EmitLastExpressionAsReturn(exprStmt.Expression, blockReturnType);
            }
            else
            {
                TranspileStatement(stmt);
            }
        }

        if (appendImplicitNullReturn && !useLastExprWins && !BlockDefinitelyReturns(block))
        {
            WriteIndent();
            if (blockReturnType == TranspiledClrType.Double)
                _output.Append("return 0d;");
            else
                _output.Append("return null;");
            AppendComment(nameof(TranspileFunctionBlock) + " (implicit return)");
            _output.AppendLine();
        }

        EmitDeferFrameEpilogue(needsDeferFrame);

        if (ProfilingEnabled)
        {
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("finally");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            EmitFunctionProfileExit(functionProfile!);
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }

        if (emitBudget)
        {
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("finally");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("MaldaLang.Interpreter.ResourceBoundsContext.Pop();");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }
        
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileFunctionBlock) + " (close)");
        _output.AppendLine();
        PopLocalFunctionScope();
        PopConstScope();
        PopTypedScope();
    }

    private static bool BlockDefinitelyReturns(BlockStatement block)
    {
        foreach (var statement in block.Statements)
        {
            if (StatementDefinitelyReturns(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementDefinitelyReturns(Statement statement)
    {
        return statement switch
        {
            ReturnStatement => true,
            BlockStatement block => BlockDefinitelyReturns(block),
            IfStatement ifStatement => ifStatement.ElseBranch != null &&
                                       StatementDefinitelyReturns(ifStatement.ThenBranch) &&
                                       StatementDefinitelyReturns(ifStatement.ElseBranch),
            TryStatement tryStatement => TryStatementDefinitelyReturns(tryStatement),
            _ => false
        };
    }

    private static bool TryStatementDefinitelyReturns(TryStatement tryStatement)
    {
        if (tryStatement.FinallyBlock != null && BlockDefinitelyReturns(tryStatement.FinallyBlock))
        {
            return true;
        }

        if (!BlockDefinitelyReturns(tryStatement.TryBlock))
        {
            return false;
        }

        if (tryStatement.CatchClauses.Count == 0)
        {
            return false;
        }

        foreach (var catchClause in tryStatement.CatchClauses)
        {
            if (!BlockDefinitelyReturns(catchClause.Body))
            {
                return false;
            }
        }

        return true;
    }

    // Blocks that can never run a `defer` skip the frame: PushDeferFrame touches an AsyncLocal
    // stack and the finally awaits, which dominated tight loops and recursive calls.
    private void EmitDeferFramePrologue(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        WriteIndent();
        _output.AppendLine("RuntimeHelpers.PushDeferFrame();");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
    }

    private void EmitDeferFrameEpilogue(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        if (_canAwait)
            _output.AppendLine("await RuntimeHelpers.RunAndPopDeferFrameAsync();");
        else
            _output.AppendLine("RuntimeHelpers.RunAndPopDeferFrameAsync().GetAwaiter().GetResult();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileBlock(BlockStatement block, bool appendImplicitNullReturn = false)
    {
        PushTypedScope();
        PushConstScope();
        PushLocalFunctionScope();
        var blockReturnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileBlock) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        var needsDeferFrame = _deferScan.ContainsDefer(block);
        EmitDeferFramePrologue(needsDeferFrame);
        
        var lastIsExpression = block.Statements.Count > 0 && block.Statements[^1] is ExpressionStatement;
        var useLastExprWins = appendImplicitNullReturn && lastIsExpression;
        
        for (int i = 0; i < block.Statements.Count; i++)
        {
            var stmt = block.Statements[i];
            var isLast = (i == block.Statements.Count - 1);
            if (useLastExprWins && isLast && stmt is ExpressionStatement exprStmt)
            {
                EmitLastExpressionAsReturn(exprStmt.Expression, blockReturnType);
            }
            else
            {
                TranspileStatement(stmt);
            }
        }

        if (appendImplicitNullReturn && !useLastExprWins && (block.Statements.Count == 0 || block.Statements[^1] is not ReturnStatement))
        {
            WriteIndent();
            if (blockReturnType == TranspiledClrType.Double)
                _output.Append("return 0d;");
            else
                _output.Append("return null;");
            AppendComment(nameof(TranspileBlock) + " (implicit return)");
            _output.AppendLine();
        }
        
        EmitDeferFrameEpilogue(needsDeferFrame);
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileBlock) + " (close)");
        _output.AppendLine();
        PopLocalFunctionScope();
        PopConstScope();
        PopTypedScope();
    }

    private void TranspileDefer(DeferStatement defer)
    {
        WriteIndent();
        _output.Append("RuntimeHelpers.RegisterDefer(async () => {");
        _output.AppendLine();
        _indentLevel++;
        foreach (var stmt in defer.Body.Statements)
        {
            TranspileStatement(stmt);
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("});");
    }

    private void TranspileUsingResource(UsingResourceStatement stmt)
    {
        WriteIndent();
        _output.Append("var ");
        _output.Append(EscapeIdentifier(stmt.VariableName));
        _output.Append(" = ");
        TranspileExpression(stmt.Initializer);
        _output.AppendLine(";");
        TranspileBlock(stmt.Body);
        WriteIndent();
        if (_canAwait)
            _output.Append("await RuntimeHelpers.DisposeResourceAsync(");
        else
            _output.Append("RuntimeHelpers.DisposeResourceAsync(");
        _output.Append(EscapeIdentifier(stmt.VariableName));
        if (_canAwait)
            _output.AppendLine(");");
        else
            _output.AppendLine(").GetAwaiter().GetResult();");
    }

    private void TranspileTry(TryStatement tryStmt)
    {
        _output.Append("try");
        AppendComment(nameof(TranspileTry));
        _output.AppendLine();
        TranspileBlock(tryStmt.TryBlock);
        
        // Transpile catch clauses (use C# when for filters so later catch clauses remain reachable)
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            WriteIndent();
            _output.Append("catch (Exception ");
            
            var tempExceptionVar = "__spl_exception";
            _output.Append(tempExceptionVar);

            if (catchClause.Filter != null)
            {
                var bindParam = "__maldaCatchE";
                _output.Append(") when (RuntimeHelpers.MaldaCatchWhen(");
                _output.Append(tempExceptionVar);
                _output.Append(", ");
                _output.Append(bindParam);
                _output.Append(" => RuntimeHelpers.CoerceToBool(");
                if (catchClause.ExceptionVariable != null)
                {
                    _catchFilterRenameFrom = catchClause.ExceptionVariable;
                    _catchFilterRenameTo = bindParam;
                }
                TranspileExpression(catchClause.Filter);
                _catchFilterRenameFrom = null;
                _catchFilterRenameTo = null;
                _output.Append("))");
            }

            _output.Append(")");
            
            AppendComment(nameof(TranspileTry) + " (catch)");
            _output.AppendLine();
            
            WriteIndent();
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;
            
            if (catchClause.ExceptionVariable != null)
            {
                WriteIndent();
                _output.Append("var ");
                _output.Append(EscapeIdentifier(catchClause.ExceptionVariable));
                _output.Append(" = RuntimeHelpers.UnwrapRuntimeValue(RuntimeHelpers.UnwrapMaldaExceptionValue(");
                _output.Append(tempExceptionVar);
                _output.Append("));");
                _output.AppendLine();
            }
            
            TranspileBlock(catchClause.Body);
            
            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            _output.AppendLine();
        }
        
        // Transpile finally block if present
        if (tryStmt.FinallyBlock != null)
        {
            WriteIndent();
            _output.Append("finally");
            AppendComment(nameof(TranspileTry) + " (finally)");
            _output.AppendLine();
            TranspileBlock(tryStmt.FinallyBlock);
        }
    }

    private void TranspileThrow(ThrowStatement throwStmt)
    {
        _output.Append("throw new MaldaLang.Interpreter.MALDAException(RuntimeHelpers.ToRuntimeValue(");
        TranspileExpression(throwStmt.Exception);
        _output.Append("));");
        AppendComment(nameof(TranspileThrow));
        _output.AppendLine();
    }

    private void TranspileWithin(WithinStatement stmt)
    {
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine($"MaldaLang.Interpreter.WithinBoundsContext.Push({stmt.TimeoutMs});");
        if (stmt.HasBudget)
        {
            WriteIndent();
            _output.Append("MaldaLang.Interpreter.ResourceBoundsContext.Push(new MaldaLang.Interpreter.ResourceBudget(");
            _output.Append(stmt.BudgetTokens.HasValue ? stmt.BudgetTokens.Value.ToString() : "null");
            _output.Append(", ");
            _output.Append(stmt.BudgetTools.HasValue ? stmt.BudgetTools.Value.ToString() : "null");
            _output.Append(", ");
            _output.Append(stmt.BudgetCost.HasValue ? stmt.BudgetCost.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null");
            _output.AppendLine("), \"within\");");
        }

        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        TranspileStatement(stmt.Body);
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.WithinBoundsContext.EnsureWithinBound(\"within\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        if (stmt.HasBudget)
        {
            WriteIndent();
            _output.AppendLine("MaldaLang.Interpreter.ResourceBoundsContext.Pop();");
        }

        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.WithinBoundsContext.Pop();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }
}
