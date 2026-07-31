// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MaldaLang;
using MaldaLang.Runtime.Workflows;
using Xunit;

namespace MaldaLang.Tests;

[Collection("WorkflowEngineSerial")]
public class WorkflowCliTests : TestBase
{
    [Fact]
    public void WorkflowCli_StartGetSteps_UsesPersistedState()
    {
        var tempDir = CreateTempDirectory("workflow_cli_");
        var dbPath = Path.Combine(tempDir, "workflows.db");
        var scriptPath = Path.Combine(tempDir, "wf.malda");
        var inputPath = Path.Combine(tempDir, "input.json");

        try
        {
            File.WriteAllText(
                scriptPath,
                """
                function identity(x) { return x; }

                workflow OnboardCli(input) {
                    step stepResult = identity(input.value);
                    return stepResult;
                }
                """);
            File.WriteAllText(inputPath, """{"value":42,"correlationId":"cli-corr-42"}""");

            WorkflowEngine.ResetForTesting("Data Source=" + dbPath);

            var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
            Assert.NotNull(programType);

            var workflowCommand = programType!.GetMethod(
                "WorkflowCommand",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(workflowCommand);

            static int InvokeWorkflow(MethodInfo method, string[] args) =>
                (int)(method.Invoke(null, new object[] { args }) ?? -1);

            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);

                try
                {
                    var startCode = InvokeWorkflow(workflowCommand!, new[] { "start", scriptPath, "OnboardCli", "--input", inputPath });
                    if (startCode != 0)
                    {
                        var startStdOut = output.ToString().Replace("\r", "");
                        var startStdErr = error.ToString().Replace("\r", "");
                        Assert.True(false, $"workflow start failed with code {startCode}\nstdout:\n{startStdOut}\nstderr:\n{startStdErr}");
                    }

                    var startOut = output.ToString().Replace("\r", "");
                    var idMatch = Regex.Match(startOut, @"\b[a-f0-9]{32}\b");
                    Assert.True(idMatch.Success, $"Expected workflow instance id in output. Output:\n{startOut}");
                    var instanceId = idMatch.Value;

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();

                    var getCode = InvokeWorkflow(workflowCommand, new[] { "get", instanceId });
                    Assert.Equal(0, getCode);
                    var getOut = output.ToString().Replace("\r", "");
                    Assert.Contains($"id: {instanceId}", getOut);
                    Assert.Contains("name: OnboardCli", getOut);
                    Assert.Contains("status: COMPLETED", getOut);
                    Assert.Contains("correlation_id: cli-corr-42", getOut);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();

                    var stepsCode = InvokeWorkflow(workflowCommand, new[] { "steps", instanceId });
                    Assert.Equal(0, stepsCode);
                    var stepsOut = output.ToString().Replace("\r", "");
                    Assert.Contains("stepResult", stepsOut);
                    Assert.Contains("SUCCEEDED", stepsOut);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var eventsCode = InvokeWorkflow(workflowCommand, new[] { "events", instanceId, "--limit", "5" });
                    Assert.Equal(0, eventsCode);
                    var eventsOut = output.ToString().Replace("\r", "");
                    Assert.Contains("workflow_started", eventsOut);
                    Assert.Contains("workflowInstanceId", eventsOut);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var metricsCode = InvokeWorkflow(workflowCommand, new[] { "metrics" });
                    Assert.Equal(0, metricsCode);
                    var metricsOut = output.ToString().Replace("\r", "");
                    Assert.Contains("workflow_instances_started_total", metricsOut);
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WorkflowCli_ApproveAndSignal_WithInvalidStateChecks()
    {
        var tempDir = CreateTempDirectory("workflow_cli_waits_");
        var dbPath = Path.Combine(tempDir, "workflows.db");
        var scriptPath = Path.Combine(tempDir, "wf_waits.malda");
        var payloadPath = Path.Combine(tempDir, "payload.json");

        try
        {
            File.WriteAllText(
                scriptPath,
                """
                workflow ApprovalCli(input) {
                    approval gate = approval("manager", {"id": 1});
                    return gate;
                }

                workflow SignalCli(input) {
                    wait docs = awaitSignal("docs_uploaded", {"id": 1});
                    return docs;
                }
                """);
            File.WriteAllText(payloadPath, """{"approvedBy":"qa"}""");

            WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
            var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
            Assert.NotNull(programType);
            var workflowCommand = programType!.GetMethod("WorkflowCommand", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(workflowCommand);
            static int InvokeWorkflow(MethodInfo method, string[] args) =>
                (int)(method.Invoke(null, new object[] { args }) ?? -1);

            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);

                try
                {
                    var startApproval = InvokeWorkflow(workflowCommand!, new[] { "start", scriptPath, "ApprovalCli" });
                    Assert.Equal(0, startApproval);
                    var approvalMatch = Regex.Match(output.ToString().Replace("\r", ""), @"\b[a-f0-9]{32}\b");
                    Assert.True(approvalMatch.Success);
                    var approvalId = approvalMatch.Value;

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var approveCode = InvokeWorkflow(workflowCommand!, new[] { "approve", approvalId, "gate", "--decision", "approve", "--payload", payloadPath });
                    Assert.Equal(0, approveCode);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var approveInvalidState = InvokeWorkflow(workflowCommand!, new[] { "approve", approvalId, "gate", "--decision", "approve" });
                    Assert.Equal(3, approveInvalidState);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var startSignal = InvokeWorkflow(workflowCommand!, new[] { "start", scriptPath, "SignalCli" });
                    Assert.Equal(0, startSignal);
                    var signalMatch = Regex.Match(output.ToString().Replace("\r", ""), @"\b[a-f0-9]{32}\b");
                    Assert.True(signalMatch.Success);
                    var signalId = signalMatch.Value;

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var signalCode = InvokeWorkflow(workflowCommand!, new[] { "signal", signalId, "docs_uploaded", "--payload", payloadPath });
                    Assert.Equal(0, signalCode);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var signalInvalid = InvokeWorkflow(workflowCommand!, new[] { "signal", signalId, "docs_uploaded" });
                    Assert.Equal(3, signalInvalid);
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WorkflowCli_JsonMode_And_DlqCommands_AreStable()
    {
        var tempDir = CreateTempDirectory("workflow_cli_json_");
        var dbPath = Path.Combine(tempDir, "workflows.db");
        var scriptPath = Path.Combine(tempDir, "wf_json.malda");

        try
        {
            File.WriteAllText(
                scriptPath,
                """
                function identity(x) { return x; }
                workflow JsonFlow(input) {
                    step one = identity(1);
                    return one;
                }
                """);

            WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
            var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
            Assert.NotNull(programType);
            var workflowCommand = programType!.GetMethod("WorkflowCommand", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(workflowCommand);

            static int InvokeWorkflow(MethodInfo method, string[] args) =>
                (int)(method.Invoke(null, new object[] { args }) ?? -1);

            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);

                try
                {
                    var startCode = InvokeWorkflow(workflowCommand!, new[] { "start", scriptPath, "JsonFlow", "--json" });
                    Assert.Equal(0, startCode);

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var listCode = InvokeWorkflow(workflowCommand!, new[] { "list", "--format", "json" });
                    Assert.Equal(0, listCode);
                    Assert.StartsWith("[", output.ToString().Trim());

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var dlqListCode = InvokeWorkflow(workflowCommand!, new[] { "dlq", "list", "--json" });
                    Assert.Equal(0, dlqListCode);
                    Assert.StartsWith("[", output.ToString().Trim());

                    output.GetStringBuilder().Clear();
                    error.GetStringBuilder().Clear();
                    var requeueMissing = InvokeWorkflow(workflowCommand!, new[] { "dlq", "requeue", "missing-id", "--json" });
                    Assert.Equal(2, requeueMissing);
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WorkflowCli_Start_WhenDisabled_ReturnsError()
    {
        var tempDir = CreateTempDirectory("workflow_cli_disabled_");
        var dbPath = Path.Combine(tempDir, "workflows.db");
        var scriptPath = Path.Combine(tempDir, "wf_disabled.malda");

        try
        {
            File.WriteAllText(
                scriptPath,
                """
                workflow DisabledByConfig(input) { return 1; }
                """);

            WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
            WorkflowEngine.Instance.ConfigureRuntimeOptions(new WorkflowRuntimeOptions
            {
                Enabled = false,
                MaxRetriesPerStep = 10,
                MaxPayloadBytes = 1024 * 1024,
                MaxWorkflowDurationMs = 7 * 24 * 60 * 60 * 1000
            });

            var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
            Assert.NotNull(programType);
            var workflowCommand = programType!.GetMethod("WorkflowCommand", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(workflowCommand);
            static int InvokeWorkflow(MethodInfo method, string[] args) =>
                (int)(method.Invoke(null, new object[] { args }) ?? -1);

            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);
                try
                {
                    var code = InvokeWorkflow(workflowCommand!, new[] { "start", scriptPath, "DisabledByConfig" });
                    Assert.Equal(1, code);
                    Assert.Contains("disabled", error.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WorkflowCli_MaintenanceRun_SupportsJsonOutput()
    {
        var tempDir = CreateTempDirectory("workflow_cli_maintenance_");
        var dbPath = Path.Combine(tempDir, "workflows.db");
        try
        {
            WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
            var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
            Assert.NotNull(programType);
            var workflowCommand = programType!.GetMethod("WorkflowCommand", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(workflowCommand);
            static int InvokeWorkflow(MethodInfo method, string[] args) =>
                (int)(method.Invoke(null, new object[] { args }) ?? -1);

            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);
                try
                {
                    var code = InvokeWorkflow(workflowCommand!, new[] { "maintenance", "run", "--dry-run", "--format", "json" });
                    Assert.Equal(0, code);
                    Assert.StartsWith("{", output.ToString().Trim());
                    Assert.Contains("\"dryRun\":true", output.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
