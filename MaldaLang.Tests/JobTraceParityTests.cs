// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Ship-contract job traces: same enqueue/claim/complete journal on interpret
/// and C# transpile. Isolates SQLite via <c>MALDA_JOBS_CONNECTION</c> so cwd
/// no longer leaks <c>./.malda/jobs.db</c>. Generated ids are stripped.
/// </summary>
[Collection("JobStoreSerial")]
public class JobTraceParityTests
{
    private static readonly Regex JobId = new(@"\b[0-9a-fA-F]{32}\b", RegexOptions.Compiled);

    private const string InlineJournalSource = """
        var id1 = enqueueJob("mail", { "to": "ada@example.com", "subject": "Hello" });
        var id2 = enqueueJob("mail", { "to": "grace@example.com", "subject": "Hi" }, { "maxAttempts": 2 });
        var claimed = [];
        var job = claimJob("mail", "worker-demo");
        while (job != null) {
            completeJob(job.id, { "sent": true });
            claimed.append(job.payload);
            job = claimJob("mail", "worker-demo");
        }
        var done = listJobs("mail", "succeeded", 10);
        var results = [];
        foreach (var row in done) {
            results.append(row.result);
        }
        print(toJSON({
            "queued": 2,
            "claimed": claimed.length,
            "succeeded": done.length,
            "payloads": claimed,
            "results": results
        }));
        """;

    [Fact]
    public void Default_UsesMaldaJobsConnectionEnv()
    {
        JobStore.ResetDefaultForTests();
        var path = Path.Combine(Path.GetTempPath(), "malda-jobs-env-" + Guid.NewGuid().ToString("N") + ".db");
        var previous = Environment.GetEnvironmentVariable("MALDA_JOBS_CONNECTION");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_JOBS_CONNECTION", "Data Source=\"" + path + "\"");
            var id = JobStore.Default.Enqueue("env", "{}");
            Assert.True(File.Exists(path), "MALDA_JOBS_CONNECTION did not create the isolated jobs db");
            Assert.NotNull(JobStore.Default.Get(id));
        }
        finally
        {
            JobStore.ResetDefaultForTests();
            Environment.SetEnvironmentVariable("MALDA_JOBS_CONNECTION", previous);
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void InlineJournal_InterpretAndTranspile_SameNormalizedStdout()
    {
        AssertSameJournal(InlineJournalSource, "inline-job-journal");
    }

    [Fact]
    public void JobQueueBasicExample_InterpretAndTranspile_SameNormalizedStdout()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "job_queue_basic.malda");
        Assert.True(File.Exists(sourcePath), $"Missing {sourcePath}");
        AssertSameJournal(File.ReadAllText(sourcePath), "Examples/Web/job_queue_basic.malda");
    }

    private static void AssertSameJournal(string source, string label)
    {
        var interpret = CaptureInterpretJournal(source);
        var transpile = CaptureTranspileJournal(source);
        Assert.True(
            interpret.ExitCode == 0,
            label + ": interpret failed: " + interpret.Error);
        Assert.True(
            transpile.ExitCode == 0,
            label + ": transpile failed: " + transpile.Error);
        Assert.Equal(interpret.Journal, transpile.Journal);
        Assert.False(string.IsNullOrWhiteSpace(interpret.Journal));
    }

    private static string JobsConnection(string dbPath) =>
        "Data Source=\"" + dbPath + "\"";

    private static (int ExitCode, string Journal, string Error) CaptureInterpretJournal(string source)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "malda_job_trace_i_" + Guid.NewGuid().ToString("N") + ".db");
        JobStore.ResetDefaultForTests();
        var store = new JobStore(JobsConnection(dbPath));
        JobStore.SetDefaultForTests(store);
        try
        {
            var outcome = TestBase.CaptureInterpretOutcomeAsync(source).GetAwaiter().GetResult();
            return (outcome.ExitCode, NormalizeJournal(outcome.StdOut), outcome.Exception?.Message ?? "");
        }
        finally
        {
            JobStore.ResetDefaultForTests();
            TryDeleteJobsDb(dbPath);
        }
    }

    private static (int ExitCode, string Journal, string Error) CaptureTranspileJournal(string source)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "malda_job_trace_t_" + Guid.NewGuid().ToString("N") + ".db");
        var env = new Dictionary<string, string>
        {
            ["MALDA_JOBS_CONNECTION"] = JobsConnection(dbPath)
        };
        try
        {
            var result = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: env);
            return (result.ExitCode, NormalizeJournal(result.StdOut), result.StdErr);
        }
        finally
        {
            TryDeleteJobsDb(dbPath);
        }
    }

    private static string NormalizeJournal(string stdout) =>
        JobId.Replace(InterpretTranspilePair.Normalize(stdout), "<id>");

    private static void TryDeleteJobsDb(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
