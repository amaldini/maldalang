// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Statements;

internal class CallbackInfo
{
    public string ParameterName { get; }
    public BlockStatement Body { get; }
    public Environment Environment { get; }
    public int? TimeoutMilliseconds { get; }
    public DateTime? ExpiresAt { get; }
    public CallbackDefinition? TimeoutErrorHandler { get; }
    public ActorReference? TargetRef { get; }
    public string HandlerName { get; }
    public CancellationTokenSource? TimeoutCancellation { get; }

    public CallbackInfo(
        string parameterName, 
        BlockStatement body, 
        Environment environment,
        int? timeoutMilliseconds = null,
        CallbackDefinition? timeoutErrorHandler = null,
        ActorReference? targetRef = null,
        string handlerName = "",
        CancellationTokenSource? timeoutCancellation = null)
    {
        ParameterName = parameterName;
        Body = body;
        Environment = environment;
        TimeoutMilliseconds = timeoutMilliseconds;
        TimeoutErrorHandler = timeoutErrorHandler;
        TargetRef = targetRef;
        HandlerName = handlerName;
        TimeoutCancellation = timeoutCancellation;
        if (timeoutMilliseconds.HasValue && timeoutMilliseconds.Value > 0)
        {
            ExpiresAt = DateTime.Now.AddMilliseconds(timeoutMilliseconds.Value);
        }
    }
}

