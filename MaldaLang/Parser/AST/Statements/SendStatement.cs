// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class SendStatement : Statement
{
    // Call-style send syntax: send target.handlerName(arg1, arg2, ...) [then (result) { ... }] [timeout ms] [catch (error) { ... }];
    // Or: send target(arg1, arg2, ...) [then (result) { ... }] [timeout ms] [catch (error) { ... }];
    public Expression Target { get; } // Actor reference (callee object)
    public string? HandlerName { get; } // Handler name from member access (null if not specified)
    public List<Expression> Arguments { get; } // Arguments to pass to handler
    public CallbackDefinition? Callback { get; } // Optional callback for reply
    public Expression? TimeoutMilliseconds { get; } // Optional timeout value in milliseconds
    public CallbackDefinition? TimeoutErrorHandler { get; } // Optional error handler for timeout
    
    public SendStatement(
        Expression target,
        string? handlerName,
        List<Expression> arguments,
        CallbackDefinition? callback = null,
        Expression? timeoutMilliseconds = null,
        CallbackDefinition? timeoutErrorHandler = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Target = target;
        HandlerName = handlerName;
        Arguments = arguments;
        Callback = callback;
        TimeoutMilliseconds = timeoutMilliseconds;
        TimeoutErrorHandler = timeoutErrorHandler;
    }
}
