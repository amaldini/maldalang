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

/// <summary>Context when executing inside a workflow body (for step journaling and WF1001/WF1002 checks).</summary>
internal sealed class WorkflowExecutionContext
{
    public string InstanceId { get; }
    public string WorkflowName { get; }
    public Environment BodyEnv { get; }
    public List<CompensableStepRegistration> CompensableSteps { get; } = new();

    public WorkflowExecutionContext(string instanceId, string workflowName, Environment bodyEnv)
    {
        InstanceId = instanceId;
        WorkflowName = workflowName;
        BodyEnv = bodyEnv;
    }

    public void UpsertCompensableStep(string stepId, Expression compensateExpression)
    {
        CompensableSteps.RemoveAll(s => s.StepId == stepId);
        CompensableSteps.Add(new CompensableStepRegistration(stepId, compensateExpression));
    }
}

internal sealed class WorkflowPausedException : Exception
{
    public WorkflowPausedException(string message) : base(message) { }
}

internal sealed class CompensableStepRegistration
{
    public string StepId { get; }
    public Expression CompensateExpression { get; }

    public CompensableStepRegistration(string stepId, Expression compensateExpression)
    {
        StepId = stepId;
        CompensateExpression = compensateExpression;
    }
}

public partial class Interpreter
{
    /// <summary>Runs a workflow body with the given input and instance ID. Used by startWorkflow built-in.</summary>
    internal async Task<RuntimeValue?> RunWorkflowBodyAsync(WorkflowDeclaration decl, RuntimeValue input, string instanceId)
    {
        var env = new Environment(_globals);
        if (decl.Parameters.Count > 0)
            env.Define(decl.Parameters[0], input);
        var prevCtx = _workflowContext;
        _workflowContext = new WorkflowExecutionContext(instanceId, decl.Name, env);
        var engine = WorkflowEngine.Instance;
        try
        {
            var prevEnv = _environment;
            _environment = env;
            try
            {
                // Execute workflow body statements directly (no BlockFrame) so step results land in env.
                foreach (var st in decl.Body.Statements)
                {
                    var inst = engine.GetInstance(instanceId);
                    if (inst != null && inst.Status == WorkflowStatus.Cancelled)
                        throw new RuntimeException("Workflow cancelled");
                    GetActivation().CancelToken.ThrowIfCancellationRequested();
                    await ExecuteAsync(st);
                }
                engine.CompleteInstance(instanceId, null);
                return null;
            }
            catch (WorkflowPausedException)
            {
                // Expected pause point for approval/signal waits; instance remains in waiting state.
                return null;
            }
            catch (ReturnException ret)
            {
                var resultJson = ret.Value != null ? BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { ret.Value }, this).AsString() : null;
                engine.CompleteInstance(instanceId, resultJson);
                return ret.Value;
            }
            catch (Exception ex)
            {
                var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().Name });
                var compensated = await ExecuteCompensationAsync(_workflowContext!, err);
                if (!compensated)
                    engine.FailInstance(instanceId, err);
                throw;
            }
            finally
            {
                _environment = prevEnv;
            }
        }
        finally
        {
            _workflowContext = prevCtx;
        }
    }

    internal bool IsInWorkflowContext => _workflowContext != null;
    internal bool IsInsideWorkflowStep => _insideWorkflowStep;

    private void EnsureWorkflowContext()
    {
        if (_workflowContext == null)
            throw new RuntimeException("Workflow step can only be executed inside a workflow body");
    }

    private async Task<RuntimeValue?> ExecuteWorkflowStepAsync(WorkflowStepStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var maxAttempts = (stmt.Options?.RetryCount ?? 0) + 1;
        if (maxAttempts < 1) maxAttempts = 1;
        var timeoutMs = stmt.Options?.TimeoutMs;
        var backoff = stmt.Options?.Backoff;
        var delayMs = stmt.Options?.DelayMs;
        var maxDelayMs = stmt.Options?.MaxDelayMs;

        // Define step result in workflow body env so it is visible to subsequent statements (print, return).
        var targetEnv = ctx.BodyEnv;

        // Replay contract: if prior successful result exists, use it (no re-exec)
        var replay = engine.GetReplayResult(ctx.InstanceId, stmt.StepId);
        if (replay != null)
        {
            var json = replay.OutputJson ?? "null";
            var parsed = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(json) }, this);
            targetEnv.Define(stmt.StepId, parsed);
            if (stmt.Options?.Compensate != null && replay.State == StepState.Succeeded)
                ctx.UpsertCompensableStep(stmt.StepId, stmt.Options.Compensate);
            return null;
        }

        var latestAttempt = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.StepId);
        var attempt = latestAttempt != null ? latestAttempt.Attempt + 1 : 1;
        while (attempt <= maxAttempts)
        {
            var stepId = Guid.NewGuid().ToString("N");
            engine.JournalStepStart(stepId, ctx.InstanceId, stmt.StepId, attempt, maxAttempts, timeoutMs, "{}", null);

            InterpreterCallStackFrame? stepFrame = null;
            try
            {
                _insideWorkflowStep = true;
                stepFrame = new InterpreterCallStackFrame
                {
                    FunctionName = "step " + stmt.StepId,
                    Line = stmt.Line,
                    File = _currentFile ?? string.Empty,
                    Environment = _environment ?? ctx.BodyEnv
                };
                _callStack.Add(stepFrame);
                var (timedOut, result) = await EvaluateWorkflowStepWithTimeoutAsync(stmt.CallExpression, timeoutMs);
                if (timedOut)
                {
                    var timeoutError = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "StepTimeoutError",
                        step = stmt.StepId,
                        attempt,
                        timeoutMs,
                        isRetryable = attempt < maxAttempts,
                        message = $"Step '{stmt.StepId}' timed out after {timeoutMs}ms"
                    });
                    engine.JournalStepTimeout(stepId, ctx.InstanceId, stmt.StepId, attempt, timeoutError);
                    if (attempt >= maxAttempts)
                    {
                        throw new RuntimeException($"Step '{stmt.StepId}' timed out after {timeoutMs}ms (max attempts reached)");
                    }

                    var retryDelayMs = engine.ComputeRetryDelayMs(ctx.InstanceId, stmt.StepId, attempt, backoff, delayMs, maxDelayMs);
                    engine.JournalStepRetryScheduled(ctx.InstanceId, stmt.StepId, attempt, attempt + 1, retryDelayMs, "timeout");
                    if (retryDelayMs > 0)
                        await Task.Delay(retryDelayMs);
                    attempt++;
                    continue;
                }

                var outputJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { result! }, this).AsString();
                engine.JournalStepSuccess(stepId, ctx.InstanceId, stmt.StepId, attempt, outputJson);
                targetEnv.Define(stmt.StepId, result!);
                if (stmt.Options?.Compensate != null)
                    ctx.UpsertCompensableStep(stmt.StepId, stmt.Options.Compensate);
                return null;
            }
            catch (Exception ex)
            {
                var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().Name, attempt, isRetryable = attempt < maxAttempts });
                engine.JournalStepFailure(stepId, ctx.InstanceId, stmt.StepId, attempt, err);
                if (attempt >= maxAttempts)
                    throw;

                var retryDelayMs = engine.ComputeRetryDelayMs(ctx.InstanceId, stmt.StepId, attempt, backoff, delayMs, maxDelayMs);
                engine.JournalStepRetryScheduled(ctx.InstanceId, stmt.StepId, attempt, attempt + 1, retryDelayMs, "failure");
                if (retryDelayMs > 0)
                    await Task.Delay(retryDelayMs);
                attempt++;
            }
            finally
            {
                if (stepFrame != null)
                {
                    if (_callStack.Count > 0 && ReferenceEquals(_callStack[_callStack.Count - 1], stepFrame))
                        _callStack.RemoveAt(_callStack.Count - 1);
                    else
                        _callStack.Remove(stepFrame);
                }
                _insideWorkflowStep = false;
            }
        }

        throw new RuntimeException($"Step '{stmt.StepId}' failed after {maxAttempts} attempts");
    }

    private async Task<(bool timedOut, RuntimeValue? result)> EvaluateWorkflowStepWithTimeoutAsync(Expression expression, int? timeoutMs)
    {
        if (!timeoutMs.HasValue || timeoutMs.Value <= 0)
        {
            var result = await EvaluateAsync(expression);
            return (false, result);
        }

        var stepTask = EvaluateAsync(expression);
        var completed = await Task.WhenAny(stepTask, Task.Delay(timeoutMs.Value));
        if (completed != stepTask)
            return (true, null);

        return (false, await stepTask);
    }

    private async Task<RuntimeValue?> ExecuteWorkflowApprovalAsync(WorkflowApprovalStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var targetEnv = ctx.BodyEnv;

        var latest = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.ApprovalId);
        if (latest != null)
        {
            if (latest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(latest.OutputJson))
            {
                var resolved = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(latest.OutputJson) }, this);
                targetEnv.Define(stmt.ApprovalId, resolved);
                var decision = TryGetStringFieldFromJson(latest.OutputJson, "decision");
                if (decision == "reject")
                {
                    if (stmt.OnReject != null)
                    {
                        _insideWorkflowStep = true;
                        try
                        {
                            await EvaluateAsync(stmt.OnReject);
                        }
                        finally
                        {
                            _insideWorkflowStep = false;
                        }
                    }
                    else
                    {
                        throw new RuntimeException($"Approval '{stmt.ApprovalId}' was rejected.");
                    }
                }

                if (decision == "timeout")
                    throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");

                return null;
            }

            if (latest.State == StepState.TimedOut)
                throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");
        }

        var approvalNameValue = await EvaluateAsync(stmt.ApprovalNameExpr);
        var approvalName = approvalNameValue.Type == ValueType.String ? approvalNameValue.AsString() : approvalNameValue.ToString();
        var payload = await EvaluateAsync(stmt.PayloadExpr);
        var payloadJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { payload }, this).AsString();
        if (!engine.EnterApprovalWait(ctx.InstanceId, stmt.ApprovalId, approvalName, stmt.TimeoutMs, payloadJson, out var enterError))
            throw new RuntimeException(enterError ?? $"Failed to enter approval wait for '{stmt.ApprovalId}'.");

        if (latest != null && latest.State == StepState.Running && IsWaitTimedOut(latest.StartedAtUtc, stmt.TimeoutMs))
        {
            if (engine.TimeoutWaitingStep(ctx.InstanceId, stmt.ApprovalId, "approval", "ApprovalTimeoutError", out var timeoutError))
                throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");
            throw new RuntimeException(timeoutError ?? $"Approval '{stmt.ApprovalId}' wait timeout handling failed.");
        }

        throw new WorkflowPausedException($"Workflow paused waiting for approval '{stmt.ApprovalId}'.");
    }

    private async Task<RuntimeValue?> ExecuteWorkflowAwaitSignalAsync(WorkflowAwaitSignalStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var targetEnv = ctx.BodyEnv;

        var latest = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.SignalId);
        if (latest != null)
        {
            if (latest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(latest.OutputJson))
            {
                var payloadFieldJson = TryGetJsonField(latest.OutputJson, "payload") ?? "null";
                var payloadValue = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(payloadFieldJson) }, this);
                targetEnv.Define(stmt.SignalId, payloadValue);
                return null;
            }

            if (latest.State == StepState.TimedOut)
                throw new RuntimeException($"Signal wait '{stmt.SignalId}' timed out.");
        }

        var signalNameValue = await EvaluateAsync(stmt.SignalNameExpr);
        var signalName = signalNameValue.Type == ValueType.String ? signalNameValue.AsString() : signalNameValue.ToString();
        var correlation = await EvaluateAsync(stmt.PayloadExpr);
        var correlationJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { correlation }, this).AsString();
        if (!engine.EnterSignalWait(ctx.InstanceId, stmt.SignalId, signalName, stmt.TimeoutMs, correlationJson, out var enterError))
            throw new RuntimeException(enterError ?? $"Failed to enter signal wait for '{stmt.SignalId}'.");

        if (latest != null && latest.State == StepState.Running && IsWaitTimedOut(latest.StartedAtUtc, stmt.TimeoutMs))
        {
            if (engine.TimeoutWaitingStep(ctx.InstanceId, stmt.SignalId, "signal_wait", "SignalTimeoutError", out var timeoutError))
                throw new RuntimeException($"Signal wait '{stmt.SignalId}' timed out.");
            throw new RuntimeException(timeoutError ?? $"Signal wait '{stmt.SignalId}' timeout handling failed.");
        }

        throw new WorkflowPausedException($"Workflow paused waiting for signal '{stmt.SignalId}'.");
    }

    private async Task<bool> ExecuteCompensationAsync(WorkflowExecutionContext ctx, string rootErrorJson)
    {
        if (ctx.CompensableSteps.Count == 0)
            return false;

        var engine = WorkflowEngine.Instance;
        engine.BeginCompensation(ctx.InstanceId, rootErrorJson);
        var allSucceeded = true;
        var diagnostics = new List<Dictionary<string, object?>>();

        for (var i = ctx.CompensableSteps.Count - 1; i >= 0; i--)
        {
            var compensable = ctx.CompensableSteps[i];
            var compensationStepName = $"{compensable.StepId}__compensate";
            var replay = engine.GetLatestStepAttempt(ctx.InstanceId, compensationStepName);
            if (replay != null && replay.State == StepState.Compensated)
                continue;

            var latestAttempt = engine.GetLatestStepAttempt(ctx.InstanceId, compensationStepName);
            var attempt = latestAttempt != null ? latestAttempt.Attempt + 1 : 1;
            var compensationStepId = Guid.NewGuid().ToString("N");
            engine.JournalCompensationStart(compensationStepId, ctx.InstanceId, compensationStepName, attempt, "{}");

            try
            {
                _insideWorkflowStep = true;
                var result = await EvaluateAsync(compensable.CompensateExpression);
                var outputJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { result }, this).AsString();
                engine.JournalCompensationSuccess(compensationStepId, ctx.InstanceId, compensationStepName, attempt, outputJson);
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                var compError = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "CompensationError",
                    step = compensable.StepId,
                    compensationStep = compensationStepName,
                    attempt,
                    message = ex.Message
                });
                diagnostics.Add(new Dictionary<string, object?>
                {
                    ["step"] = compensable.StepId,
                    ["compensationStep"] = compensationStepName,
                    ["attempt"] = attempt,
                    ["message"] = ex.Message
                });
                engine.JournalCompensationFailure(compensationStepId, ctx.InstanceId, compensationStepName, attempt, compError);
            }
            finally
            {
                _insideWorkflowStep = false;
            }
        }

        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "CompensationSummary",
            allSucceeded,
            failures = diagnostics
        });
        engine.FinishCompensation(ctx.InstanceId, allSucceeded, diagnosticsJson);
        return true;
    }

    private static bool IsWaitTimedOut(string? startedAtUtc, int? timeoutMs)
    {
        if (!timeoutMs.HasValue || timeoutMs.Value <= 0 || string.IsNullOrWhiteSpace(startedAtUtc))
            return false;
        if (!DateTime.TryParse(startedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            return false;
        if (started.Kind == DateTimeKind.Unspecified)
            started = DateTime.SpecifyKind(started, DateTimeKind.Utc);
        return (DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds >= timeoutMs.Value;
    }

    private static string? TryGetStringFieldFromJson(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var field) && field.ValueKind == JsonValueKind.String)
                return field.GetString();
        }
        catch
        {
            // ignore malformed payloads and let caller apply fallback behavior
        }

        return null;
    }

    private static string? TryGetJsonField(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var field))
                return field.GetRawText();
        }
        catch
        {
            // ignore malformed payloads and let caller apply fallback behavior
        }

        return null;
    }
}
