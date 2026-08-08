// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Jobs;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class JobStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly JobStore _store;

    public JobStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "malda-jobs-test-" + Guid.NewGuid().ToString("N") + ".db");
        _store = new JobStore($"Data Source={_dbPath}");
        JobStore.SetDefaultForTests(_store);
    }

    public void Dispose()
    {
        JobStore.ResetDefaultForTests();
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    [Fact]
    public void EnqueueClaimComplete_RoundTrip()
    {
        var id = _store.Enqueue("mail", "{\"to\":\"a@b.c\"}");
        var claimed = _store.Claim("mail", "worker-1");
        Assert.NotNull(claimed);
        Assert.Equal(id, claimed!.Id);
        Assert.Equal("running", claimed.Status);
        Assert.Equal(1, claimed.Attempts);

        _store.Complete(id, "{\"sent\":true}");
        var done = _store.Get(id);
        Assert.NotNull(done);
        Assert.Equal("succeeded", done!.Status);
        Assert.Null(_store.Claim("mail"));
    }

    [Fact]
    public void Fail_RetriesThenDeadLetters()
    {
        var id = _store.Enqueue("webhooks", "{}", maxAttempts: 2);
        var first = _store.Claim("webhooks");
        Assert.NotNull(first);
        _store.Fail(id, "\"boom\"", retry: true);

        var pending = _store.Get(id);
        Assert.Equal("pending", pending!.Status);

        // Force run_at to now for immediate reclaim
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET run_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        var second = _store.Claim("webhooks");
        Assert.NotNull(second);
        _store.Fail(id, "\"boom\"", retry: true);
        Assert.Equal("dead", _store.Get(id)!.Status);
    }

    [Fact]
    public void Builtins_EnqueueClaimComplete()
    {
        var jobId = BuiltInFunctions.CallBuiltIn(
            "enqueueJob",
            new List<RuntimeValue>
            {
                RuntimeValue.String("default"),
                RuntimeValue.Object(new JsonObject())
            },
            null!).AsString();

        var claimed = BuiltInFunctions.CallBuiltIn(
            "claimJob",
            new List<RuntimeValue> { RuntimeValue.String("default") },
            null!);
        Assert.Equal(ValueType.Object, claimed.Type);
        Assert.Equal(jobId, claimed.AsObject().Get("id", null).AsString());

        BuiltInFunctions.CallBuiltIn(
            "completeJob",
            new List<RuntimeValue>
            {
                RuntimeValue.String(jobId),
                RuntimeValue.Object(new JsonObject())
            },
            null!);

        var got = BuiltInFunctions.CallBuiltIn(
            "getJob",
            new List<RuntimeValue> { RuntimeValue.String(jobId) },
            null!).AsObject();
        Assert.Equal("succeeded", got.Get("status", null).AsString());
    }
}
