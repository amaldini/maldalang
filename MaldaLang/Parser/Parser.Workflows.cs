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
    private Statement WorkflowDeclaration()
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect workflow name.");
        var name = nameToken.Lexeme;
        Consume(TokenType.LeftParen, "Expect '(' after workflow name.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(ConsumeIdentifierOrInput("Expect parameter name."));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Consume(TokenType.LeftBrace, "Expect '{' before workflow body.");
        var body = WorkflowBlock();
        Consume(TokenType.RightBrace, "Expect '}' after workflow body.");
        return new WorkflowDeclaration(name, parameters, body, nameToken.Line, nameToken.Column);
    }

    private BlockStatement WorkflowBlock()
    {
        var statements = new List<Statement>();
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _workflowStepIds = stepIds;
        _inWorkflowBlock = true;
        _blockDepth++;
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                AppendFlattened(statements, WorkflowStatement(stepIds), applySourceFile: false);
            }
            return new BlockStatement(statements);
        }
        finally
        {
            _blockDepth--;
            _inWorkflowBlock = false;
            _workflowStepIds = null;
        }
    }

    private Statement? WorkflowStatement(HashSet<string> stepIds)
    {
        try
        {
            if (Match(TokenType.Step))
                return StepStatement(stepIds);
            if (Match(TokenType.Approval))
                return ApprovalStatement();
            if (Match(TokenType.Wait))
                return WaitSignalStatement();
            if (Match(TokenType.Var, TokenType.Const))
                return VarDeclaration(isConst: Previous().Type == TokenType.Const);
            return Statement();
        }
        catch (ParseException ex)
        {
            _errors.Add(ex);
            Synchronize();
            return null;
        }
    }

    private Statement StepStatement(HashSet<string> stepIds)
    {
        var token = Previous();
        var stepId = ConsumeIdentifierLike("Expect step identifier.");
        Consume(TokenType.Assign, "Expect '=' after step identifier.");
        var callExpr = Call();
        var options = ParseStepOptions();
        Consume(TokenType.Semicolon, "Expect ';' after step statement.");

        if (stepIds.Contains(stepId))
            _errors.Add(Error(Current() ?? new Token(TokenType.Identifier, stepId, null, token.Line, token.Column), $"Duplicate step identifier '{stepId}' in same workflow scope.", "WF1003"));
        else
            stepIds.Add(stepId);

        if (options != null)
            ValidateStepOptions(options, token);

        return new WorkflowStepStatement(stepId, callExpr, options, token.Line, token.Column);
    }

    private WorkflowStepOptions? ParseStepOptions()
    {
        int? retryCount = null;
        string? backoff = null;
        int? delayMs = null;
        int? maxDelayMs = null;
        int? timeoutMs = null;
        Expression? compensate = null;

        while (true)
        {
            if (Match(TokenType.Retry))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'retry'.");
                retryCount = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Backoff))
            {
                var s = Consume(TokenType.String, "Expect string after 'backoff' (e.g. \"fixed\", \"linear\", \"exponential\").");
                backoff = s.Literal as string ?? s.Lexeme.Trim('"');
            }
            else if (Match(TokenType.Delay))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'delay'.");
                delayMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.MaxDelay))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'maxDelay'.");
                maxDelayMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Timeout))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
                timeoutMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Compensate))
            {
                compensate = Call();
            }
            else
                break;
        }

        if (retryCount == null && backoff == null && delayMs == null && maxDelayMs == null && timeoutMs == null && compensate == null)
            return null;

        return new WorkflowStepOptions(retryCount, backoff, delayMs, maxDelayMs, timeoutMs, compensate);
    }

    private void ValidateStepOptions(WorkflowStepOptions options, Token stepToken)
    {
        if (options.Backoff != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'backoff' requires 'retry' to be specified.", "WF1004"));
        if (options.DelayMs != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'delay' requires 'retry' to be specified.", "WF1004"));
        if (options.MaxDelayMs != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'maxDelay' requires 'retry' to be specified.", "WF1004"));
        if (options.Backoff != null && options.Backoff != "fixed" && options.Backoff != "linear" && options.Backoff != "exponential")
            _errors.Add(Error(stepToken, $"Invalid backoff '{options.Backoff}'. Use \"fixed\", \"linear\", or \"exponential\".", "WF1004"));
        if (options.RetryCount.HasValue && options.RetryCount.Value < 0)
            _errors.Add(Error(stepToken, "Retry count must be >= 0.", "WF1004"));
    }

    private Statement ApprovalStatement()
    {
        var token = Previous();
        var approvalId = ConsumeIdentifierLike("Expect approval identifier.");
        Consume(TokenType.Assign, "Expect '=' after approval identifier.");
        Consume(TokenType.Approval, "Expect 'approval' after '='.");
        Consume(TokenType.LeftParen, "Expect '(' after 'approval'.");
        var nameExpr = Expression();
        Expression payloadExpr;
        if (Match(TokenType.Comma))
            payloadExpr = Expression();
        else
            payloadExpr = new LiteralExpression(null, nameExpr.Line, nameExpr.Column);
        Consume(TokenType.RightParen, "Expect ')' after approval arguments.");

        int? timeoutMs = null;
        Expression? onReject = null;
        while (true)
        {
            if (Match(TokenType.Timeout))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
                timeoutMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.OnReject))
            {
                onReject = Call();
            }
            else
                break;
        }
        Consume(TokenType.Semicolon, "Expect ';' after approval statement.");

        return new WorkflowApprovalStatement(approvalId, nameExpr, payloadExpr, timeoutMs, onReject, token.Line, token.Column);
    }

    private Statement WaitSignalStatement()
    {
        var token = Previous();
        var signalId = ConsumeIdentifierLike("Expect signal identifier.");
        Consume(TokenType.Assign, "Expect '=' after signal identifier.");
        if (!Check(TokenType.Identifier) || Peek().Lexeme != "awaitSignal")
            throw Error(Peek(), "Expect 'awaitSignal' after '=' in wait statement.");
        Advance(); // consume awaitSignal
        Consume(TokenType.LeftParen, "Expect '(' after 'awaitSignal'.");
        var nameExpr = Expression();
        Expression payloadExpr;
        if (Match(TokenType.Comma))
            payloadExpr = Expression();
        else
            payloadExpr = new LiteralExpression(null, nameExpr.Line, nameExpr.Column);
        Consume(TokenType.RightParen, "Expect ')' after awaitSignal arguments.");

        int? timeoutMs = null;
        while (Match(TokenType.Timeout))
        {
            var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
            timeoutMs = (int)(n.Literal ?? 0);
        }
        Consume(TokenType.Semicolon, "Expect ';' after wait statement.");

        return new WorkflowAwaitSignalStatement(signalId, nameExpr, payloadExpr, timeoutMs, token.Line, token.Column);
    }
}
