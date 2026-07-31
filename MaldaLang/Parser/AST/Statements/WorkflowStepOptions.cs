// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;

/// <summary>
/// Options for a workflow step: retry, backoff, delay, maxDelay, timeout, compensate.
/// </summary>
public class WorkflowStepOptions
{
    /// <summary>Retry count (null = no retry).</summary>
    public int? RetryCount { get; }
    /// <summary>Backoff type: "fixed", "linear", or "exponential".</summary>
    public string? Backoff { get; }
    /// <summary>Base delay in ms.</summary>
    public int? DelayMs { get; }
    /// <summary>Max delay cap in ms.</summary>
    public int? MaxDelayMs { get; }
    /// <summary>Step timeout in ms.</summary>
    public int? TimeoutMs { get; }
    /// <summary>Compensation call expression.</summary>
    public Expression? Compensate { get; }

    public WorkflowStepOptions(int? retryCount, string? backoff, int? delayMs, int? maxDelayMs, int? timeoutMs, Expression? compensate)
    {
        RetryCount = retryCount;
        Backoff = backoff;
        DelayMs = delayMs;
        MaxDelayMs = maxDelayMs;
        TimeoutMs = timeoutMs;
        Compensate = compensate;
    }
}
