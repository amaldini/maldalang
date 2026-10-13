// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Statements;

/// <summary>A <c>return</c>, <c>break</c> or <c>continue</c> unwinding toward its function or loop.</summary>
internal enum PendingControl : byte
{
    None,
    Return,
    Break,
    Continue
}

/// <summary>
/// <c>return</c> / <c>break</c> / <c>continue</c> without exceptions on the hot path.
/// <see cref="ExecuteStatementAsync"/>, blocks, <c>if</c>, loops and function bodies set a
/// pending signal on the activation and unwind by returning; loops and
/// <c>CallFunctionAsync</c> consume it. Every other caller goes through
/// <see cref="ExecuteAsync"/> / <see cref="ExecuteBlockAsync(BlockStatement, Environment)"/>,
/// which turn a signal into the matching <see cref="ReturnException"/>,
/// <see cref="BreakException"/> or <see cref="ContinueException"/> — try/finally, defer,
/// workflows, callbacks and match bodies keep their exception-based handling.
/// </summary>
public partial class Interpreter
{
    private PendingControl _pendingControl
    {
        get => GetActivation().PendingControl;
        set => GetActivation().PendingControl = value;
    }

    private RuntimeValue? SignalControl(PendingControl control, RuntimeValue? returnValue = null)
    {
        var activation = GetActivation();
        activation.PendingControl = control;
        activation.PendingReturnValue = returnValue;
        return null;
    }

    private RuntimeValue? TakePendingReturnValue()
    {
        var activation = GetActivation();
        var value = activation.PendingReturnValue;
        activation.PendingControl = PendingControl.None;
        activation.PendingReturnValue = null;
        return value;
    }

    /// <summary>Runs <paramref name="stmt"/> and rethrows any signal it leaves pending as the legacy exception.</summary>
    internal async Task<RuntimeValue?> ExecuteAsync(Statement stmt)
    {
        var activation = GetActivation();
        var (outerControl, outerValue) = (activation.PendingControl, activation.PendingReturnValue);
        activation.PendingControl = PendingControl.None;
        RuntimeValue? result;
        try
        {
            result = await ExecuteStatementAsync(stmt);
        }
        catch
        {
            ClearPendingControl();
            throw;
        }
        ThrowPendingControl();
        RestorePendingControl(outerControl, outerValue);
        return result;
    }

    /// <summary>Block counterpart of <see cref="ExecuteAsync"/>; defer bodies run here while an outer signal is pending.</summary>
    private async Task<RuntimeValue?> ExecuteBlockAsync(BlockStatement stmt, Environment environment)
    {
        var activation = GetActivation();
        var (outerControl, outerValue) = (activation.PendingControl, activation.PendingReturnValue);
        activation.PendingControl = PendingControl.None;
        try
        {
            await ExecuteBlockStatementsAsync(stmt, environment);
        }
        catch
        {
            ClearPendingControl();
            throw;
        }
        ThrowPendingControl();
        RestorePendingControl(outerControl, outerValue);
        return null;
    }

    /// <summary>
    /// Handles a signal left by one pass of a while body. Returns true when the loop must
    /// exit: on <c>break</c> (consumed) or <c>return</c> (left pending for the function).
    /// </summary>
    private async Task<bool> ConsumeWhileBodySignalAsync(WhileLoopFrame whileFrame)
    {
        switch (_pendingControl)
        {
            case PendingControl.None:
                return false;
            case PendingControl.Continue:
                ClearPendingControl();
                // A desugared for loop's body is [body, increment]: `continue` in the body
                // still runs the increment.
                if (whileFrame.BodyBlock != null
                    && whileFrame.BodyBlock.Statements.Count == 2
                    && whileFrame.StatementIndex == 0)
                {
                    whileFrame.StatementIndex = 1;
                    await ExecuteStatementAsync(whileFrame.BodyBlock.Statements[1]);
                }
                return false;
            case PendingControl.Break:
                ClearPendingControl();
                return true;
            default:
                return true;
        }
    }

    private void ClearPendingControl()
    {
        var activation = GetActivation();
        activation.PendingControl = PendingControl.None;
        activation.PendingReturnValue = null;
    }

    private void RestorePendingControl(PendingControl control, RuntimeValue? value)
    {
        if (control == PendingControl.None)
            return;
        var activation = GetActivation();
        activation.PendingControl = control;
        activation.PendingReturnValue = value;
    }

    private void ThrowPendingControl()
    {
        switch (_pendingControl)
        {
            case PendingControl.None:
                return;
            case PendingControl.Return:
                throw new ReturnException(TakePendingReturnValue());
            case PendingControl.Break:
                ClearPendingControl();
                throw new BreakException();
            default:
                ClearPendingControl();
                throw new ContinueException();
        }
    }
}
