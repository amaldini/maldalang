// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Phase 4.5: tagged <c>catch (e if …)</c> matching.
/// </summary>
public partial class Interpreter
{
    private static RuntimeValue ToCatchValue(Exception exception) =>
        exception switch
        {
            MALDAException malda => malda.Value,
            RuntimeException runtime => RuntimeValue.String(runtime.Message),
            _ => RuntimeValue.String(exception.Message)
        };

    private async Task<bool> MatchesCatchClauseAsync(CatchClause clause, Exception caughtException)
    {
        if (clause.Filter == null)
            return true;

        if (string.IsNullOrEmpty(clause.ExceptionVariable))
            return false;

        var exceptionValue = ToCatchValue(caughtException);
        var catchEnvironment = new Environment(_environment);
        catchEnvironment.Define(clause.ExceptionVariable, exceptionValue);
        var previous = _environment;
        _environment = catchEnvironment;
        try
        {
            var result = await EvaluateAsync(clause.Filter);
            return CoerceToBoolean(result);
        }
        finally
        {
            _environment = previous;
        }
    }

    private async Task ExecuteCatchBodyAsync(CatchClause catchClause, Exception caughtException)
    {
        if (catchClause.ExceptionVariable != null)
        {
            var exceptionValue = ToCatchValue(caughtException);
            var catchEnvironment = new Environment(_environment);
            catchEnvironment.Define(catchClause.ExceptionVariable, exceptionValue);
            var previousEnv = _environment;
            _environment = catchEnvironment;
            try
            {
                await ExecuteBlockAsync(catchClause.Body);
            }
            finally
            {
                _environment = previousEnv;
            }
        }
        else
        {
            await ExecuteBlockAsync(catchClause.Body);
        }
    }

    private static bool CoerceToBoolean(RuntimeValue value)
    {
        if (value.Type == ValueType.Boolean)
            return value.AsBoolean();
        if (value.Type == ValueType.Null)
            return false;
        if (value.Type == ValueType.Integer)
            return value.AsInteger() != 0;
        if (value.Type == ValueType.Float)
            return Math.Abs(value.AsFloat()) > double.Epsilon;
        if (value.Type == ValueType.String)
            return !string.IsNullOrEmpty(value.AsString());
        return true;
    }
}
