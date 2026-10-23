// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    /// <summary>
    /// Signal-aware statement dispatch (see Interpreter.ControlFlow.cs) with the debugger,
    /// profiler and current-file bookkeeping; <see cref="ExecuteStatementAsync"/> skips it when idle.
    /// </summary>
    private async Task<RuntimeValue?> ExecuteStatementSlowAsync(Statement stmt)
    {
        var previousFile = _currentFile;
        var statementFile = stmt.SourceFile ?? _currentFile;
        _currentFile = statementFile;
        // The profile name costs a GetType().Name and a substring: only build it when profiling.
        var profileToken = MaldaProfiler.IsEnabled
            ? MaldaProfiler.EnterStatement(statementFile, stmt.Line, GetStatementProfileName(stmt))
            : default;
        // Check debugger hook before executing
        try
        {
            if (_debuggerHook != null && DebugStatementClassifier.IsStoppable(stmt))
            {
                if (_callStack.Count > 0)
                {
                    var frame = _callStack[_callStack.Count - 1];
                    frame.Line = stmt.Line;
                    frame.File = _currentFile ?? frame.File;
                    frame.Environment = _environment;
                    frame.ThisObject = _currentObject;
                }

                if (!_debuggerHook.OnStatement(stmt.Line, _currentFile))
                {
                    _debuggerHook.OnPause(stmt.Line, _currentFile);
                    await _debuggerHook.WaitIfPausedAsync(_interpretCancellation);
                }
            }

            return stmt switch
            {
                VarDeclStatement varDecl => await ExecuteVarDeclAsync(varDecl),
                DestructuringVarDecl destVarDecl => await ExecuteDestructuringVarDeclAsync(destVarDecl),
                AssignmentStatement assign => await ExecuteAssignmentAsync(assign),
                DestructuringAssignment destAssign => await ExecuteDestructuringAssignmentAsync(destAssign),
                ExpressionStatement expr => await ExecuteExpressionAsync(expr),
                PrintStatement print => await ExecutePrintAsync(print),
                BlockStatement block => await ExecuteBlockStatementsAsync(block, new Environment(_environment)),
                IfStatement ifStmt => await ExecuteIfAsync(ifStmt),
                WhileStatement whileStmt => await ExecuteWhileAsync(whileStmt),
                ForStatement forStmt => await ExecuteForAsync(forStmt),
                ForInStatement forInStmt => await ExecuteForInAsync(forInStmt),
                ReturnStatement returnStmt => await ExecuteReturnAsync(returnStmt),
                BreakStatement => SignalControl(PendingControl.Break),
                ContinueStatement => SignalControl(PendingControl.Continue),
                TryStatement tryStmt => await ExecuteTryAsync(tryStmt),
                ThrowStatement throwStmt => await ExecuteThrowAsync(throwStmt),
                SendStatement sendStmt => await ExecuteSendAsync(sendStmt),
                UsingStatement usingStmt => await ExecuteUsingAsync(usingStmt),
                UsingResourceStatement usingResource => await ExecuteUsingResourceAsync(usingResource),
                DeferStatement deferStmt => ExecuteDefer(deferStmt),
                ImportStatement importStmt => await ExecuteImportAsync(importStmt),
                JsImportStatement => throw new RuntimeException(JsImportStatement.HostBackendMessage, stmt.Line, _currentFile),
                FunctionDeclaration funcDecl => BindLocalFunction(funcDecl),
                ClassDeclaration classDecl => null, // Already handled
                ActorDeclaration actorDecl => null, // Already handled
                PromptDeclaration => null, // Already handled
                TypeDeclaration => null, // Already handled
                SchemaDeclaration => null, // Already handled
                ApiDeclaration => null, // Already handled
                WorkflowDeclaration => null, // Already handled in declaration pass
                PropertyDeclaration => null, // Already handled in declaration pass
                SuiteDeclaration => null,
                ContextDeclaration => null,
                PolicyDeclaration => null,
                WithinStatement withinStmt => await ExecuteWithinAsync(withinStmt),
                WorkflowStepStatement stepStmt => await ExecuteWorkflowStepAsync(stepStmt),
                WorkflowApprovalStatement approvalStmt => await ExecuteWorkflowApprovalAsync(approvalStmt),
                WorkflowAwaitSignalStatement awaitSignalStmt => await ExecuteWorkflowAwaitSignalAsync(awaitSignalStmt),
                _ => throw new RuntimeException($"Unknown statement type: {stmt.GetType()}")
            };
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
            _currentFile = previousFile;
        }
    }
    
    private async Task<RuntimeValue?> ExecuteExpressionAsync(ExpressionStatement stmt)
    {
        return await EvaluateAsync(stmt.Expression);
    }
    
    private async Task<RuntimeValue?> ExecuteVarDeclAsync(VarDeclStatement stmt)
    {
        var value = await EvaluateAsync(stmt.Initializer);
        _environment.Define(stmt.Name, value, stmt.IsConst);
        return null;
    }

    private void EnsureMutableIdentifier(string name, int line)
    {
        if (_environment.IsConst(name))
            throw new RuntimeException($"Cannot assign to const '{name}'.", line, _currentFile);
    }
    
    private RuntimeValue ApplyCompoundAssignment(AssignmentStatement stmt, RuntimeValue currentValue, RuntimeValue rightValue)
    {
        return stmt.Operator switch
        {
            TokenType.PlusAssign => EvaluatePlus(currentValue, rightValue, stmt.Line),
            TokenType.MinusAssign => EvaluateMinus(currentValue, rightValue, stmt.Line),
            TokenType.MultiplyAssign => EvaluateMultiply(currentValue, rightValue, stmt.Line),
            TokenType.DivideAssign => EvaluateDivide(currentValue, rightValue, stmt.Line),
            _ => throw new RuntimeException($"Unknown compound assignment operator: {stmt.Operator}", stmt.Line, _currentFile)
        };
    }

    private async Task<RuntimeValue?> ExecuteAssignmentAsync(AssignmentStatement stmt)
    {
        RuntimeValue value;
        
        // Handle compound assignment operators
        if (stmt.Operator != TokenType.Assign)
        {
            // Get current value of target
            var currentValue = await GetLvalueAsync(stmt.Target);
            var rightValue = await EvaluateAsync(stmt.Value);
            value = ApplyCompoundAssignment(stmt, currentValue, rightValue);
        }
        else
        {
            // Regular assignment
            value = await EvaluateAsync(stmt.Value);
        }
        
        // Use the helper method to set the value
        await SetLvalueAsync(stmt.Target, value);
        
        return null;
    }
    
    private async Task<RuntimeValue?> ExecutePrintAsync(PrintStatement stmt)
    {
        var value = await EvaluateAsync(stmt.Expression);
        var text = value.ToString();
        if (_outputCallback != null)
            _outputCallback(text);
        else
            Console.WriteLine(text);
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteBlockAsync(BlockStatement stmt)
    {
        return await ExecuteBlockAsync(stmt, new Environment(_environment));
    }
    
    /// <summary>
    /// Pops <paramref name="frame"/> when it is on top. Every frame owner pops in a
    /// <c>finally</c>, so nested frames are already gone by the time the owner unwinds.
    /// </summary>
    private void PopExecutionFrame(ExecutionFrame frame)
    {
        if (_executionStack.Count > 0 && ReferenceEquals(_executionStack.Peek(), frame))
            _executionStack.Pop();
    }

    private async Task<RuntimeValue?> ExecuteBlockStatementsAsync(BlockStatement stmt, Environment environment)
    {
        // Check if we're resuming a block execution
        BlockFrame? existingFrame = null;
        if (_executionStack.Count > 0 && _executionStack.Peek() is BlockFrame frame && frame.Statement == stmt)
        {
            existingFrame = frame;
        }
        
        BlockFrame blockFrame;
        if (existingFrame != null)
        {
            // Resuming - use existing frame
            blockFrame = existingFrame;
            environment = blockFrame.Environment;
        }
        else
        {
            // New block - create frame
            blockFrame = new BlockFrame
            {
                Statement = stmt,
                Environment = environment,
                StatementIndex = 0
            };
            _executionStack.Push(blockFrame);
        }
        
        var previous = _environment;
        PushDeferFrame();
        try
        {
            _environment = environment;
            
            // Execute statements starting from saved index
            for (int i = blockFrame.StatementIndex; i < stmt.Statements.Count; i++)
            {
                blockFrame.StatementIndex = i;
                var blockStmt = stmt.Statements[i];
                if (!TryExecuteSync(blockStmt, out _))
                    await ExecuteStatementAsync(blockStmt);
                if (_pendingControl != PendingControl.None)
                    break;
            }
        }
        finally
        {
            // Pop on every exit (return/break/continue/throw included). A frame left behind
            // would be "resumed" by the next execution of this same block — e.g. the outer
            // call of a recursive function jumping straight to the inner call's `return`.
            PopExecutionFrame(blockFrame);
            await RunAndPopDeferFrameAsync();
            _environment = previous;
        }
        return null;
    }

    private RuntimeValue? ExecuteDefer(DeferStatement defer)
    {
        RegisterDeferAction(defer);
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteWhileAsync(WhileStatement stmt)
    {
        // Check if we're resuming from an existing frame
        WhileLoopFrame? existingFrame = null;
        if (_executionStack.Count > 0 && _executionStack.Peek() is WhileLoopFrame frame && frame.Statement == stmt)
        {
            existingFrame = frame;
        }
        
        WhileLoopFrame whileFrame;
        if (existingFrame != null)
        {
            // Resuming - use existing frame
            whileFrame = existingFrame;
        }
        else
        {
            // New loop - create frame
            whileFrame = new WhileLoopFrame
            {
                Statement = stmt,
                Environment = _environment,
                StatementIndex = 0,
                LoopIteration = 0,
                ConditionEvaluated = false
            };
            
            // Check if body is a block for statement tracking
            if (stmt.Body is BlockStatement bodyBlock)
            {
                whileFrame.BodyBlock = bodyBlock;
            }
            
            _executionStack.Push(whileFrame);
        }
        
        try
        {
            // If resuming (StatementIndex > 0), continue from where we left off without re-evaluating condition
            if (whileFrame.StatementIndex > 0)
            {
                // Resuming: continue from saved StatementIndex
                // Execute from StatementIndex (where we left off)
                if (whileFrame.BodyBlock != null)
                {
                    // Execute block statements from StatementIndex
                    for (int i = whileFrame.StatementIndex; i < whileFrame.BodyBlock.Statements.Count; i++)
                    {
                        whileFrame.StatementIndex = i;
                        await ExecuteStatementAsync(whileFrame.BodyBlock.Statements[i]);
                        if (_pendingControl != PendingControl.None)
                            break;
                    }
                    if (await ConsumeWhileBodySignalAsync(whileFrame))
                        return null;
                    // Iteration completed - reset statement index for next iteration
                    whileFrame.StatementIndex = 0;
                }
                else
                {
                    // Single statement body - already executed, so this shouldn't happen
                    // But if it does, just reset
                    whileFrame.StatementIndex = 0;
                }
            }
            
            // Continue looping (or start new loop if StatementIndex was 0)
            // Keep looping until condition is false
            while ((TryEvaluateSync(stmt.Condition, out var condition) ? condition : await EvaluateCoreAsync(stmt.Condition, false)).IsTruthy())
            {
                // Only increment LoopIteration for new iterations (StatementIndex == 0)
                if (whileFrame.StatementIndex == 0)
                {
                    whileFrame.LoopIteration++;
                }
                whileFrame.ConditionEvaluated = true;
                
                try
                {
                    // Execute from StatementIndex (0 for new iteration)
                    if (whileFrame.BodyBlock != null)
                    {
                        // Execute block statements from StatementIndex
                        for (int i = whileFrame.StatementIndex; i < whileFrame.BodyBlock.Statements.Count; i++)
                        {
                            whileFrame.StatementIndex = i;
                            var bodyStmt = whileFrame.BodyBlock.Statements[i];
                            if (!TryExecuteSync(bodyStmt, out _))
                                await ExecuteStatementAsync(bodyStmt);
                            if (_pendingControl != PendingControl.None)
                                break;
                        }
                    }
                    else
                    {
                        // Single statement body
                        await ExecuteStatementAsync(stmt.Body);
                    }
                    if (_pendingControl != PendingControl.None && await ConsumeWhileBodySignalAsync(whileFrame))
                        return null;
                    // Iteration completed - reset statement index for next iteration
                    whileFrame.StatementIndex = 0;
                }
                catch (ContinueException)
                {
                    // Continue to next iteration
                    // For desugared for loops, the while body is a Block with exactly 2 statements: [body, increment]
                    // When continue is thrown from the body (first statement), we need to execute the increment
                    // before continuing to the next iteration
                    if (whileFrame.BodyBlock != null
                        && whileFrame.Statement.DesugaredForIncrement != null
                        && whileFrame.StatementIndex == 0)
                    {
                        // Desugared for loop - execute the increment (second statement)
                        whileFrame.StatementIndex = 1;
                        await ExecuteAsync(whileFrame.BodyBlock.Statements[1]);
                    }
                    // Reset statement index for next iteration
                    whileFrame.StatementIndex = 0;
                    continue;
                }
                
                // Reset statement index for next iteration
                whileFrame.StatementIndex = 0;
            }
        }
        catch (BreakException)
        {
            // Break out of loop
        }
        finally
        {
            // Pop on every exit, including `return` from inside the loop body.
            PopExecutionFrame(whileFrame);
        }
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteForAsync(ForStatement stmt)
    {
        // For loops are desugared to while loops in the parser
        // The desugared structure is: Block { initializer, While { condition, Block { body, increment } } }
        // Since for loops are desugared, the execution stack will handle the nested while loop
        // We just need to execute the desugared body
        return await ExecuteStatementAsync(stmt.Body);
    }
    
    private async Task<RuntimeValue?> ExecuteForInAsync(ForInStatement stmt)
    {
        var collection = await EvaluateAsync(stmt.Collection);
        
        if (collection.Type != ValueType.Array)
            throw new RuntimeException("for-in loop requires an array.");
        
        var array = collection.AsArray();
        
        // Create a new scope for the loop variable
        _environment = new Environment(_environment);
        
        try
        {
            foreach (var element in array)
            {
                // Assign the current element to the loop variable
                _environment.Define(stmt.VariableName, element);
                
                try
                {
                    await ExecuteStatementAsync(stmt.Body);
                    if (_pendingControl == PendingControl.Return)
                        break;
                    if (_pendingControl == PendingControl.Break)
                    {
                        ClearPendingControl();
                        break;
                    }
                    if (_pendingControl == PendingControl.Continue)
                        ClearPendingControl();
                }
                catch (BreakException)
                {
                    break;
                }
                catch (ContinueException)
                {
                    continue;
                }
            }
        }
        finally
        {
            // Restore the previous environment
            _environment = _environment.GetEnclosing()!;
        }
        
        return null;
    }

    private async Task<RuntimeValue?> ExecuteReturnAsync(ReturnStatement stmt)
    {
        RuntimeValue? value = null;
        if (stmt.Value != null)
        {
            if (!TryEvaluateSync(stmt.Value, out var returned))
                returned = await EvaluateCoreAsync(stmt.Value, false);
            value = returned;
        }
        return SignalControl(PendingControl.Return, value);
    }
    
    private async Task<RuntimeValue?> ExecuteThrowAsync(ThrowStatement stmt)
    {
        var exceptionValue = await EvaluateAsync(stmt.Exception);
        throw new MALDAException(exceptionValue, stmt.Line, _currentFile);
    }
    
    private async Task<RuntimeValue?> ExecuteUsingAsync(UsingStatement stmt)
    {
        return await ExecuteUsingViaImportExecutorAsync(stmt);
    }

    private async Task<RuntimeValue?> ExecuteImportAsync(ImportStatement stmt)
    {
        return await ExecuteImportViaImportExecutorAsync(stmt);
    }
    
    private async Task<RuntimeValue?> ExecuteTryAsync(TryStatement stmt)
    {
        Exception? caughtException = null;
        bool exceptionHandled = false;
        
        try
        {
            await ExecuteBlockAsync(stmt.TryBlock);
        }
        catch (BreakException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (ContinueException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (ReturnException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (MALDAException ex)
        {
            caughtException = ex;
        }
        catch (RuntimeException ex)
        {
            caughtException = ex;
        }
        
        // If an exception was caught, try to match it against catch clauses (Phase 4.5: optional filter)
        if (caughtException != null)
        {
            foreach (var catchClause in stmt.CatchClauses)
            {
                if (!await MatchesCatchClauseAsync(catchClause, caughtException))
                    continue;

                exceptionHandled = true;
                try
                {
                    await ExecuteCatchBodyAsync(catchClause, caughtException);
                }
                catch
                {
                    // break / continue / return / throw out of the catch body: finally still runs.
                    await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
                    throw;
                }
                break;
            }

            // If exception wasn't caught by any clause, execute finally and rethrow
            if (!exceptionHandled)
            {
                await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
                throw caughtException;
            }
        }
        
        // Execute finally block (always executes, whether exception occurred or not)
        await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
        
        return null;
    }
    
    private async Task ExecuteFinallyBlockAsync(BlockStatement? finallyBlock)
    {
        if (finallyBlock != null)
        {
            try
            {
                await ExecuteBlockAsync(finallyBlock);
            }
            catch (BreakException)
            {
                // Control flow exceptions from finally are rethrown
                throw;
            }
            catch (ContinueException)
            {
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            // Other exceptions from finally are also rethrown (they override the original exception)
        }
    }
}
