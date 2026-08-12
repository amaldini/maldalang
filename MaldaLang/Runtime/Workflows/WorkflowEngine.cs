// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Workflows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

/// <summary>
/// Workflow instance lifecycle states per spec section 5.1.
/// </summary>
public static class WorkflowStatus
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string WaitingApproval = "WAITING_APPROVAL";
    public const string WaitingSignal = "WAITING_SIGNAL";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Compensating = "COMPENSATING";
    public const string Compensated = "COMPENSATED";
}

/// <summary>
/// Step states per spec section 5.1.
/// </summary>
public static class StepState
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string TimedOut = "TIMED_OUT";
    public const string Skipped = "SKIPPED";
    public const string Compensating = "COMPENSATING";
    public const string Compensated = "COMPENSATED";
    public const string CompensationFailed = "COMPENSATION_FAILED";
}

/// <summary>
/// Central workflow engine: manages persistence, instance lifecycle, and step journaling.
/// </summary>
public sealed class WorkflowEngine
{
    private static WorkflowEngine? _instance;
    private static readonly object _lock = new();
    private IWorkflowStorageProvider? _persistence;
    private string _connectionString;
    private string _providerName;
    private bool _startupRecoveryCompleted;
    private WorkflowRuntimeOptions _runtimeOptions;
    private const int DefaultRetryDelayMs = 1000;
    private const int DefaultRetryMaxDelayMs = 60000;
    private const int DefaultStaleRunningLeaseMs = 120000;

    public bool EnableRetryJitter { get; set; } = true;

    public static WorkflowEngine Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new WorkflowEngine();
                }
            }
            return _instance;
        }
    }

    public static void ResetForTesting(string? connectionString = null)
    {
        lock (_lock)
        {
            _instance?._persistence?.Dispose();
            _instance = new WorkflowEngine(connectionString);
        }
    }

    private WorkflowEngine(string? connectionString = null)
    {
        _connectionString =
            connectionString ??
            System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_CONNECTION") ??
            WorkflowPersistence.DefaultConnectionString;
        _providerName =
            System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_PROVIDER") ??
            "sqlite";
        _runtimeOptions = WorkflowRuntimeOptions.FromEnvironment();
    }

    private IWorkflowStorageProvider Persistence
    {
        get
        {
            if (_persistence == null)
            {
                lock (_lock)
                {
                    _persistence ??= CreateStorageProvider(_providerName, _connectionString);
                    if (!_startupRecoveryCompleted)
                    {
                        RecoverStaleRunningState();
                        _startupRecoveryCompleted = true;
                    }
                }
            }
            return _persistence;
        }
    }

    public void SetConnectionString(string connectionString)
    {
        lock (_lock)
        {
            _persistence?.Dispose();
            _persistence = null;
            _connectionString = connectionString;
        }
    }

    private static IWorkflowStorageProvider CreateStorageProvider(string providerName, string connectionString)
    {
        if (string.Equals(providerName, "sqlite", StringComparison.OrdinalIgnoreCase))
            return new WorkflowPersistence(connectionString);
        throw new NotSupportedException($"Unsupported workflow storage provider '{providerName}'. Supported providers: sqlite.");
    }

    public WorkflowRuntimeOptions GetRuntimeOptions()
    {
        lock (_lock)
        {
            return _runtimeOptions.Clone();
        }
    }

    public void ConfigureRuntimeOptions(WorkflowRuntimeOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        options.ValidateOrThrow();
        lock (_lock)
        {
            _runtimeOptions = options.Clone();
        }
    }

    public void EnsureWorkflowsEnabled(string operation)
    {
        var options = GetRuntimeOptions();
        if (!options.Enabled)
            throw new InvalidOperationException($"Workflow runtime is disabled by configuration. Cannot perform '{operation}'.");
    }

    /// <summary>Creates a PENDING workflow instance. Returns instance ID.</summary>
    public string CreateInstance(string workflowName, string? inputJson, string? correlationId = null)
    {
        EnsureWorkflowsEnabled("create");
        EnsurePayloadWithinLimit("workflow input", inputJson);
        var id = Guid.NewGuid().ToString("N");
        var now = Persistence.UtcNow();
        var rec = new WorkflowInstanceRecord
        {
            Id = id,
            Name = workflowName,
            InputJson = inputJson,
            Status = WorkflowStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CorrelationId = correlationId
        };
        Persistence.CreateInstance(rec);
        var payload = BuildEventPayload(
            id,
            stepName: null,
            attempt: null,
            detailsJson: JsonSerializer.Serialize(new { workflowName, status = WorkflowStatus.Pending }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), id, "workflow_created", payload);
        return id;
    }

    /// <summary>Transitions PENDING -> RUNNING. Returns true if transition applied.</summary>
    public bool StartInstance(string instanceId)
    {
        EnsureWorkflowsEnabled("start");
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null || inst.Status != WorkflowStatus.Pending)
            return false;
        var now = Persistence.UtcNow();
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Running, startedAt: now);
        var payload = BuildEventPayload(instanceId, null, null, JsonSerializer.Serialize(new { status = WorkflowStatus.Running }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_started", payload);
        return true;
    }

    /// <summary>Transitions to COMPLETED with result.</summary>
    public void CompleteInstance(string instanceId, string? resultJson)
    {
        EnsurePayloadWithinLimit("workflow result", resultJson);
        var now = Persistence.UtcNow();
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Completed, resultJson: resultJson, finishedAt: now);
        var payload = BuildEventPayload(instanceId, null, null, resultJson ?? "{}");
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_completed", payload);
    }

    /// <summary>Transitions to FAILED with error.</summary>
    public void FailInstance(string instanceId, string? errorJson)
    {
        EnsurePayloadWithinLimit("workflow error", errorJson);
        var now = Persistence.UtcNow();
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Failed, errorJson: errorJson, finishedAt: now);
        var payload = BuildEventPayload(instanceId, null, null, errorJson ?? "{}");
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_failed", payload);
    }

    /// <summary>Transitions to CANCELLED. Returns true if transition applied.</summary>
    public bool CancelInstance(string instanceId, string? reason = null)
    {
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null) return false;
        var terminal = new HashSet<string> { WorkflowStatus.Completed, WorkflowStatus.Failed, WorkflowStatus.Cancelled, WorkflowStatus.Compensated };
        if (terminal.Contains(inst.Status)) return false;
        var now = Persistence.UtcNow();
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Cancelled, errorJson: reason != null ? "{\"reason\":\"" + reason.Replace("\"", "\\\"") + "\"}" : null, finishedAt: now);
        var payload = BuildEventPayload(instanceId, null, null, reason ?? "{}");
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_cancelled", payload);
        return true;
    }

    /// <summary>Marks FAILED instance for retry (status stays FAILED; runtime will re-run from failed step).</summary>
    public bool RetryInstance(string instanceId)
    {
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null || inst.Status != WorkflowStatus.Failed)
            return false;
        var now = Persistence.UtcNow();
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Running, errorJson: null, finishedAt: null);
        var payload = BuildEventPayload(instanceId, null, null, JsonSerializer.Serialize(new { status = WorkflowStatus.Running }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_retry", payload);
        return true;
    }

    /// <summary>Resume WAITING_APPROVAL or WAITING_SIGNAL -> RUNNING. For Sprint 2 we treat resume as no-op for RUNNING.</summary>
    public bool ResumeInstance(string instanceId)
    {
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null) return false;
        if (inst.Status == WorkflowStatus.WaitingApproval || inst.Status == WorkflowStatus.WaitingSignal)
        {
            Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Running);
            var payload = BuildEventPayload(instanceId, null, null, JsonSerializer.Serialize(new { status = WorkflowStatus.Running }));
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_resumed", payload);
            return true;
        }
        return false;
    }

    public WorkflowInstanceRecord? GetInstance(string instanceId) => Persistence.GetInstance(instanceId);
    public IReadOnlyList<WorkflowStepRecord> GetSteps(string instanceId) => Persistence.GetSteps(instanceId);
    public IReadOnlyList<WorkflowEventRecord> GetEvents(string instanceId, int limit = 200) => Persistence.GetEvents(instanceId, limit);
    public IReadOnlyList<WorkflowDeadLetterRecord> ListDeadLetters(int limit = 100, bool includeRequeued = true) =>
        Persistence.ListDeadLetters(limit, includeRequeued);

    /// <summary>
    /// Compose instance + steps + timeline events + related dead letters for ops inspection.
    /// Returns null when the instance does not exist.
    /// </summary>
    public WorkflowOpsReport? GetOpsReport(string instanceId, int eventLimit = 200)
    {
        if (eventLimit < 1) eventLimit = 1;
        if (eventLimit > 10000) eventLimit = 10000;

        var instance = Persistence.GetInstance(instanceId);
        if (instance == null) return null;

        var steps = Persistence.GetSteps(instanceId);
        var events = Persistence.GetEvents(instanceId, eventLimit);
        var deadLetters = Persistence.ListDeadLetters(1000, includeRequeued: true)
            .Where(d => string.Equals(d.WorkflowInstanceId, instanceId, StringComparison.Ordinal))
            .ToList();

        return new WorkflowOpsReport
        {
            Instance = instance,
            Steps = steps,
            Events = events,
            DeadLetters = deadLetters,
            GeneratedAtUtc = Persistence.UtcNow(),
            EventLimit = eventLimit
        };
    }
    public bool RequeueDeadLetter(string deadLetterId) =>
        RequeueDeadLetter(deadLetterId, requeueReason: null, requestedBy: null, requeueCorrelationId: null, out _);

    public bool RequeueDeadLetter(string deadLetterId, string? requeueReason, string? requestedBy, string? requeueCorrelationId, out string? error)
    {
        error = null;
        var deadLetter = Persistence.GetDeadLetter(deadLetterId);
        if (deadLetter == null)
        {
            error = $"Dead letter not found: {deadLetterId}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(deadLetter.RequeuedAtUtc))
        {
            error = $"Dead letter already requeued: {deadLetterId}";
            return false;
        }

        var instance = Persistence.GetInstance(deadLetter.WorkflowInstanceId);
        if (instance == null)
        {
            error = $"Workflow instance not found for dead letter: {deadLetter.WorkflowInstanceId}";
            return false;
        }

        if (instance.Status != WorkflowStatus.Failed &&
            instance.Status != WorkflowStatus.Cancelled &&
            instance.Status != WorkflowStatus.Compensated)
        {
            error = $"WF1006: Illegal transition - instance is {instance.Status}, expected FAILED/CANCELLED/COMPENSATED.";
            return false;
        }

        var requeuedAt = Persistence.UtcNow();
        var marked = Persistence.MarkDeadLetterRequeued(new WorkflowDeadLetterRequeueRequest
        {
            DeadLetterId = deadLetterId,
            RequeuedAtUtc = requeuedAt,
            RequeueReason = requeueReason,
            RequeueRequestedBy = requestedBy,
            RequeueCorrelationId = requeueCorrelationId
        });
        if (!marked)
        {
            error = $"Dead letter could not be requeued: {deadLetterId}";
            return false;
        }

        Persistence.UpdateInstanceStatus(instance.Id, WorkflowStatus.Running, errorJson: null, finishedAt: null);
        var payload = BuildEventPayload(
            instance.Id,
            deadLetter.StepName,
            null,
            JsonSerializer.Serialize(new
            {
                deadLetterId,
                deadLetter.Reason,
                deadLetterCreatedAtUtc = deadLetter.CreatedAtUtc,
                requeueReason,
                requestedBy,
                requeueCorrelationId,
                requeuedAtUtc = requeuedAt
            }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instance.Id, "workflow_dead_letter_requeued", payload);
        return true;
    }

    public WorkflowMaintenanceReport RunMaintenanceJob(WorkflowMaintenanceOptions options)
    {
        options.ValidateOrThrow();
        var report = Persistence.RunMaintenance(options);
        return report;
    }

    public IReadOnlyList<WorkflowInstanceRecord> ListInstances(string? status = null, string? name = null, int limit = 100) =>
        Persistence.ListInstances(status, name, limit);

    /// <summary>Replay contract: if a prior successful step exists, return its output (no re-exec). Otherwise null.</summary>
    public WorkflowStepRecord? GetReplayResult(string instanceId, string stepName)
    {
        return Persistence.GetLatestSuccessfulStep(instanceId, stepName);
    }

    public WorkflowStepRecord? GetLatestStepAttempt(string instanceId, string stepName)
    {
        return Persistence.GetLatestStep(instanceId, stepName);
    }

    /// <summary>Journal step: persist RUNNING, then after execution persist terminal state. Atomic per step.</summary>
    public void JournalStepStart(string stepId, string workflowInstanceId, string stepName, int attempt, int maxAttempts,
        int? timeoutMs, string? inputJson, string? idempotencyKey)
    {
        JournalStepStart(stepId, workflowInstanceId, stepName, "normal", attempt, maxAttempts, timeoutMs, inputJson, idempotencyKey, "workflow_step_started");
    }

    public void JournalStepStart(string stepId, string workflowInstanceId, string stepName, string stepKind, int attempt, int maxAttempts,
        int? timeoutMs, string? inputJson, string? idempotencyKey, string startedEventType)
    {
        EnsureWorkflowRuntimeWithinLimit(workflowInstanceId, stepName);
        EnsureStepAttemptWithinLimit(stepName, maxAttempts);
        EnsurePayloadWithinLimit("step input", inputJson);
        var now = Persistence.UtcNow();
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = stepKind,
            State = StepState.Running,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            TimeoutMs = timeoutMs,
            InputJson = inputJson,
            IdempotencyKey = idempotencyKey ?? $"wf:{workflowInstanceId}:step:{stepName}",
            StartedAtUtc = now
        };
        Persistence.UpsertStep(rec);
        var details = JsonSerializer.Serialize(new
        {
            step = stepName,
            attempt,
            maxAttempts,
            timeoutMs,
            stepKind
        });
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, details);
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), workflowInstanceId, startedEventType, payload);
    }

    public void JournalStepSuccess(string stepId, string workflowInstanceId, string stepName, int attempt, string? outputJson)
    {
        EnsurePayloadWithinLimit("step output", outputJson);
        var now = Persistence.UtcNow();
        var prior = Persistence.GetStepByKey(workflowInstanceId, stepName, attempt);
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = prior?.StepKind ?? "normal",
            State = StepState.Succeeded,
            Attempt = attempt,
            MaxAttempts = prior?.MaxAttempts ?? 1,
            TimeoutMs = prior?.TimeoutMs,
            InputJson = prior?.InputJson,
            OutputJson = outputJson,
            IdempotencyKey = prior?.IdempotencyKey,
            StartedAtUtc = prior?.StartedAtUtc,
            FinishedAtUtc = now
        };
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, outputJson ?? "{}");
        Persistence.UpsertStepWithEvent(rec, "workflow_step_succeeded", payload);
    }

    public void JournalCompensationStart(string stepId, string workflowInstanceId, string stepName, int attempt, string? inputJson)
    {
        JournalStepStart(stepId, workflowInstanceId, stepName, "compensation", attempt, 1, null, inputJson, null, "workflow_compensation_step_started");
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, JsonSerializer.Serialize(new { step = stepName, attempt, stepKind = "compensation" }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), workflowInstanceId, "workflow_step_started", payload);
    }

    public void JournalCompensationSuccess(string stepId, string workflowInstanceId, string stepName, int attempt, string? outputJson)
    {
        EnsurePayloadWithinLimit("compensation output", outputJson);
        var now = Persistence.UtcNow();
        var prior = Persistence.GetStepByKey(workflowInstanceId, stepName, attempt);
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = "compensation",
            State = StepState.Compensated,
            Attempt = attempt,
            MaxAttempts = 1,
            TimeoutMs = prior?.TimeoutMs,
            InputJson = prior?.InputJson,
            OutputJson = outputJson,
            IdempotencyKey = prior?.IdempotencyKey,
            StartedAtUtc = prior?.StartedAtUtc,
            FinishedAtUtc = now
        };
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, outputJson ?? "{}");
        Persistence.UpsertStepWithEvent(rec, "workflow_compensation_step_succeeded", payload);
    }

    public void JournalCompensationFailure(string stepId, string workflowInstanceId, string stepName, int attempt, string? errorJson)
    {
        EnsurePayloadWithinLimit("compensation error", errorJson);
        var now = Persistence.UtcNow();
        var prior = Persistence.GetStepByKey(workflowInstanceId, stepName, attempt);
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = "compensation",
            State = StepState.CompensationFailed,
            Attempt = attempt,
            MaxAttempts = 1,
            TimeoutMs = prior?.TimeoutMs,
            InputJson = prior?.InputJson,
            ErrorJson = errorJson,
            IdempotencyKey = prior?.IdempotencyKey,
            StartedAtUtc = prior?.StartedAtUtc,
            FinishedAtUtc = now
        };
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, errorJson ?? "{}");
        Persistence.UpsertStepWithEvent(rec, "workflow_compensation_step_failed", payload);
        if (attempt >= rec.MaxAttempts)
            InsertDeadLetterForStep(rec, "compensation_failure", errorJson);
    }

    public void JournalStepFailure(string stepId, string workflowInstanceId, string stepName, int attempt, string? errorJson)
    {
        EnsurePayloadWithinLimit("step error", errorJson);
        var now = Persistence.UtcNow();
        var prior = Persistence.GetStepByKey(workflowInstanceId, stepName, attempt);
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = prior?.StepKind ?? "normal",
            State = StepState.Failed,
            Attempt = attempt,
            MaxAttempts = prior?.MaxAttempts ?? 1,
            TimeoutMs = prior?.TimeoutMs,
            InputJson = prior?.InputJson,
            ErrorJson = errorJson,
            IdempotencyKey = prior?.IdempotencyKey,
            StartedAtUtc = prior?.StartedAtUtc,
            FinishedAtUtc = now
        };
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, errorJson ?? "{}");
        Persistence.UpsertStepWithEvent(rec, "workflow_step_failed", payload);
        if (attempt >= rec.MaxAttempts)
            InsertDeadLetterForStep(rec, "step_failure", errorJson);
    }

    public void JournalStepTimeout(string stepId, string workflowInstanceId, string stepName, int attempt, string? errorJson)
    {
        EnsurePayloadWithinLimit("step timeout error", errorJson);
        var now = Persistence.UtcNow();
        var prior = Persistence.GetStepByKey(workflowInstanceId, stepName, attempt);
        var rec = new WorkflowStepRecord
        {
            Id = stepId,
            WorkflowInstanceId = workflowInstanceId,
            StepName = stepName,
            StepKind = prior?.StepKind ?? "normal",
            State = StepState.TimedOut,
            Attempt = attempt,
            MaxAttempts = prior?.MaxAttempts ?? 1,
            TimeoutMs = prior?.TimeoutMs,
            InputJson = prior?.InputJson,
            ErrorJson = errorJson,
            IdempotencyKey = prior?.IdempotencyKey,
            StartedAtUtc = prior?.StartedAtUtc,
            FinishedAtUtc = now
        };
        var payload = BuildEventPayload(workflowInstanceId, stepName, attempt, errorJson ?? "{}");
        Persistence.UpsertStepWithEvent(rec, "workflow_step_timed_out", payload);
        if (attempt >= rec.MaxAttempts)
            InsertDeadLetterForStep(rec, "step_timeout", errorJson);
    }

    public void JournalStepRetryScheduled(string workflowInstanceId, string stepName, int attempt, int nextAttempt, int delayMs, string reason)
    {
        var payload = BuildEventPayload(
            workflowInstanceId,
            stepName,
            attempt,
            JsonSerializer.Serialize(new { step = stepName, attempt, nextAttempt, delayMs, reason }));
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), workflowInstanceId, "workflow_step_retry_scheduled", payload);
    }

    public int ComputeRetryDelayMs(string workflowInstanceId, string stepName, int retryOrdinal, string? backoff, int? delayMs, int? maxDelayMs)
    {
        var baseDelay = delayMs.GetValueOrDefault(DefaultRetryDelayMs);
        if (baseDelay < 0) baseDelay = 0;
        var cap = maxDelayMs.GetValueOrDefault(DefaultRetryMaxDelayMs);
        if (cap < 0) cap = 0;

        var mode = (backoff ?? "fixed").ToLowerInvariant();
        long computed = mode switch
        {
            "linear" => (long)baseDelay * Math.Max(1, retryOrdinal),
            "exponential" => (long)baseDelay * (1L << Math.Min(30, Math.Max(0, retryOrdinal - 1))),
            _ => baseDelay
        };
        if (mode is "linear" or "exponential")
            computed = Math.Min(computed, cap);

        var jittered = (int)Math.Clamp(computed, 0, int.MaxValue);
        if (EnableRetryJitter && jittered > 0)
        {
            var factor = GetDeterministicJitterFactor(workflowInstanceId, stepName, retryOrdinal);
            jittered = Math.Max(0, (int)Math.Round(jittered * factor));
        }
        return jittered;
    }

    public IReadOnlyDictionary<string, int> GetMinimumMetricSnapshot()
    {
        return new Dictionary<string, int>
        {
            ["workflow_instances_started_total"] = Persistence.CountEventsByType("workflow_started"),
            ["workflow_instances_completed_total"] = Persistence.CountEventsByType("workflow_completed"),
            ["workflow_instances_failed_total"] = Persistence.CountEventsByType("workflow_failed"),
            ["workflow_step_retries_total"] = Persistence.CountEventsByType("workflow_step_retry_scheduled"),
            ["workflow_approval_wait_seconds"] = Persistence.GetApprovalWaitSeconds(),
            ["workflow_step_duration_ms"] = Persistence.GetAverageStepDurationMs(),
            ["workflow_resume_count_total"] = Persistence.CountEventsByTypes("workflow_resumed", "workflow_recovered_after_restart"),
            ["workflow_step_timeouts_total"] = Persistence.CountEventsByType("workflow_step_timed_out"),
            ["workflow_compensation_started_total"] = Persistence.CountEventsByType("workflow_compensation_started")
        };
    }

    public int RecoverStaleRunningState(int staleRunningLeaseMs = DefaultStaleRunningLeaseMs)
    {
        var now = DateTime.UtcNow;
        var runningSteps = Persistence.GetRunningSteps();
        var recoveredWorkflowIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in runningSteps)
        {
            if (!IsStepStale(step, now, staleRunningLeaseMs))
                continue;

            var errorJson = $"{{\"type\":\"StepTimeoutError\",\"message\":\"Recovered stale RUNNING step after restart\",\"step\":\"{EscapeJson(step.StepName)}\",\"attempt\":{step.Attempt}}}";
            Persistence.UpdateStepTerminalState(step.Id, StepState.TimedOut, errorJson, Persistence.UtcNow());
            var payload = BuildEventPayload(step.WorkflowInstanceId, step.StepName, step.Attempt, errorJson);
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), step.WorkflowInstanceId, "workflow_step_timed_out", payload);
            recoveredWorkflowIds.Add(step.WorkflowInstanceId);
        }

        // Any RUNNING workflow is considered resumed by startup reconciliation.
        var runningWorkflows = Persistence.ListInstances(status: WorkflowStatus.Running, limit: int.MaxValue);
        foreach (var wf in runningWorkflows)
            recoveredWorkflowIds.Add(wf.Id);

        foreach (var id in recoveredWorkflowIds)
        {
            var payload = BuildEventPayload(id, null, null, JsonSerializer.Serialize(new { reason = "startup_recovery" }));
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), id, "workflow_recovered_after_restart", payload);
        }

        return recoveredWorkflowIds.Count;
    }

    private static bool IsStepStale(WorkflowStepRecord step, DateTime nowUtc, int staleRunningLeaseMs)
    {
        if (staleRunningLeaseMs <= 0)
            return true;
        if (string.IsNullOrWhiteSpace(step.StartedAtUtc))
            return true;
        if (!DateTime.TryParse(step.StartedAtUtc, out var started))
            return true;
        return (nowUtc - started).TotalMilliseconds >= staleRunningLeaseMs;
    }

    private string BuildEventPayload(string workflowInstanceId, string? stepName, int? attempt, string? detailsJson)
    {
        object details;
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            details = new Dictionary<string, object?>();
        }
        else
        {
            try
            {
                details = JsonSerializer.Deserialize<object>(detailsJson!) ?? new Dictionary<string, object?>();
            }
            catch
            {
                details = detailsJson!;
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["correlationId"] = Persistence.GetInstance(workflowInstanceId)?.CorrelationId,
            ["workflowInstanceId"] = workflowInstanceId,
            ["details"] = details
        };
        if (!string.IsNullOrWhiteSpace(stepName))
            payload["stepName"] = stepName;
        if (attempt.HasValue)
            payload["attempt"] = attempt.Value;

        var json = JsonSerializer.Serialize(payload);
        EnsurePayloadWithinLimit("event payload", json);
        return json;
    }

    private void EnsureStepAttemptWithinLimit(string stepName, int maxAttempts)
    {
        var options = GetRuntimeOptions();
        if (maxAttempts > options.MaxRetriesPerStep + 1)
        {
            throw new InvalidOperationException(
                $"Workflow step '{stepName}' exceeds retry limit: requested {maxAttempts - 1} retries, allowed maximum is {options.MaxRetriesPerStep}.");
        }
    }

    private void EnsurePayloadWithinLimit(string payloadLabel, string? payloadJson)
    {
        if (payloadJson == null)
            return;

        var options = GetRuntimeOptions();
        var size = Encoding.UTF8.GetByteCount(payloadJson);
        if (size > options.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Workflow {payloadLabel} exceeds configured payload limit ({options.MaxPayloadBytes} bytes). Actual: {size} bytes.");
        }
    }

    private void EnsureWorkflowRuntimeWithinLimit(string instanceId, string? stepName = null)
    {
        var options = GetRuntimeOptions();
        var instance = Persistence.GetInstance(instanceId);
        if (instance == null)
            return;

        if (!DateTime.TryParse(instance.StartedAtUtc, out var started))
            return;

        var elapsedMs = (DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds;
        if (elapsedMs > options.MaxWorkflowDurationMs)
        {
            var suffix = string.IsNullOrWhiteSpace(stepName) ? string.Empty : $" before step '{stepName}'";
            throw new InvalidOperationException(
                $"Workflow instance '{instanceId}' exceeded max runtime ({options.MaxWorkflowDurationMs}ms){suffix}.");
        }
    }

    private static string EscapeJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public bool EnterApprovalWait(string instanceId, string approvalStepName, string approvalName, int? timeoutMs, string payloadJson, out string? error)
    {
        EnsurePayloadWithinLimit("approval payload", payloadJson);
        return EnterWait(instanceId, approvalStepName, "approval", WorkflowStatus.WaitingApproval, "workflow_waiting_approval",
            $"{{\"approvalName\":\"{EscapeJson(approvalName)}\",\"payload\":{payloadJson},\"timeoutMs\":{(timeoutMs.HasValue ? timeoutMs.Value.ToString() : "null")}}}",
            timeoutMs,
            out error);
    }

    public bool EnterSignalWait(string instanceId, string signalStepName, string signalName, int? timeoutMs, string payloadJson, out string? error)
    {
        EnsurePayloadWithinLimit("signal payload", payloadJson);
        return EnterWait(instanceId, signalStepName, "signal_wait", WorkflowStatus.WaitingSignal, "workflow_waiting_signal",
            $"{{\"signalName\":\"{EscapeJson(signalName)}\",\"correlation\":{payloadJson},\"timeoutMs\":{(timeoutMs.HasValue ? timeoutMs.Value.ToString() : "null")}}}",
            timeoutMs,
            out error);
    }

    public bool ResolveApproval(string instanceId, string approvalStepName, string decision, string? payloadJson, out string? error)
    {
        error = null;
        var normalizedDecision = (decision ?? "approve").Trim().ToLowerInvariant();
        if (normalizedDecision is not ("approve" or "reject" or "timeout"))
        {
            error = "Decision must be one of: approve, reject, timeout.";
            return false;
        }

        var inst = Persistence.GetInstance(instanceId);
        if (inst == null)
        {
            error = $"Workflow instance not found: {instanceId}";
            return false;
        }

        if (inst.Status != WorkflowStatus.WaitingApproval)
        {
            error = $"WF1006: Illegal transition - instance is {inst.Status}, expected {WorkflowStatus.WaitingApproval}.";
            return false;
        }

        var step = Persistence.GetLatestStepByKind(instanceId, approvalStepName, "approval");
        if (step == null || step.State != StepState.Running)
        {
            error = $"Approval step '{approvalStepName}' is not in waiting state.";
            return false;
        }

        var resolutionPayloadJson = BuildResolutionPayload(normalizedDecision, payloadJson);
        if (normalizedDecision == "timeout")
        {
            Persistence.UpdateStepTerminalState(step.Id, StepState.TimedOut, resolutionPayloadJson, Persistence.UtcNow());
            var resolvedPayload = BuildEventPayload(instanceId, approvalStepName, step.Attempt, resolutionPayloadJson);
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_approval_resolved", resolvedPayload);
            Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Failed, errorJson: resolutionPayloadJson, finishedAt: Persistence.UtcNow());
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_failed", BuildEventPayload(instanceId, approvalStepName, step.Attempt, resolutionPayloadJson));
            InsertDeadLetterForStep(new WorkflowStepRecord
            {
                Id = step.Id,
                WorkflowInstanceId = step.WorkflowInstanceId,
                StepName = step.StepName,
                StepKind = step.StepKind,
                State = StepState.TimedOut,
                Attempt = step.Attempt,
                MaxAttempts = step.MaxAttempts,
                TimeoutMs = step.TimeoutMs,
                InputJson = step.InputJson,
                ErrorJson = resolutionPayloadJson,
                IdempotencyKey = step.IdempotencyKey,
                StartedAtUtc = step.StartedAtUtc,
                FinishedAtUtc = Persistence.UtcNow()
            }, "approval_timeout", resolutionPayloadJson);
            return true;
        }

        Persistence.UpdateStepTerminalState(step.Id, StepState.Succeeded, null, Persistence.UtcNow());
        var rec = new WorkflowStepRecord
        {
            Id = step.Id,
            WorkflowInstanceId = step.WorkflowInstanceId,
            StepName = step.StepName,
            StepKind = step.StepKind,
            State = StepState.Succeeded,
            Attempt = step.Attempt,
            MaxAttempts = step.MaxAttempts,
            TimeoutMs = step.TimeoutMs,
            InputJson = step.InputJson,
            OutputJson = resolutionPayloadJson,
            ErrorJson = null,
            IdempotencyKey = step.IdempotencyKey,
            StartedAtUtc = step.StartedAtUtc,
            FinishedAtUtc = Persistence.UtcNow()
        };
        Persistence.UpsertStepWithEvent(rec, "workflow_approval_resolved", BuildEventPayload(instanceId, approvalStepName, step.Attempt, resolutionPayloadJson));
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Running, errorJson: null, finishedAt: null);
        return true;
    }

    public bool DeliverSignal(string instanceId, string signalName, string? payloadJson, out string? error)
    {
        error = null;
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null)
        {
            error = $"Workflow instance not found: {instanceId}";
            return false;
        }

        if (inst.Status != WorkflowStatus.WaitingSignal)
        {
            error = $"WF1006: Illegal transition - instance is {inst.Status}, expected {WorkflowStatus.WaitingSignal}.";
            return false;
        }

        var step = Persistence.GetRunningSignalWaitBySignalName(instanceId, signalName);
        if (step == null)
        {
            error = $"No waiting signal node correlated for signal '{signalName}'.";
            return false;
        }

        var signalPayload = payloadJson ?? "null";
        var outputJson = $"{{\"signalName\":\"{EscapeJson(signalName)}\",\"payload\":{signalPayload},\"receivedAtUtc\":\"{EscapeJson(Persistence.UtcNow())}\"}}";
        var rec = new WorkflowStepRecord
        {
            Id = step.Id,
            WorkflowInstanceId = step.WorkflowInstanceId,
            StepName = step.StepName,
            StepKind = step.StepKind,
            State = StepState.Succeeded,
            Attempt = step.Attempt,
            MaxAttempts = step.MaxAttempts,
            TimeoutMs = step.TimeoutMs,
            InputJson = step.InputJson,
            OutputJson = outputJson,
            ErrorJson = null,
            IdempotencyKey = step.IdempotencyKey,
            StartedAtUtc = step.StartedAtUtc,
            FinishedAtUtc = Persistence.UtcNow()
        };
        Persistence.UpsertStepWithEvent(rec, "workflow_signal_received", BuildEventPayload(instanceId, step.StepName, step.Attempt, outputJson));
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Running, errorJson: null, finishedAt: null);
        return true;
    }

    public bool TimeoutWaitingStep(string instanceId, string stepName, string waitKind, string errorType, out string? error)
    {
        error = null;
        var step = Persistence.GetLatestStepByKind(instanceId, stepName, waitKind);
        if (step == null || step.State != StepState.Running)
        {
            error = $"Waiting step '{stepName}' is not active.";
            return false;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = errorType,
            step = stepName,
            attempt = step.Attempt,
            message = $"{waitKind} wait timed out"
        });
        Persistence.UpdateStepTerminalState(step.Id, StepState.TimedOut, payload, Persistence.UtcNow());
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Failed, errorJson: payload, finishedAt: Persistence.UtcNow());
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_failed", BuildEventPayload(instanceId, stepName, step.Attempt, payload));
        InsertDeadLetterForStep(new WorkflowStepRecord
        {
            Id = step.Id,
            WorkflowInstanceId = step.WorkflowInstanceId,
            StepName = step.StepName,
            StepKind = step.StepKind,
            State = StepState.TimedOut,
            Attempt = step.Attempt,
            MaxAttempts = step.MaxAttempts,
            TimeoutMs = step.TimeoutMs,
            InputJson = step.InputJson,
            ErrorJson = payload,
            IdempotencyKey = step.IdempotencyKey,
            StartedAtUtc = step.StartedAtUtc,
            FinishedAtUtc = Persistence.UtcNow()
        }, $"{waitKind}_timeout", payload);
        return true;
    }

    public void BeginCompensation(string instanceId, string? errorJson)
    {
        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Compensating, errorJson: errorJson, finishedAt: null);
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_compensation_started", BuildEventPayload(instanceId, null, null, errorJson ?? "{}"));
    }

    public void FinishCompensation(string instanceId, bool allSucceeded, string diagnosticsJson)
    {
        var now = Persistence.UtcNow();
        if (allSucceeded)
        {
            Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Compensated, errorJson: diagnosticsJson, finishedAt: now);
            Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_compensated", BuildEventPayload(instanceId, null, null, diagnosticsJson));
            return;
        }

        Persistence.UpdateInstanceStatus(instanceId, WorkflowStatus.Failed, errorJson: diagnosticsJson, finishedAt: now);
        Persistence.InsertEvent(Guid.NewGuid().ToString("N"), instanceId, "workflow_failed", BuildEventPayload(instanceId, null, null, diagnosticsJson));
    }

    private bool EnterWait(string instanceId, string stepName, string stepKind, string targetStatus, string waitingEventType, string inputJson, int? timeoutMs, out string? error)
    {
        EnsureWorkflowRuntimeWithinLimit(instanceId, stepName);
        error = null;
        var inst = Persistence.GetInstance(instanceId);
        if (inst == null)
        {
            error = $"Workflow instance not found: {instanceId}";
            return false;
        }

        if (inst.Status != WorkflowStatus.Running && inst.Status != targetStatus)
        {
            error = $"WF1006: Illegal transition - instance is {inst.Status}, expected RUNNING.";
            return false;
        }

        var existing = Persistence.GetLatestStepByKind(instanceId, stepName, stepKind);
        if (existing != null && existing.State == StepState.Running)
        {
            if (inst.Status != targetStatus)
                Persistence.UpdateInstanceStatus(instanceId, targetStatus);
            return true;
        }

        var attempt = existing != null ? existing.Attempt + 1 : 1;
        var waitStepId = Guid.NewGuid().ToString("N");
        JournalStepStart(waitStepId, instanceId, stepName, stepKind, attempt, 1, timeoutMs, inputJson, null, waitingEventType);
        Persistence.UpdateInstanceStatus(instanceId, targetStatus);
        return true;
    }

    private string BuildResolutionPayload(string decision, string? payloadJson)
    {
        var payload = payloadJson ?? "null";
        var resolvedAt = EscapeJson(Persistence.UtcNow());
        return $"{{\"decision\":\"{EscapeJson(decision)}\",\"payload\":{payload},\"resolvedAtUtc\":\"{resolvedAt}\"}}";
    }

    private void InsertDeadLetterForStep(WorkflowStepRecord step, string reason, string? payloadJson)
    {
        EnsurePayloadWithinLimit("dead letter payload", payloadJson);
        Persistence.InsertDeadLetter(new WorkflowDeadLetterRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkflowInstanceId = step.WorkflowInstanceId,
            StepName = step.StepName,
            Reason = reason,
            PayloadJson = payloadJson,
            CreatedAtUtc = Persistence.UtcNow()
        });
    }

    private static double GetDeterministicJitterFactor(string workflowInstanceId, string stepName, int retryOrdinal)
    {
        var key = $"{workflowInstanceId}:{stepName}:{retryOrdinal}";
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in key)
            {
                hash ^= c;
                hash *= 16777619;
            }
            var normalized = hash / (double)uint.MaxValue; // 0..1
            return 0.8 + (0.4 * normalized); // +/- 20%
        }
    }
}

public sealed class WorkflowRuntimeOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxRetriesPerStep { get; set; } = 10;
    public int MaxPayloadBytes { get; set; } = 1024 * 1024;
    public int MaxWorkflowDurationMs { get; set; } = 7 * 24 * 60 * 60 * 1000;
    public int OperationalRetentionDays { get; set; } = 30;
    public int AuditRetentionDays { get; set; } = 180;
    public int CompactionRetentionDays { get; set; } = 14;
    public int CleanupBatchSize { get; set; } = 500;

    public WorkflowRuntimeOptions Clone()
    {
        return new WorkflowRuntimeOptions
        {
            Enabled = Enabled,
            MaxRetriesPerStep = MaxRetriesPerStep,
            MaxPayloadBytes = MaxPayloadBytes,
            MaxWorkflowDurationMs = MaxWorkflowDurationMs,
            OperationalRetentionDays = OperationalRetentionDays,
            AuditRetentionDays = AuditRetentionDays,
            CompactionRetentionDays = CompactionRetentionDays,
            CleanupBatchSize = CleanupBatchSize
        };
    }

    public void ValidateOrThrow()
    {
        if (MaxRetriesPerStep < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetriesPerStep), "maxRetriesPerStep must be >= 0.");
        if (MaxPayloadBytes < 128)
            throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes), "maxPayloadBytes must be >= 128.");
        if (MaxWorkflowDurationMs < 1000)
            throw new ArgumentOutOfRangeException(nameof(MaxWorkflowDurationMs), "maxWorkflowDurationMs must be >= 1000.");
        new WorkflowMaintenanceOptions
        {
            OperationalRetentionDays = OperationalRetentionDays,
            AuditRetentionDays = AuditRetentionDays,
            CompactionRetentionDays = CompactionRetentionDays,
            CleanupBatchSize = CleanupBatchSize
        }.ValidateOrThrow();
    }

    public static WorkflowRuntimeOptions FromEnvironment()
    {
        var options = new WorkflowRuntimeOptions();

        var enabledRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOWS_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
            options.Enabled = enabled;

        var maxRetriesRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_MAX_RETRIES_PER_STEP");
        if (!string.IsNullOrWhiteSpace(maxRetriesRaw) && int.TryParse(maxRetriesRaw, out var maxRetries))
            options.MaxRetriesPerStep = maxRetries;

        var maxPayloadRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_MAX_PAYLOAD_BYTES");
        if (!string.IsNullOrWhiteSpace(maxPayloadRaw) && int.TryParse(maxPayloadRaw, out var maxPayload))
            options.MaxPayloadBytes = maxPayload;

        var maxRuntimeRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_MAX_DURATION_MS");
        if (!string.IsNullOrWhiteSpace(maxRuntimeRaw) && int.TryParse(maxRuntimeRaw, out var maxRuntime))
            options.MaxWorkflowDurationMs = maxRuntime;

        var operationalRetentionRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_RETENTION_OPERATIONAL_DAYS");
        if (!string.IsNullOrWhiteSpace(operationalRetentionRaw) && int.TryParse(operationalRetentionRaw, out var operationalRetentionDays))
            options.OperationalRetentionDays = operationalRetentionDays;

        var auditRetentionRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_RETENTION_AUDIT_DAYS");
        if (!string.IsNullOrWhiteSpace(auditRetentionRaw) && int.TryParse(auditRetentionRaw, out var auditRetentionDays))
            options.AuditRetentionDays = auditRetentionDays;

        var compactionRetentionRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_RETENTION_COMPACTION_DAYS");
        if (!string.IsNullOrWhiteSpace(compactionRetentionRaw) && int.TryParse(compactionRetentionRaw, out var compactionRetentionDays))
            options.CompactionRetentionDays = compactionRetentionDays;

        var cleanupBatchRaw = System.Environment.GetEnvironmentVariable("MALDA_WORKFLOW_CLEANUP_BATCH_SIZE");
        if (!string.IsNullOrWhiteSpace(cleanupBatchRaw) && int.TryParse(cleanupBatchRaw, out var cleanupBatchSize))
            options.CleanupBatchSize = cleanupBatchSize;

        options.ValidateOrThrow();
        return options;
    }
}
