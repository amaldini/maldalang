// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Runtime.Workflows;
using Xunit;

namespace MaldaLang.Tests;

[Collection("WorkflowEngineSerial")]
public class WorkflowSprint7ReliabilityTests
{
    private static string GetTestDbPath() =>
        Path.Combine(Path.GetTempPath(), $"workflow_sprint7_{Guid.NewGuid():N}.db");

    [Fact]
    public void Workflow_TerminalRetryFailure_PopulatesDeadLetter_AndRequeueIsAudited()
    {
        var dbPath = GetTestDbPath();
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);

        var source = @"
function alwaysFail() { error(""storage unavailable""); }
workflow DlqFlow(input) {
    step task = alwaysFail() retry 1 backoff ""fixed"" delay 1;
    return task;
}
startWorkflow(""DlqFlow"", null);
";
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        var interpreter = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interpreter.InterpretAsync(statements).GetAwaiter().GetResult());

        var engine = WorkflowEngine.Instance;
        var deadLetters = engine.ListDeadLetters(20, includeRequeued: false);
        Assert.NotEmpty(deadLetters);
        var deadLetter = deadLetters.First(dl => dl.StepName == "task");

        var ok = engine.RequeueDeadLetter(deadLetter.Id, "operator replay", "qa", "corr-requeue-1", out var requeueError);
        Assert.True(ok, requeueError ?? "requeue should succeed");

        var second = engine.RequeueDeadLetter(deadLetter.Id, "duplicate", "qa", "corr-requeue-2", out _);
        Assert.False(second);

        var updated = engine.ListDeadLetters(20, includeRequeued: true).First(dl => dl.Id == deadLetter.Id);
        Assert.NotNull(updated.RequeuedAtUtc);
        Assert.Equal("operator replay", updated.RequeueReason);
        Assert.Equal("qa", updated.RequeueRequestedBy);
        Assert.Equal("corr-requeue-1", updated.RequeueCorrelationId);
        Assert.True(updated.RequeueAttempts >= 1);
    }

    [Fact]
    public void Workflow_RetentionMaintenance_ArchivesTerminalRows_WithoutTouchingActiveInstances()
    {
        var dbPath = GetTestDbPath();
        var connection = "Data Source=" + dbPath;
        var now = DateTime.UtcNow;
        var oldCreated = now.AddDays(-90).ToString("O");
        var oldFinished = now.AddDays(-60).ToString("O");

        using (var persistence = new WorkflowPersistence(connection))
        {
            persistence.CreateInstance(new WorkflowInstanceRecord
            {
                Id = "wf_old_terminal",
                Name = "OldFlow",
                InputJson = "{}",
                Status = WorkflowStatus.Completed,
                CreatedAtUtc = oldCreated,
                UpdatedAtUtc = oldFinished,
                StartedAtUtc = oldCreated,
                FinishedAtUtc = oldFinished
            });
            persistence.UpsertStep(new WorkflowStepRecord
            {
                Id = "old_step_1",
                WorkflowInstanceId = "wf_old_terminal",
                StepName = "stepA",
                StepKind = "normal",
                State = StepState.Succeeded,
                Attempt = 1,
                MaxAttempts = 1,
                InputJson = "{\"a\":1}",
                OutputJson = "{\"ok\":true}",
                StartedAtUtc = oldCreated,
                FinishedAtUtc = oldFinished
            });
            persistence.InsertEvent("old_event_1", "wf_old_terminal", "workflow_completed", "{\"ok\":true}");
            persistence.InsertDeadLetter(new WorkflowDeadLetterRecord
            {
                Id = "old_dlq_1",
                WorkflowInstanceId = "wf_old_terminal",
                StepName = "stepA",
                Reason = "historical",
                PayloadJson = "{\"legacy\":true}",
                CreatedAtUtc = oldFinished
            });

            persistence.CreateInstance(new WorkflowInstanceRecord
            {
                Id = "wf_active",
                Name = "ActiveFlow",
                InputJson = "{}",
                Status = WorkflowStatus.Running,
                CreatedAtUtc = now.ToString("O"),
                UpdatedAtUtc = now.ToString("O"),
                StartedAtUtc = now.ToString("O")
            });
        }

        WorkflowEngine.ResetForTesting(connection);
        var report = WorkflowEngine.Instance.RunMaintenanceJob(new WorkflowMaintenanceOptions
        {
            OperationalRetentionDays = 30,
            AuditRetentionDays = 30,
            CompactionRetentionDays = 14,
            CleanupBatchSize = 200
        });

        Assert.True(report.ArchivedInstances >= 1);
        Assert.Null(WorkflowEngine.Instance.GetInstance("wf_old_terminal"));
        Assert.NotNull(WorkflowEngine.Instance.GetInstance("wf_active"));
        Assert.Equal(WorkflowStatus.Running, WorkflowEngine.Instance.GetInstance("wf_active")!.Status);
    }

    [Fact]
    public void Workflow_RandomizedRestartRecovery_BoundedAndDeterministic()
    {
        var dbPath = GetTestDbPath();
        var connection = "Data Source=" + dbPath;
        WorkflowEngine.ResetForTesting(connection);
        var engine = WorkflowEngine.Instance;

        var random = new Random(1337);
        var staleIds = new System.Collections.Generic.List<string>();
        for (var i = 0; i < 16; i++)
        {
            var instanceId = engine.CreateInstance("RestartFlow", $"{{\"i\":{i}}}");
            Assert.True(engine.StartInstance(instanceId));
            if (random.NextDouble() < 0.5)
            {
                var stepId = Guid.NewGuid().ToString("N");
                engine.JournalStepStart(stepId, instanceId, "work", 1, 2, 1000, "{}", null);
                staleIds.Add(instanceId);
            }
            else
            {
                engine.CompleteInstance(instanceId, "{\"ok\":true}");
            }
        }

        WorkflowEngine.ResetForTesting(connection);
        var recovered = WorkflowEngine.Instance.RecoverStaleRunningState(0);
        Assert.True(recovered >= staleIds.Count);

        foreach (var instanceId in staleIds)
        {
            var steps = WorkflowEngine.Instance.GetSteps(instanceId);
            Assert.Contains(steps, s => s.StepName == "work" && s.State == StepState.TimedOut);
        }
    }
}
