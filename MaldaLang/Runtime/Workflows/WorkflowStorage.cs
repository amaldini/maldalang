// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Workflows;

using System;
using System.Collections.Generic;

public interface IWorkflowStorageProvider : IDisposable
{
    string UtcNow();
    void CreateInstance(WorkflowInstanceRecord rec);
    void UpdateInstanceStatus(string id, string status, string? resultJson = null, string? errorJson = null, string? startedAt = null, string? finishedAt = null);
    WorkflowInstanceRecord? GetInstance(string id);
    IReadOnlyList<WorkflowInstanceRecord> ListInstances(string? status = null, string? name = null, int limit = 100);

    void UpsertStep(WorkflowStepRecord rec);
    void UpsertStepWithEvent(WorkflowStepRecord rec, string eventType, string payloadJson);
    WorkflowStepRecord? GetStepByKey(string workflowInstanceId, string stepName, int attempt);
    WorkflowStepRecord? GetLatestSuccessfulStep(string workflowInstanceId, string stepName);
    WorkflowStepRecord? GetLatestStep(string workflowInstanceId, string stepName);
    WorkflowStepRecord? GetLatestStepByKind(string workflowInstanceId, string stepName, string stepKind);
    IReadOnlyList<WorkflowStepRecord> GetRunningStepsByKind(string workflowInstanceId, string stepKind);
    WorkflowStepRecord? GetRunningSignalWaitBySignalName(string workflowInstanceId, string signalName);
    IReadOnlyList<WorkflowStepRecord> GetRunningSteps();
    void UpdateStepTerminalState(string stepId, string state, string? errorJson, string? finishedAtUtc);
    IReadOnlyList<WorkflowStepRecord> GetSteps(string workflowInstanceId);

    void InsertEvent(string id, string workflowInstanceId, string eventType, string payloadJson);
    IReadOnlyList<WorkflowEventRecord> GetEvents(string workflowInstanceId, int limit = 200);

    void InsertDeadLetter(WorkflowDeadLetterRecord rec);
    WorkflowDeadLetterRecord? GetDeadLetter(string deadLetterId);
    IReadOnlyList<WorkflowDeadLetterRecord> ListDeadLetters(int limit = 100, bool includeRequeued = true);
    bool MarkDeadLetterRequeued(WorkflowDeadLetterRequeueRequest request);

    int CountEventsByType(string eventType);
    int CountEventsByTypes(params string[] eventTypes);
    int GetAverageStepDurationMs();
    int GetApprovalWaitSeconds();

    WorkflowMaintenanceReport RunMaintenance(WorkflowMaintenanceOptions options);
}

public sealed class WorkflowDeadLetterRequeueRequest
{
    public string DeadLetterId { get; set; } = "";
    public string RequeuedAtUtc { get; set; } = "";
    public string? RequeueReason { get; set; }
    public string? RequeueRequestedBy { get; set; }
    public string? RequeueCorrelationId { get; set; }
}

public sealed class WorkflowMaintenanceOptions
{
    public int OperationalRetentionDays { get; set; } = 30;
    public int AuditRetentionDays { get; set; } = 180;
    public int CompactionRetentionDays { get; set; } = 14;
    public int CleanupBatchSize { get; set; } = 500;
    public bool DryRun { get; set; }

    public WorkflowMaintenanceOptions Clone()
    {
        return new WorkflowMaintenanceOptions
        {
            OperationalRetentionDays = OperationalRetentionDays,
            AuditRetentionDays = AuditRetentionDays,
            CompactionRetentionDays = CompactionRetentionDays,
            CleanupBatchSize = CleanupBatchSize,
            DryRun = DryRun
        };
    }

    public void ValidateOrThrow()
    {
        if (OperationalRetentionDays < 1 || OperationalRetentionDays > 3650)
            throw new ArgumentOutOfRangeException(nameof(OperationalRetentionDays), "operationalRetentionDays must be between 1 and 3650.");
        if (AuditRetentionDays < OperationalRetentionDays || AuditRetentionDays > 3650)
            throw new ArgumentOutOfRangeException(nameof(AuditRetentionDays), "auditRetentionDays must be >= operationalRetentionDays and <= 3650.");
        if (CompactionRetentionDays < 1 || CompactionRetentionDays > OperationalRetentionDays)
            throw new ArgumentOutOfRangeException(nameof(CompactionRetentionDays), "compactionRetentionDays must be between 1 and operationalRetentionDays.");
        if (CleanupBatchSize < 1 || CleanupBatchSize > 10000)
            throw new ArgumentOutOfRangeException(nameof(CleanupBatchSize), "cleanupBatchSize must be between 1 and 10000.");
    }
}

public sealed class WorkflowMaintenanceReport
{
    public string MaintenanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string StartedAtUtc { get; set; } = "";
    public string FinishedAtUtc { get; set; } = "";
    public int ArchivedInstances { get; set; }
    public int DeletedSteps { get; set; }
    public int DeletedEvents { get; set; }
    public int DeletedDeadLetters { get; set; }
    public int CompactedSteps { get; set; }
    public bool DryRun { get; set; }
}
