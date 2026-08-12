// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Workflows;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

/// <summary>
/// SQLite persistence for workflow instances, steps, events, and dead letters.
/// Implements schema from workflowProposal.md section 10.
/// </summary>
public sealed class WorkflowPersistence : IWorkflowStorageProvider
{
    private static readonly object SqliteInitLock = new();
    private static bool _sqliteProviderInitialized;
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;
    private bool _disposed;

    public const string DefaultConnectionString = "Data Source=./.malda/workflows.db";
    internal const int SqliteBusyTimeoutMs = 5000;

    public WorkflowPersistence(string? connectionString = null)
    {
        _connectionString = connectionString ?? DefaultConnectionString;
        EnsureSqliteProviderInitialized();
        EnsureWorkflowDbDirectory();
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        ApplyConnectionPragmas();
        EnsureSchema();
    }

    /// <summary>
    /// WAL lets a second process run read-only ops against the same DB while a writer
    /// is active; busy_timeout softens brief lock contention. See docs/workflows-ha.md.
    /// </summary>
    private void ApplyConnectionPragmas()
    {
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();
        pragma.CommandText = $"PRAGMA busy_timeout={SqliteBusyTimeoutMs};";
        pragma.ExecuteNonQuery();
    }

    /// <summary>Test hook: journal_mode is DB-persisted; busy_timeout is per-connection.</summary>
    internal (string JournalMode, long BusyTimeoutMs) ReadSqlitePragmasForTests()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var journalMode = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        cmd.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt64(cmd.ExecuteScalar());
        return (journalMode, busyTimeout);
    }

    private static void EnsureSqliteProviderInitialized()
    {
        if (_sqliteProviderInitialized)
            return;

        lock (SqliteInitLock)
        {
            if (_sqliteProviderInitialized)
                return;

            SQLitePCL.Batteries_V2.Init();
            _sqliteProviderInitialized = true;
        }
    }

    private static void EnsureWorkflowDbDirectory()
    {
        try
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), ".malda");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch
        {
            // Best effort; SQLite may still work with absolute paths
        }
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS workflow_instances (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                input_json TEXT,
                status TEXT NOT NULL,
                result_json TEXT,
                error_json TEXT,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                started_at_utc TEXT,
                finished_at_utc TEXT,
                runtime_version TEXT,
                correlation_id TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_workflow_instances_status_created 
                ON workflow_instances(status, created_at_utc);

            CREATE TABLE IF NOT EXISTS workflow_steps (
                id TEXT PRIMARY KEY,
                workflow_instance_id TEXT NOT NULL,
                step_name TEXT NOT NULL,
                step_kind TEXT NOT NULL,
                state TEXT NOT NULL,
                attempt INTEGER NOT NULL,
                max_attempts INTEGER NOT NULL,
                timeout_ms INTEGER,
                input_json TEXT,
                output_json TEXT,
                error_json TEXT,
                idempotency_key TEXT,
                started_at_utc TEXT,
                finished_at_utc TEXT,
                FOREIGN KEY (workflow_instance_id) REFERENCES workflow_instances(id)
            );
            CREATE INDEX IF NOT EXISTS idx_workflow_steps_instance_state 
                ON workflow_steps(workflow_instance_id, state);

            CREATE TABLE IF NOT EXISTS workflow_events (
                id TEXT PRIMARY KEY,
                workflow_instance_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (workflow_instance_id) REFERENCES workflow_instances(id)
            );
            CREATE INDEX IF NOT EXISTS idx_workflow_events_instance_created 
                ON workflow_events(workflow_instance_id, created_at_utc);

            CREATE TABLE IF NOT EXISTS workflow_dead_letters (
                id TEXT PRIMARY KEY,
                workflow_instance_id TEXT NOT NULL,
                step_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                payload_json TEXT,
                created_at_utc TEXT NOT NULL,
                requeued_at_utc TEXT,
                FOREIGN KEY (workflow_instance_id) REFERENCES workflow_instances(id)
            );
            CREATE INDEX IF NOT EXISTS idx_workflow_dead_letters_created 
                ON workflow_dead_letters(created_at_utc);

            CREATE TABLE IF NOT EXISTS workflow_instance_archive (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                status TEXT NOT NULL,
                result_json TEXT,
                error_json TEXT,
                created_at_utc TEXT NOT NULL,
                finished_at_utc TEXT,
                archived_at_utc TEXT NOT NULL,
                correlation_id TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_workflow_instance_archive_archived
                ON workflow_instance_archive(archived_at_utc);
        ";
        cmd.ExecuteNonQuery();

        EnsureColumnExists("workflow_dead_letters", "requeue_reason", "TEXT");
        EnsureColumnExists("workflow_dead_letters", "requeue_requested_by", "TEXT");
        EnsureColumnExists("workflow_dead_letters", "requeue_correlation_id", "TEXT");
        EnsureColumnExists("workflow_dead_letters", "requeue_attempts", "INTEGER NOT NULL DEFAULT 0");
    }

    private void EnsureColumnExists(string tableName, string columnName, string columnTypeClause)
    {
        using var probe = _connection.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = probe.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnTypeClause}";
        alter.ExecuteNonQuery();
    }

    public string UtcNow() => DateTime.UtcNow.ToString("O");

    public void CreateInstance(WorkflowInstanceRecord rec)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workflow_instances (id, name, input_json, status, result_json, error_json,
                created_at_utc, updated_at_utc, started_at_utc, finished_at_utc, runtime_version, correlation_id)
            VALUES (@id, @name, @input_json, @status, @result_json, @error_json,
                @created_at_utc, @updated_at_utc, @started_at_utc, @finished_at_utc, @runtime_version, @correlation_id)";
        AddInstanceParams(cmd, rec);
        cmd.ExecuteNonQuery();
    }

    public void UpdateInstanceStatus(string id, string status, string? resultJson = null, string? errorJson = null,
        string? startedAt = null, string? finishedAt = null)
    {
        var now = UtcNow();
        var parts = new List<string> { "UPDATE workflow_instances SET status = @status, updated_at_utc = @now" };
        if (resultJson != null) parts.Add(", result_json = @result_json");
        if (errorJson != null) parts.Add(", error_json = @error_json");
        if (startedAt != null) parts.Add(", started_at_utc = @started_at");
        if (finishedAt != null) parts.Add(", finished_at_utc = @finished_at");
        parts.Add(" WHERE id = @id");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = string.Join("", parts);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@now", now);
        if (resultJson != null) cmd.Parameters.AddWithValue("@result_json", resultJson);
        if (errorJson != null) cmd.Parameters.AddWithValue("@error_json", errorJson);
        if (startedAt != null) cmd.Parameters.AddWithValue("@started_at", startedAt);
        if (finishedAt != null) cmd.Parameters.AddWithValue("@finished_at", finishedAt);
        cmd.ExecuteNonQuery();
    }

    public WorkflowInstanceRecord? GetInstance(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_instances WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadInstance(r);
    }

    public IReadOnlyList<WorkflowInstanceRecord> ListInstances(string? status = null, string? name = null, int limit = 100)
    {
        var list = new List<WorkflowInstanceRecord>();
        var parts = new List<string> { "SELECT * FROM workflow_instances WHERE 1=1" };
        if (!string.IsNullOrEmpty(status)) parts.Add(" AND status = @status");
        if (!string.IsNullOrEmpty(name)) parts.Add(" AND name = @name");
        parts.Add(" ORDER BY created_at_utc DESC LIMIT @limit");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = string.Join("", parts);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        if (!string.IsNullOrEmpty(name)) cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadInstance(r));
        return list;
    }

    public void UpsertStep(WorkflowStepRecord rec)
    {
        using var cmd = CreateUpsertStepCommand(rec, transaction: null);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Atomically persists terminal step state and its corresponding workflow event.
    /// </summary>
    public void UpsertStepWithEvent(WorkflowStepRecord rec, string eventType, string payloadJson)
    {
        var now = UtcNow();
        try
        {
            using var tx = _connection.BeginTransaction();

            using (var stepCmd = CreateUpsertStepCommand(rec, tx))
            {
                stepCmd.ExecuteNonQuery();
            }

            using (var eventCmd = _connection.CreateCommand())
            {
                eventCmd.Transaction = tx;
                eventCmd.CommandText = "INSERT INTO workflow_events (id, workflow_instance_id, event_type, payload_json, created_at_utc) VALUES (@id, @wi_id, @type, @payload, @now)";
                eventCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                eventCmd.Parameters.AddWithValue("@wi_id", rec.WorkflowInstanceId);
                eventCmd.Parameters.AddWithValue("@type", eventType);
                eventCmd.Parameters.AddWithValue("@payload", payloadJson);
                eventCmd.Parameters.AddWithValue("@now", now);
                eventCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("nested transactions", StringComparison.OrdinalIgnoreCase))
        {
            // If caller already owns a transaction on this connection, degrade to best-effort writes
            // rather than failing workflow execution.
        }

        using (var stepCmd = CreateUpsertStepCommand(rec, transaction: null))
        {
            stepCmd.ExecuteNonQuery();
        }
        using (var eventCmd = _connection.CreateCommand())
        {
            eventCmd.CommandText = "INSERT INTO workflow_events (id, workflow_instance_id, event_type, payload_json, created_at_utc) VALUES (@id, @wi_id, @type, @payload, @now)";
            eventCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            eventCmd.Parameters.AddWithValue("@wi_id", rec.WorkflowInstanceId);
            eventCmd.Parameters.AddWithValue("@type", eventType);
            eventCmd.Parameters.AddWithValue("@payload", payloadJson);
            eventCmd.Parameters.AddWithValue("@now", now);
            eventCmd.ExecuteNonQuery();
        }
    }

    private SqliteCommand CreateUpsertStepCommand(WorkflowStepRecord rec, SqliteTransaction? transaction)
    {
        var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO workflow_steps (id, workflow_instance_id, step_name, step_kind, state, attempt, max_attempts,
                timeout_ms, input_json, output_json, error_json, idempotency_key, started_at_utc, finished_at_utc)
            VALUES (@id, @wi_id, @step_name, @step_kind, @state, @attempt, @max_attempts,
                @timeout_ms, @input_json, @output_json, @error_json, @idempotency_key, @started_at, @finished_at)
            ON CONFLICT(id) DO UPDATE SET
                state = excluded.state, output_json = excluded.output_json, error_json = excluded.error_json,
                finished_at_utc = excluded.finished_at_utc";
        cmd.Parameters.AddWithValue("@id", rec.Id);
        cmd.Parameters.AddWithValue("@wi_id", rec.WorkflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", rec.StepName);
        cmd.Parameters.AddWithValue("@step_kind", rec.StepKind);
        cmd.Parameters.AddWithValue("@state", rec.State);
        cmd.Parameters.AddWithValue("@attempt", rec.Attempt);
        cmd.Parameters.AddWithValue("@max_attempts", rec.MaxAttempts);
        cmd.Parameters.AddWithValue("@timeout_ms", (object?)rec.TimeoutMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@input_json", rec.InputJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@output_json", rec.OutputJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@error_json", rec.ErrorJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@idempotency_key", rec.IdempotencyKey ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@started_at", rec.StartedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@finished_at", rec.FinishedAtUtc ?? (object)DBNull.Value);
        return cmd;
    }

    public WorkflowStepRecord? GetStepByKey(string workflowInstanceId, string stepName, int attempt)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id AND step_name = @step_name AND attempt = @attempt ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", stepName);
        cmd.Parameters.AddWithValue("@attempt", attempt);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadStep(r);
    }

    /// <summary>Returns the latest successful step record for replay (highest attempt with state SUCCEEDED).</summary>
    public WorkflowStepRecord? GetLatestSuccessfulStep(string workflowInstanceId, string stepName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id AND step_name = @step_name AND state = 'SUCCEEDED' ORDER BY attempt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", stepName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadStep(r);
    }

    public WorkflowStepRecord? GetLatestStep(string workflowInstanceId, string stepName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id AND step_name = @step_name ORDER BY attempt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", stepName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadStep(r);
    }

    public WorkflowStepRecord? GetLatestStepByKind(string workflowInstanceId, string stepName, string stepKind)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id AND step_name = @step_name AND step_kind = @step_kind ORDER BY attempt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", stepName);
        cmd.Parameters.AddWithValue("@step_kind", stepKind);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadStep(r);
    }

    public IReadOnlyList<WorkflowStepRecord> GetRunningStepsByKind(string workflowInstanceId, string stepKind)
    {
        var list = new List<WorkflowStepRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id AND step_kind = @step_kind AND state = 'RUNNING' ORDER BY attempt DESC";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@step_kind", stepKind);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadStep(r));
        return list;
    }

    public WorkflowStepRecord? GetRunningSignalWaitBySignalName(string workflowInstanceId, string signalName)
    {
        var runningSignalWaits = GetRunningStepsByKind(workflowInstanceId, "signal_wait");
        foreach (var step in runningSignalWaits)
        {
            if (string.IsNullOrWhiteSpace(step.InputJson))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(step.InputJson);
                if (doc.RootElement.TryGetProperty("signalName", out var signalNameProp) &&
                    string.Equals(signalNameProp.GetString(), signalName, StringComparison.Ordinal))
                {
                    return step;
                }
            }
            catch
            {
                // Keep scanning in case another row has valid payload JSON.
            }
        }

        return null;
    }

    public IReadOnlyList<WorkflowStepRecord> GetRunningSteps()
    {
        var list = new List<WorkflowStepRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE state = 'RUNNING'";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadStep(r));
        return list;
    }

    public void UpdateStepTerminalState(string stepId, string state, string? errorJson, string? finishedAtUtc)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE workflow_steps
            SET state = @state,
                error_json = @error_json,
                finished_at_utc = @finished_at
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", stepId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@error_json", errorJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@finished_at", finishedAtUtc ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkflowStepRecord> GetSteps(string workflowInstanceId)
    {
        var list = new List<WorkflowStepRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_steps WHERE workflow_instance_id = @wi_id ORDER BY step_name, attempt";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadStep(r));
        return list;
    }

    public void InsertEvent(string id, string workflowInstanceId, string eventType, string payloadJson)
    {
        var now = UtcNow();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO workflow_events (id, workflow_instance_id, event_type, payload_json, created_at_utc) VALUES (@id, @wi_id, @type, @payload, @now)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@type", eventType);
        cmd.Parameters.AddWithValue("@payload", payloadJson);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkflowEventRecord> GetEvents(string workflowInstanceId, int limit = 200)
    {
        var list = new List<WorkflowEventRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workflow_instance_id, event_type, payload_json, created_at_utc
            FROM workflow_events
            WHERE workflow_instance_id = @wi_id
            ORDER BY created_at_utc ASC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@wi_id", workflowInstanceId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WorkflowEventRecord
            {
                Id = r.GetString(0),
                WorkflowInstanceId = r.GetString(1),
                EventType = r.GetString(2),
                PayloadJson = r.GetString(3),
                CreatedAtUtc = r.GetString(4)
            });
        }
        return list;
    }

    public void InsertDeadLetter(WorkflowDeadLetterRecord rec)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workflow_dead_letters (
                id, workflow_instance_id, step_name, reason, payload_json, created_at_utc,
                requeued_at_utc, requeue_reason, requeue_requested_by, requeue_correlation_id, requeue_attempts)
            VALUES (
                @id, @workflow_instance_id, @step_name, @reason, @payload_json, @created_at_utc,
                @requeued_at_utc, @requeue_reason, @requeue_requested_by, @requeue_correlation_id, @requeue_attempts)";
        cmd.Parameters.AddWithValue("@id", rec.Id);
        cmd.Parameters.AddWithValue("@workflow_instance_id", rec.WorkflowInstanceId);
        cmd.Parameters.AddWithValue("@step_name", rec.StepName);
        cmd.Parameters.AddWithValue("@reason", rec.Reason);
        cmd.Parameters.AddWithValue("@payload_json", rec.PayloadJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at_utc", rec.CreatedAtUtc);
        cmd.Parameters.AddWithValue("@requeued_at_utc", rec.RequeuedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_reason", rec.RequeueReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_requested_by", rec.RequeueRequestedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_correlation_id", rec.RequeueCorrelationId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_attempts", rec.RequeueAttempts);
        cmd.ExecuteNonQuery();
    }

    public WorkflowDeadLetterRecord? GetDeadLetter(string deadLetterId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workflow_instance_id, step_name, reason, payload_json, created_at_utc, requeued_at_utc,
                   requeue_reason, requeue_requested_by, requeue_correlation_id, requeue_attempts
            FROM workflow_dead_letters
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", deadLetterId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return ReadDeadLetter(r);
    }

    public IReadOnlyList<WorkflowDeadLetterRecord> ListDeadLetters(int limit = 100, bool includeRequeued = true)
    {
        var list = new List<WorkflowDeadLetterRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workflow_instance_id, step_name, reason, payload_json, created_at_utc, requeued_at_utc,
                   requeue_reason, requeue_requested_by, requeue_correlation_id, requeue_attempts
            FROM workflow_dead_letters
            WHERE (@include_requeued = 1 OR requeued_at_utc IS NULL)
            ORDER BY created_at_utc DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@include_requeued", includeRequeued ? 1 : 0);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadDeadLetter(r));
        return list;
    }

    public bool RequeueDeadLetter(string deadLetterId)
    {
        return MarkDeadLetterRequeued(new WorkflowDeadLetterRequeueRequest
        {
            DeadLetterId = deadLetterId,
            RequeuedAtUtc = UtcNow()
        });
    }

    public bool MarkDeadLetterRequeued(WorkflowDeadLetterRequeueRequest request)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE workflow_dead_letters
            SET requeued_at_utc = @requeued,
                requeue_reason = @requeue_reason,
                requeue_requested_by = @requeue_requested_by,
                requeue_correlation_id = @requeue_correlation_id,
                requeue_attempts = COALESCE(requeue_attempts, 0) + 1
            WHERE id = @id AND requeued_at_utc IS NULL";
        cmd.Parameters.AddWithValue("@id", request.DeadLetterId);
        cmd.Parameters.AddWithValue("@requeued", request.RequeuedAtUtc);
        cmd.Parameters.AddWithValue("@requeue_reason", request.RequeueReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_requested_by", request.RequeueRequestedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@requeue_correlation_id", request.RequeueCorrelationId ?? (object)DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int CountEventsByType(string eventType)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM workflow_events WHERE event_type = @event_type";
        cmd.Parameters.AddWithValue("@event_type", eventType);
        var count = cmd.ExecuteScalar();
        return Convert.ToInt32(count);
    }

    public int CountEventsByTypes(params string[] eventTypes)
    {
        if (eventTypes == null || eventTypes.Length == 0)
            return 0;

        using var cmd = _connection.CreateCommand();
        var placeholders = new List<string>(eventTypes.Length);
        for (var i = 0; i < eventTypes.Length; i++)
        {
            var paramName = "@t" + i;
            placeholders.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, eventTypes[i]);
        }

        cmd.CommandText = $"SELECT COUNT(*) FROM workflow_events WHERE event_type IN ({string.Join(", ", placeholders)})";
        var count = cmd.ExecuteScalar();
        return Convert.ToInt32(count);
    }

    public int GetAverageStepDurationMs()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(AVG((julianday(finished_at_utc) - julianday(started_at_utc)) * 86400000.0), 0)
            FROM workflow_steps
            WHERE started_at_utc IS NOT NULL AND finished_at_utc IS NOT NULL";
        var value = cmd.ExecuteScalar();
        return Convert.ToInt32(Math.Round(Convert.ToDouble(value)));
    }

    public int GetApprovalWaitSeconds()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM((julianday(finished_at_utc) - julianday(started_at_utc)) * 86400.0), 0)
            FROM workflow_steps
            WHERE step_kind = 'approval' AND started_at_utc IS NOT NULL AND finished_at_utc IS NOT NULL";
        var value = cmd.ExecuteScalar();
        return Convert.ToInt32(Math.Round(Convert.ToDouble(value)));
    }

    public WorkflowMaintenanceReport RunMaintenance(WorkflowMaintenanceOptions options)
    {
        options.ValidateOrThrow();
        var report = new WorkflowMaintenanceReport
        {
            StartedAtUtc = UtcNow(),
            DryRun = options.DryRun
        };

        var now = DateTime.UtcNow;
        var operationalCutoff = now.AddDays(-options.OperationalRetentionDays).ToString("O");
        var auditCutoff = now.AddDays(-options.AuditRetentionDays).ToString("O");
        var compactionCutoff = now.AddDays(-options.CompactionRetentionDays).ToString("O");

        using var tx = _connection.BeginTransaction();
        try
        {
            report.CompactedSteps = CountAndMaybeCompactTerminalStepPayloads(compactionCutoff, options, tx);
            report.ArchivedInstances = CountAndMaybeArchiveTerminalInstances(operationalCutoff, options, tx, report);
            report.DeletedEvents += CountAndMaybeDeleteByCutoff("workflow_events", "id", "created_at_utc", auditCutoff, options, tx);
            report.DeletedDeadLetters += CountAndMaybeDeleteByCutoff("workflow_dead_letters", "id", "created_at_utc", auditCutoff, options, tx);

            if (!options.DryRun)
                tx.Commit();
            else
                tx.Rollback();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }

        report.FinishedAtUtc = UtcNow();
        return report;
    }

    private int CountAndMaybeCompactTerminalStepPayloads(string cutoffUtc, WorkflowMaintenanceOptions options, SqliteTransaction tx)
    {
        var candidateIds = SelectIds(@"
            SELECT s.id
            FROM workflow_steps s
            INNER JOIN workflow_instances i ON i.id = s.workflow_instance_id
            WHERE i.status IN ('COMPLETED', 'COMPENSATED')
              AND i.finished_at_utc IS NOT NULL
              AND i.finished_at_utc < @cutoff
              AND (s.input_json IS NOT NULL OR s.output_json IS NOT NULL OR s.error_json IS NOT NULL)
            ORDER BY i.finished_at_utc ASC
            LIMIT @limit", cutoffUtc, options.CleanupBatchSize, tx);
        if (candidateIds.Count == 0 || options.DryRun)
            return candidateIds.Count;

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE workflow_steps SET input_json = NULL, output_json = NULL, error_json = NULL WHERE id IN ({string.Join(",", candidateIds.Select((_, i) => "@id" + i))})";
        for (var i = 0; i < candidateIds.Count; i++)
            cmd.Parameters.AddWithValue("@id" + i, candidateIds[i]);
        return cmd.ExecuteNonQuery();
    }

    private int CountAndMaybeArchiveTerminalInstances(string cutoffUtc, WorkflowMaintenanceOptions options, SqliteTransaction tx, WorkflowMaintenanceReport report)
    {
        var ids = SelectIds(@"
            SELECT id
            FROM workflow_instances
            WHERE status IN ('COMPLETED', 'COMPENSATED')
              AND finished_at_utc IS NOT NULL
              AND finished_at_utc < @cutoff
            ORDER BY finished_at_utc ASC
            LIMIT @limit", cutoffUtc, options.CleanupBatchSize, tx);
        if (ids.Count == 0)
            return 0;
        if (options.DryRun)
            return ids.Count;

        foreach (var id in ids)
        {
            using (var archive = _connection.CreateCommand())
            {
                archive.Transaction = tx;
                archive.CommandText = @"
                    INSERT OR IGNORE INTO workflow_instance_archive
                        (id, name, status, result_json, error_json, created_at_utc, finished_at_utc, archived_at_utc, correlation_id)
                    SELECT id, name, status, result_json, error_json, created_at_utc, finished_at_utc, @archived_at, correlation_id
                    FROM workflow_instances
                    WHERE id = @id";
                archive.Parameters.AddWithValue("@id", id);
                archive.Parameters.AddWithValue("@archived_at", UtcNow());
                archive.ExecuteNonQuery();
            }

            report.DeletedSteps += ExecuteDeleteById("workflow_steps", "workflow_instance_id", id, tx);
            report.DeletedEvents += ExecuteDeleteById("workflow_events", "workflow_instance_id", id, tx);
            report.DeletedDeadLetters += ExecuteDeleteById("workflow_dead_letters", "workflow_instance_id", id, tx);
            ExecuteDeleteById("workflow_instances", "id", id, tx);
        }

        return ids.Count;
    }

    private int CountAndMaybeDeleteByCutoff(string table, string idColumn, string cutoffColumn, string cutoffUtc, WorkflowMaintenanceOptions options, SqliteTransaction tx)
    {
        var ids = SelectIds(
            $"SELECT {idColumn} FROM {table} WHERE {cutoffColumn} < @cutoff ORDER BY {cutoffColumn} ASC LIMIT @limit",
            cutoffUtc,
            options.CleanupBatchSize,
            tx);
        if (ids.Count == 0 || options.DryRun)
            return ids.Count;

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {table} WHERE {idColumn} IN ({string.Join(",", ids.Select((_, i) => "@id" + i))})";
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue("@id" + i, ids[i]);
        return cmd.ExecuteNonQuery();
    }

    private int ExecuteDeleteById(string table, string column, string id, SqliteTransaction tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {table} WHERE {column} = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery();
    }

    private List<string> SelectIds(string query, string cutoffUtc, int limit, SqliteTransaction tx)
    {
        var ids = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = query;
        cmd.Parameters.AddWithValue("@cutoff", cutoffUtc);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            ids.Add(r.GetString(0));
        return ids;
    }

    private void AddInstanceParams(SqliteCommand cmd, WorkflowInstanceRecord rec)
    {
        cmd.Parameters.AddWithValue("@id", rec.Id);
        cmd.Parameters.AddWithValue("@name", rec.Name);
        cmd.Parameters.AddWithValue("@input_json", rec.InputJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", rec.Status);
        cmd.Parameters.AddWithValue("@result_json", rec.ResultJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@error_json", rec.ErrorJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at_utc", rec.CreatedAtUtc);
        cmd.Parameters.AddWithValue("@updated_at_utc", rec.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("@started_at_utc", rec.StartedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@finished_at_utc", rec.FinishedAtUtc ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@runtime_version", rec.RuntimeVersion ?? "1.0");
        cmd.Parameters.AddWithValue("@correlation_id", rec.CorrelationId ?? (object)DBNull.Value);
    }

    private static WorkflowInstanceRecord ReadInstance(SqliteDataReader r)
    {
        return new WorkflowInstanceRecord
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            InputJson = r.IsDBNull(2) ? null : r.GetString(2),
            Status = r.GetString(3),
            ResultJson = r.IsDBNull(4) ? null : r.GetString(4),
            ErrorJson = r.IsDBNull(5) ? null : r.GetString(5),
            CreatedAtUtc = r.GetString(6),
            UpdatedAtUtc = r.GetString(7),
            StartedAtUtc = r.IsDBNull(8) ? null : r.GetString(8),
            FinishedAtUtc = r.IsDBNull(9) ? null : r.GetString(9),
            RuntimeVersion = r.IsDBNull(10) ? null : r.GetString(10),
            CorrelationId = r.IsDBNull(11) ? null : r.GetString(11)
        };
    }

    private static WorkflowStepRecord ReadStep(SqliteDataReader r)
    {
        return new WorkflowStepRecord
        {
            Id = r.GetString(0),
            WorkflowInstanceId = r.GetString(1),
            StepName = r.GetString(2),
            StepKind = r.GetString(3),
            State = r.GetString(4),
            Attempt = r.GetInt32(5),
            MaxAttempts = r.GetInt32(6),
            TimeoutMs = r.IsDBNull(7) ? null : r.GetInt32(7),
            InputJson = r.IsDBNull(8) ? null : r.GetString(8),
            OutputJson = r.IsDBNull(9) ? null : r.GetString(9),
            ErrorJson = r.IsDBNull(10) ? null : r.GetString(10),
            IdempotencyKey = r.IsDBNull(11) ? null : r.GetString(11),
            StartedAtUtc = r.IsDBNull(12) ? null : r.GetString(12),
            FinishedAtUtc = r.IsDBNull(13) ? null : r.GetString(13)
        };
    }

    private static WorkflowDeadLetterRecord ReadDeadLetter(SqliteDataReader r)
    {
        return new WorkflowDeadLetterRecord
        {
            Id = r.GetString(0),
            WorkflowInstanceId = r.GetString(1),
            StepName = r.GetString(2),
            Reason = r.GetString(3),
            PayloadJson = r.IsDBNull(4) ? null : r.GetString(4),
            CreatedAtUtc = r.GetString(5),
            RequeuedAtUtc = r.IsDBNull(6) ? null : r.GetString(6),
            RequeueReason = r.FieldCount > 7 && !r.IsDBNull(7) ? r.GetString(7) : null,
            RequeueRequestedBy = r.FieldCount > 8 && !r.IsDBNull(8) ? r.GetString(8) : null,
            RequeueCorrelationId = r.FieldCount > 9 && !r.IsDBNull(9) ? r.GetString(9) : null,
            RequeueAttempts = r.FieldCount > 10 && !r.IsDBNull(10) ? r.GetInt32(10) : 0
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _connection.Dispose();
        }
        catch (NullReferenceException)
        {
            // Some providers can throw during teardown if already partially closed.
            // Dispose should remain best-effort and non-throwing.
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class WorkflowInstanceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? InputJson { get; set; }
    public string Status { get; set; } = "";
    public string? ResultJson { get; set; }
    public string? ErrorJson { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public string? StartedAtUtc { get; set; }
    public string? FinishedAtUtc { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? CorrelationId { get; set; }
}

public class WorkflowStepRecord
{
    public string Id { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string StepName { get; set; } = "";
    public string StepKind { get; set; } = "normal";
    public string State { get; set; } = "";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }
    public int? TimeoutMs { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorJson { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? StartedAtUtc { get; set; }
    public string? FinishedAtUtc { get; set; }
}

public class WorkflowEventRecord
{
    public string Id { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
}

public class WorkflowDeadLetterRecord
{
    public string Id { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string StepName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? PayloadJson { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string? RequeuedAtUtc { get; set; }
    public string? RequeueReason { get; set; }
    public string? RequeueRequestedBy { get; set; }
    public string? RequeueCorrelationId { get; set; }
    public int RequeueAttempts { get; set; }
}
