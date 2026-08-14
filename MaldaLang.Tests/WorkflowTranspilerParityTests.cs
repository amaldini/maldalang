using System.Text;
using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Runtime.Workflows;
using Xunit;

namespace MaldaLang.Tests;

[Collection("WorkflowEngineSerial")]
public class WorkflowTranspilerParityTests
{
    private static string NewDbPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"workflow_parity_{suffix}_{Guid.NewGuid():N}.db");

    private sealed record WorkflowSnapshot(
        string Status,
        List<string> StepStates,
        List<string> EventTypes,
        string? StepMetadataShape);

    [Fact]
    public void Parity_BasicLifecycle_StartGetSteps()
    {
        var source = """
            function addOne(x) { return x + 1; }
            workflow Basic(input) {
                step first = addOne(1);
                return first;
            }
            var id = startWorkflow("Basic", {"a":1});
            print(id);
            """;

        AssertParity(source, "Basic");
    }

    [Fact]
    public void Parity_RetryWithBackoffOutcome()
    {
        var source = """
            var attempts = 0;
            function flaky() {
                attempts = attempts + 1;
                if (attempts == 1) {
                    sleep(25);
                }
                return attempts;
            }
            workflow RetryFlow(input) {
                step x = flaky() retry 2 backoff "fixed" delay 1 timeout 5;
                return x;
            }
            var id = startWorkflow("RetryFlow", null);
            print(id);
            """;

        AssertParity(source, "RetryFlow");
    }

    [Fact]
    public void Parity_TimeoutPath()
    {
        var source = """
            function alwaysSlow() {
                sleep(30);
                return 1;
            }
            workflow TimeoutFlow(input) {
                step x = alwaysSlow() timeout 5;
                return x;
            }
            startWorkflow("TimeoutFlow", null);
            """;

        AssertParity(source, "TimeoutFlow");
    }

    [Fact]
    public void Parity_ApprovalAndSignalResolution()
    {
        var source = """
            workflow WaitFlow(input) {
                approval gate = approval("manager", {"x":1});
                wait docs = awaitSignal("docs_uploaded", {"x":1});
                return docs;
            }
            var id = startWorkflow("WaitFlow", null);
            approveWorkflowStep(id, "gate", "approve", {"ok":true});
            signalWorkflow(id, "docs_uploaded", {"fileId":"A1"});
            runWorkflowInstance(id);
            print(id);
            """;

        AssertParity(source, "WaitFlow");
    }

    [Fact]
    public void Transpile_NowInHelperFromWorkflowBody_ThrowsWF1001()
    {
        var dbPath = NewDbPath("helper_body");
        var source = """
            function stamp() {
                return now();
            }
            workflow Bad(input) {
                stamp();
            }
            startWorkflow("Bad", null);
            """;
        var env = new Dictionary<string, string>
        {
            ["MALDA_WORKFLOW_CONNECTION"] = "Data Source=" + dbPath
        };
        var result = TranspiledTestRunner.CompileAndRunFromSource(source, includeUiHost: false, environmentVariables: env);
        var combined = result.StdOut + result.StdErr;
        Assert.Contains("WF1001", combined, StringComparison.Ordinal);
        Assert.Contains("now", combined, StringComparison.Ordinal);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Transpile_NowInHelperInsideStep_Completes()
    {
        var source = """
            function stamp() {
                return now();
            }
            workflow Ok(input) {
                step t = stamp();
                return t;
            }
            startWorkflow("Ok", null);
            """;
        AssertParity(source, "Ok");
    }

    [Fact]
    public void Parity_CompensationOutcome()
    {
        var source = """
            function ok(x) { return x; }
            function boom() { error("boom"); }
            workflow CompFlow(input) {
                step one = ok("1") compensate ok("undo-1");
                step two = ok("2") compensate ok("undo-2");
                step bad = boom();
                return 0;
            }
            startWorkflow("CompFlow", null);
            """;

        AssertParity(source, "CompFlow");
    }

    private static void AssertParity(string source, string workflowName)
    {
        var interpreterDb = NewDbPath("interp");
        var transpiledDb = NewDbPath("transp");

        var interpreterSnapshot = RunInterpreterAndSnapshot(source, workflowName, interpreterDb);
        var transpiledSnapshot = RunTranspiledAndSnapshot(source, workflowName, transpiledDb);

        Assert.Equal(interpreterSnapshot.Status, transpiledSnapshot.Status);
        Assert.Equal(interpreterSnapshot.StepStates, transpiledSnapshot.StepStates);
        Assert.Equal(interpreterSnapshot.EventTypes, transpiledSnapshot.EventTypes);
        Assert.Equal(interpreterSnapshot.StepMetadataShape, transpiledSnapshot.StepMetadataShape);
    }

    private static WorkflowSnapshot RunInterpreterAndSnapshot(string source, string workflowName, string dbPath)
    {
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interpreter = new Interpreter.Interpreter();
        var output = new StringBuilder();
        interpreter.SetOutputCallback(s => output.AppendLine(s));
        try
        {
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        }
        catch
        {
            // Some parity scenarios intentionally fail terminally.
        }

        return CaptureSnapshot(workflowName, dbPath);
    }

    private static WorkflowSnapshot RunTranspiledAndSnapshot(string source, string workflowName, string dbPath)
    {
        var env = new Dictionary<string, string>
        {
            ["MALDA_WORKFLOW_CONNECTION"] = "Data Source=" + dbPath
        };

        var result = TranspiledTestRunner.CompileAndRunFromSource(source, includeUiHost: false, environmentVariables: env);
        Assert.True(
            result.ExitCode == 0 || result.ExitCode == 1,
            $"Unexpected transpiled exit code {result.ExitCode}. stdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var probe = WorkflowEngine.Instance.ListInstances(name: workflowName, limit: 1).FirstOrDefault();
        Assert.True(
            probe != null,
            $"No transpiled workflow instance persisted for '{workflowName}'. ExitCode={result.ExitCode}\nstdout:\n{result.StdOut}\nstderr:\n{result.StdErr}");

        return CaptureSnapshot(workflowName, dbPath);
    }

    private static WorkflowSnapshot CaptureSnapshot(string workflowName, string dbPath)
    {
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var engine = WorkflowEngine.Instance;
        var instance = engine.ListInstances(name: workflowName, limit: 1).FirstOrDefault();
        Assert.NotNull(instance);

        var steps = engine.GetSteps(instance!.Id)
            .OrderBy(s => s.Attempt)
            .ThenBy(s => s.StepName, StringComparer.Ordinal)
            .Select(s => $"{s.StepName}:{s.State}:{s.Attempt}")
            .ToList();
        var events = engine.GetEvents(instance.Id, 500)
            .Select(e => e.EventType)
            .ToList();
        var firstStepStartedPayload = engine.GetEvents(instance.Id, 500)
            .FirstOrDefault(e => e.EventType == "workflow_step_started")?.PayloadJson;
        var metadataShape = DescribeStepEventMetadataShape(firstStepStartedPayload);
        return new WorkflowSnapshot(instance.Status, steps, events, metadataShape);
    }

    private static string? DescribeStepEventMetadataShape(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        var hasCorrelation = root.TryGetProperty("correlationId", out _);
        var hasWorkflowId = root.TryGetProperty("workflowInstanceId", out _);
        var hasStepName = root.TryGetProperty("stepName", out _);
        var hasAttempt = root.TryGetProperty("attempt", out _);
        var hasDetails = root.TryGetProperty("details", out _);
        return $"{hasCorrelation}:{hasWorkflowId}:{hasStepName}:{hasAttempt}:{hasDetails}";
    }
}
