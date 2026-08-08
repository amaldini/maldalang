// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Jobs;

using System.Text.Json;
using Microsoft.Data.Sqlite;

public sealed class JobRecord
{
    public string Id { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string PayloadJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? ErrorJson { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// SQLite-backed short job queue (separate from durable workflows).
/// </summary>
public sealed class JobStore : IDisposable
{
    private static readonly object SqliteInitLock = new();
    private static bool _sqliteProviderInitialized;
    private static readonly object DefaultLock = new();
    private static JobStore? _default;

    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public const string DefaultConnectionString = "Data Source=./.malda/jobs.db";

    public JobStore(string? connectionString = null)
    {
        EnsureSqliteProviderInitialized();
        var cs = connectionString ?? DefaultConnectionString;
        EnsureDbDirectory(cs);
        _connection = new SqliteConnection(cs);
        _connection.Open();
        EnsureSchema();
    }

    public static JobStore Default
    {
        get
        {
            if (_default != null)
            {
                return _default;
            }

            lock (DefaultLock)
            {
                return _default ??= new JobStore();
            }
        }
    }

    public static void ResetDefaultForTests()
    {
        lock (DefaultLock)
        {
            _default?.Dispose();
            _default = null;
        }
    }

    public static void SetDefaultForTests(JobStore store)
    {
        lock (DefaultLock)
        {
            if (!ReferenceEquals(_default, store))
            {
                _default?.Dispose();
            }

            _default = store;
        }
    }

    public string Enqueue(
        string queue,
        string payloadJson,
        DateTimeOffset? runAt = null,
        int maxAttempts = 3,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new Exception("enqueueJob() queue cannot be empty");
        }

        if (maxAttempts < 1)
        {
            throw new Exception("enqueueJob() maxAttempts must be >= 1");
        }

        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Queue = queue.Trim(),
            Status = "pending",
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            MaxAttempts = maxAttempts,
            RunAt = runAt ?? now,
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now
        };

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO jobs(
                    id, queue, status, payload_json, attempts, max_attempts, run_at,
                    correlation_id, created_at, updated_at)
                VALUES (
                    $id, $queue, $status, $payload, 0, $maxAttempts, $runAt,
                    $correlationId, $createdAt, $updatedAt);";
            cmd.Parameters.AddWithValue("$id", job.Id);
            cmd.Parameters.AddWithValue("$queue", job.Queue);
            cmd.Parameters.AddWithValue("$status", job.Status);
            cmd.Parameters.AddWithValue("$payload", job.PayloadJson);
            cmd.Parameters.AddWithValue("$maxAttempts", job.MaxAttempts);
            cmd.Parameters.AddWithValue("$runAt", job.RunAt.ToString("O"));
            cmd.Parameters.AddWithValue("$correlationId", (object?)job.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$updatedAt", job.UpdatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        return job.Id;
    }

    public JobRecord? Claim(string queue, string? workerId = null)
    {
        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new Exception("claimJob() queue cannot be empty");
        }

        lock (_lock)
        {
            using var tx = _connection.BeginTransaction();
            using (var select = _connection.CreateCommand())
            {
                select.Transaction = tx;
                select.CommandText = @"
                    SELECT id FROM jobs
                    WHERE queue = $queue
                      AND status = 'pending'
                      AND run_at <= $now
                    ORDER BY run_at ASC, created_at ASC
                    LIMIT 1;";
                select.Parameters.AddWithValue("$queue", queue.Trim());
                select.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                var idObj = select.ExecuteScalar();
                if (idObj == null || idObj is DBNull)
                {
                    tx.Commit();
                    return null;
                }

                var id = Convert.ToString(idObj)!;
                var locker = string.IsNullOrWhiteSpace(workerId) ? Guid.NewGuid().ToString("N") : workerId.Trim();
                var now = DateTimeOffset.UtcNow;
                using var update = _connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = @"
                    UPDATE jobs
                    SET status = 'running',
                        attempts = attempts + 1,
                        locked_by = $lockedBy,
                        locked_at = $lockedAt,
                        updated_at = $updatedAt
                    WHERE id = $id AND status = 'pending';";
                update.Parameters.AddWithValue("$lockedBy", locker);
                update.Parameters.AddWithValue("$lockedAt", now.ToString("O"));
                update.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                update.Parameters.AddWithValue("$id", id);
                var rows = update.ExecuteNonQuery();
                if (rows == 0)
                {
                    tx.Commit();
                    return null;
                }

                tx.Commit();
                return Get(id);
            }
        }
    }

    public JobRecord? Get(string jobId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, queue, status, payload_json, result_json, error_json, attempts, max_attempts,
                       run_at, locked_by, locked_at, correlation_id, created_at, updated_at
                FROM jobs WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", jobId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return ReadJob(reader);
        }
    }

    public void Complete(string jobId, string? resultJson = null)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE jobs
                SET status = 'succeeded',
                    result_json = $result,
                    error_json = NULL,
                    locked_by = NULL,
                    locked_at = NULL,
                    updated_at = $updatedAt
                WHERE id = $id;";
            cmd.Parameters.AddWithValue("$result", (object?)resultJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", jobId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                throw new Exception($"completeJob() job not found: {jobId}");
            }
        }
    }

    public void Fail(string jobId, string? errorJson = null, bool retry = true)
    {
        var job = Get(jobId) ?? throw new Exception($"failJob() job not found: {jobId}");
        var now = DateTimeOffset.UtcNow;
        var shouldRetry = retry && job.Attempts < job.MaxAttempts;
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            if (shouldRetry)
            {
                var backoffSeconds = Math.Min(300, (int)Math.Pow(2, Math.Max(0, job.Attempts - 1)));
                var runAt = now.AddSeconds(backoffSeconds);
                cmd.CommandText = @"
                    UPDATE jobs
                    SET status = 'pending',
                        error_json = $error,
                        run_at = $runAt,
                        locked_by = NULL,
                        locked_at = NULL,
                        updated_at = $updatedAt
                    WHERE id = $id;";
                cmd.Parameters.AddWithValue("$runAt", runAt.ToString("O"));
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE jobs
                    SET status = 'dead',
                        error_json = $error,
                        locked_by = NULL,
                        locked_at = NULL,
                        updated_at = $updatedAt
                    WHERE id = $id;";
            }

            cmd.Parameters.AddWithValue("$error", (object?)errorJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.ExecuteNonQuery();
        }
    }

    public List<JobRecord> List(string? queue = null, string? status = null, int limit = 50)
    {
        if (limit < 1)
        {
            limit = 50;
        }

        if (limit > 500)
        {
            limit = 500;
        }

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            var clauses = new List<string>();
            if (!string.IsNullOrWhiteSpace(queue))
            {
                clauses.Add("queue = $queue");
                cmd.Parameters.AddWithValue("$queue", queue.Trim());
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                clauses.Add("status = $status");
                cmd.Parameters.AddWithValue("$status", status.Trim());
            }

            var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
            cmd.CommandText = $@"
                SELECT id, queue, status, payload_json, result_json, error_json, attempts, max_attempts,
                       run_at, locked_by, locked_at, correlation_id, created_at, updated_at
                FROM jobs
                {where}
                ORDER BY created_at DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);

            var results = new List<JobRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadJob(reader));
            }

            return results;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS jobs (
                id TEXT PRIMARY KEY,
                queue TEXT NOT NULL,
                status TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                result_json TEXT,
                error_json TEXT,
                attempts INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 3,
                run_at TEXT NOT NULL,
                locked_by TEXT,
                locked_at TEXT,
                correlation_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_jobs_claim ON jobs(queue, status, run_at);";
        cmd.ExecuteNonQuery();
    }

    private static JobRecord ReadJob(SqliteDataReader reader)
    {
        return new JobRecord
        {
            Id = reader.GetString(0),
            Queue = reader.GetString(1),
            Status = reader.GetString(2),
            PayloadJson = reader.GetString(3),
            ResultJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            ErrorJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            Attempts = reader.GetInt32(6),
            MaxAttempts = reader.GetInt32(7),
            RunAt = DateTimeOffset.Parse(reader.GetString(8)),
            LockedBy = reader.IsDBNull(9) ? null : reader.GetString(9),
            LockedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            CorrelationId = reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(12)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(13))
        };
    }

    private static void EnsureSqliteProviderInitialized()
    {
        if (_sqliteProviderInitialized)
        {
            return;
        }

        lock (SqliteInitLock)
        {
            if (_sqliteProviderInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _sqliteProviderInitialized = true;
        }
    }

    private static void EnsureDbDirectory(string connectionString)
    {
        try
        {
            const string prefix = "Data Source=";
            var idx = connectionString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return;
            }

            var path = connectionString[(idx + prefix.Length)..].Trim();
            if (path.StartsWith('\'') && path.EndsWith('\''))
            {
                path = path[1..^1];
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public static string ToJson(JobRecord job)
    {
        return JsonSerializer.Serialize(new
        {
            id = job.Id,
            queue = job.Queue,
            status = job.Status,
            payload = job.PayloadJson,
            result = job.ResultJson,
            error = job.ErrorJson,
            attempts = job.Attempts,
            maxAttempts = job.MaxAttempts,
            runAt = job.RunAt.ToString("O"),
            lockedBy = job.LockedBy,
            lockedAt = job.LockedAt?.ToString("O"),
            correlationId = job.CorrelationId,
            createdAt = job.CreatedAt.ToString("O"),
            updatedAt = job.UpdatedAt.ToString("O")
        });
    }
}
