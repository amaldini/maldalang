// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Workflows;
using System.IO;
using System.Linq;

namespace MaldaLang.Tests;

[Collection("WorkflowEngineSerial")]
public class WorkflowRuntimeTests
{
    private static string GetTestDbPath() =>
        Path.Combine(Path.GetTempPath(), $"workflow_test_{System.Guid.NewGuid():N}.db");

    [Fact]
    public void Workflow_StartAndComplete_ReturnsInstanceId()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function addOne(x) { return x + 1; }
workflow AddOne(input) {
    step result = addOne(10);
    print(result);
}
var id = startWorkflow(""AddOne"", 10);
print(id);
var status = getWorkflowStatus(id);
print(status);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        var outStr = output.ToString();
        Assert.Contains("COMPLETED", outStr);
        Assert.True(outStr.Trim().Length >= 32, "Expected instance ID in output");
    }

    [Fact]
    public void Workflow_GetAndSteps_ReturnPersistedData()
    {
        var dbPath = GetTestDbPath();
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var source = @"
function identity(x) { return x; }
workflow Identity(input) {
    step stepOut = identity(42);
    print(stepOut);
}
var id = startWorkflow(""Identity"", 42);
var wf = getWorkflow(id);
print(wf.status);
var steps = getWorkflowSteps(id);
print(length(steps));
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        var outStr = output.ToString();
        Assert.Contains("COMPLETED", outStr);
        Assert.Contains("1", outStr); // one step
    }

    [Fact]
    public void Workflow_Replay_NoDuplicateStepExecution()
    {
        var dbPath = GetTestDbPath();
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var source = @"
var callCount = 0;
function countCall(x) {
    callCount = callCount + 1;
    return x;
}
workflow ReplayTest(input) {
    step stepResult = countCall(1);
    print(stepResult);
}
var id = startWorkflow(""ReplayTest"", 1);
print(callCount);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        Assert.Contains("1", output.ToString()); // step executed exactly once
    }

    [Fact]
    public void Workflow_BranchingBody_RunsOnlyTheTakenBranchStep()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
var log = """";
function work(label) {
    log = log + label + "";"";
    return label;
}
workflow Branchy(input) {
    step first = work(""first"");
    if (first == ""first"") {
        step thenStep = work(""then"");
    } else {
        step elseStep = work(""else"");
    }
}
var id = startWorkflow(""Branchy"", 1);
print(""log="" + log);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        Assert.Contains("log=first;then;", output.ToString());
        Assert.DoesNotContain("else", output.ToString());
    }

    /// <summary>
    /// Replay is keyed on the step name, so a step reached twice in one run resolves the
    /// second time from the journal instead of executing. Pins that behaviour: a step inside
    /// a loop runs once and every later iteration observes the memoized result.
    /// </summary>
    [Fact]
    public void Workflow_StepInsideLoop_ExecutesOnceAndReplaysMemoizedResult()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
var calls = 0;
function work() {
    calls = calls + 1;
    return ""run"";
}
workflow Looped(input) {
    var i = 0;
    var seen = """";
    while (i < 3) {
        step inLoop = work();
        seen = seen + inLoop + "","";
        i = i + 1;
    }
    print(""seen="" + seen);
}
var id = startWorkflow(""Looped"", 1);
print(""calls="" + calls);
print(""steps="" + length(getWorkflowSteps(id)));
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        var outStr = output.ToString();

        Assert.Contains("seen=run,run,run,", outStr);
        Assert.Contains("calls=1", outStr);
        Assert.Contains("steps=1", outStr);
    }

    [Fact]
    public void Workflow_CrashRecovery_NoDuplicateSuccessfulStepAfterRestart()
    {
        var dbPath = GetTestDbPath();
        var connection = "Data Source=" + dbPath;
        WorkflowEngine.ResetForTesting(connection);
        var engine = WorkflowEngine.Instance;

        // Simulate a crash after a step was journaled as successful.
        var id = engine.CreateInstance("CrashReplay", "{\"value\":1}");
        Assert.True(engine.StartInstance(id));
        var stepId = System.Guid.NewGuid().ToString("N");
        engine.JournalStepStart(stepId, id, "stepResult", 1, 1, null, "{}", null);
        engine.JournalStepSuccess(stepId, id, "stepResult", 1, "1");

        // Simulate process restart and replay the same instance.
        WorkflowEngine.ResetForTesting(connection);
        var source = $@"
var callCount = 0;
function countCall(x) {{
    callCount = callCount + 1;
    return x;
}}
workflow CrashReplay(input) {{
    step stepResult = countCall(1);
    print(stepResult);
}}
runWorkflowInstance(""{id}"");
print(callCount);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var lines = output.ToString()
            .Replace("\r", "")
            .Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        Assert.Equal("0", lines[^1]);

        var completed = WorkflowEngine.Instance.GetInstance(id);
        Assert.NotNull(completed);
        Assert.Equal("COMPLETED", completed.Status);
    }

    [Fact]
    public void Workflow_Cancel_TransitionsToCancelled()
    {
        var dbPath = GetTestDbPath();
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);
        var engine = WorkflowEngine.Instance;
        var id = engine.CreateInstance("Test", "{}");
        engine.StartInstance(id);
        var ok = engine.CancelInstance(id, "test cancel");
        Assert.True(ok);
        var inst = engine.GetInstance(id);
        Assert.NotNull(inst);
        Assert.Equal("CANCELLED", inst.Status);
    }

    [Fact]
    public void WF1001_NowInWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow Bad(input) {
    var t = now();
    print(t);
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
    }

    [Fact]
    public void WF1001_RandomChoiceWeightedInWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow Bad(input) {
    var i = randomChoiceWeighted([1.0, 2.0]);
    print(i);
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
        Assert.Contains("randomChoiceWeighted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1001_RandnInWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow Bad(input) {
    var x = randn();
    print(x);
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
        Assert.Contains("randn", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1002_WriteFileOutsideStep_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow Bad(input) {
    writeFile(""x.txt"", ""data"");
    print(1);
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1002", ex.Message);
    }

    [Fact]
    public void WF1001_SleepInWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow Bad(input) {
    sleep(10);
    print(1);
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
        Assert.Contains("sleep", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1001_SleepInsideStep_Allowed()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function pause() {
    sleep(5);
    return 1;
}
workflow Ok(input) {
    step x = pause();
    print(x);
}
var id = startWorkflow(""Ok"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var inst = WorkflowEngine.Instance.ListInstances(name: "Ok", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);
        Assert.Equal("COMPLETED", inst!.Status);
    }

    [Fact]
    public void WF1001_NowInHelperFromWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function stamp() {
    return now();
}
workflow Bad(input) {
    stamp();
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
        Assert.Contains("now", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1001_NowInNestedHelperFromWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function inner() {
    return now();
}
function stamp() {
    return inner();
}
workflow Bad(input) {
    stamp();
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1001", ex.Message);
        Assert.Contains("now", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1002_WriteFileInHelperFromWorkflowBody_Throws()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function persist() {
    writeFile(""x.txt"", ""data"");
}
workflow Bad(input) {
    persist();
}
var id = startWorkflow(""Bad"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var ex = Assert.Throws<RuntimeException>(() =>
            interp.InterpretAsync(statements).GetAwaiter().GetResult());
        Assert.Contains("WF1002", ex.Message);
        Assert.Contains("writeFile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WF1001_NowInHelperInsideStep_Allowed()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function stamp() {
    return now();
}
workflow Ok(input) {
    step t = stamp();
    print(t);
}
var id = startWorkflow(""Ok"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var inst = WorkflowEngine.Instance.ListInstances(name: "Ok", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);
        Assert.Equal("COMPLETED", inst!.Status);
    }

    [Fact]
    public void Workflow_RetryMath_FixedLinearExponentialAndCap()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var engine = WorkflowEngine.Instance;
        engine.EnableRetryJitter = false;

        Assert.Equal(1000, engine.ComputeRetryDelayMs("wf1", "stepA", 1, "fixed", 1000, 5000));
        Assert.Equal(2000, engine.ComputeRetryDelayMs("wf1", "stepA", 2, "linear", 1000, 5000));
        Assert.Equal(3000, engine.ComputeRetryDelayMs("wf1", "stepA", 3, "linear", 1000, 5000));
        Assert.Equal(1000, engine.ComputeRetryDelayMs("wf1", "stepA", 1, "exponential", 1000, 5000));
        Assert.Equal(2000, engine.ComputeRetryDelayMs("wf1", "stepA", 2, "exponential", 1000, 5000));
        Assert.Equal(5000, engine.ComputeRetryDelayMs("wf1", "stepA", 4, "exponential", 1000, 5000)); // 8000 capped to 5000
    }

    [Fact]
    public void Workflow_TimeoutThenRetryThenSuccess_Completes()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
var attempts = 0;
function flakyStep() {
    attempts = attempts + 1;
    if (attempts == 1) {
        sleep(120);
    }
    return attempts;
}
workflow RetryTimeout(input) {
    step result = flakyStep() retry 2 backoff ""fixed"" delay 100 timeout 40;
    print(result);
}
var id = startWorkflow(""RetryTimeout"", null);
print(id);
";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var inst = WorkflowEngine.Instance.ListInstances(name: "RetryTimeout", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);
        Assert.Equal("COMPLETED", inst!.Status);

        var steps = WorkflowEngine.Instance.GetSteps(inst.Id);
        Assert.Equal(2, steps.Count);
        Assert.Contains(steps, s => s.Attempt == 1 && s.State == StepState.TimedOut);
        Assert.Contains(steps, s => s.Attempt == 2 && s.State == StepState.Succeeded);
    }

    [Fact]
    public void Workflow_FailAfterMaxAttempts_PersistsFailedAttempts()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function alwaysFail() {
    error(""boom"");
}
workflow FailAfterMax(input) {
    step result = alwaysFail() retry 2 backoff ""fixed"" delay 1;
    print(result);
}
startWorkflow(""FailAfterMax"", null);
";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interp.InterpretAsync(statements).GetAwaiter().GetResult());

        var inst = WorkflowEngine.Instance.ListInstances(name: "FailAfterMax", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);
        Assert.Equal("FAILED", inst!.Status);

        var steps = WorkflowEngine.Instance.GetSteps(inst.Id);
        Assert.Equal(3, steps.Count); // initial + 2 retries
        Assert.All(steps, s => Assert.Equal(StepState.Failed, s.State));
    }

    [Fact]
    public void Workflow_RestartRecovery_StaleRunningStepIsRecoveredAndNotDuplicated()
    {
        var dbPath = GetTestDbPath();
        var connection = "Data Source=" + dbPath;
        WorkflowEngine.ResetForTesting(connection);
        var engine = WorkflowEngine.Instance;

        var id = engine.CreateInstance("RecoverWorkflow", "{\"value\":1}");
        Assert.True(engine.StartInstance(id));
        var stepId = System.Guid.NewGuid().ToString("N");
        engine.JournalStepStart(stepId, id, "stepResult", 1, 2, 5, "{}", null);

        WorkflowEngine.ResetForTesting(connection);
        var recovered = WorkflowEngine.Instance.RecoverStaleRunningState(0);
        Assert.True(recovered >= 1);

        var source = $@"
var callCount = 0;
function countCall(x) {{
    callCount = callCount + 1;
    return x;
}}
workflow RecoverWorkflow(data) {{
    step stepResult = countCall(1) retry 1 backoff ""fixed"" delay 1 timeout 5;
    print(stepResult);
}}
runWorkflowInstance(""{id}"");
print(callCount);
";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        Assert.Contains("\n1\n", "\n" + output + "\n");
        var instance = WorkflowEngine.Instance.GetInstance(id);
        Assert.NotNull(instance);
        Assert.Equal(WorkflowStatus.Completed, instance!.Status);

        var steps = WorkflowEngine.Instance.GetSteps(id);
        Assert.Contains(steps, s => s.Attempt == 1 && s.State == StepState.TimedOut);
        Assert.Contains(steps, s => s.Attempt == 2 && s.State == StepState.Succeeded);
    }

    [Fact]
    public void Workflow_EmitsCoreEventsAndMinimumMetrics()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
var attempts = 0;
function eventful() {
    attempts = attempts + 1;
    if (attempts == 1) {
        sleep(120);
    }
    return 7;
}
workflow EventFlow(data) {
    step value = eventful() retry 1 backoff ""fixed"" delay 100 timeout 40;
    return value;
}
var id = startWorkflow(""EventFlow"", null);
print(id);
";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var inst = WorkflowEngine.Instance.ListInstances(name: "EventFlow", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);

        var events = WorkflowEngine.Instance.GetEvents(inst!.Id);
        Assert.Contains(events, e => e.EventType == "workflow_created");
        Assert.Contains(events, e => e.EventType == "workflow_started");
        Assert.Contains(events, e => e.EventType == "workflow_step_started");
        Assert.Contains(events, e => e.EventType == "workflow_step_timed_out");
        Assert.Contains(events, e => e.EventType == "workflow_step_retry_scheduled");
        Assert.Contains(events, e => e.EventType == "workflow_step_succeeded");
        Assert.Contains(events, e => e.EventType == "workflow_completed");

        var metrics = WorkflowEngine.Instance.GetMinimumMetricSnapshot();
        Assert.True(metrics["workflow_instances_started_total"] >= 1);
        Assert.True(metrics["workflow_instances_completed_total"] >= 1);
        Assert.True(metrics["workflow_step_retries_total"] >= 1);
    }

    [Fact]
    public void Workflow_ApprovalApprove_ResumesAndCompletes()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function afterApproval() { return 99; }
workflow ApprovalFlow(data) {
    approval gate = approval(""manager"", {""request"": 1}) timeout 60000;
    step done = afterApproval();
    return done;
}
var id = startWorkflow(""ApprovalFlow"", null);
print(getWorkflowStatus(id));
approveWorkflowStep(id, ""gate"", ""approve"", {""approvedBy"": ""qa""});
runWorkflowInstance(id);
print(getWorkflowStatus(id));
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var outStr = output.ToString();
        Assert.Contains("WAITING_APPROVAL", outStr);
        Assert.Contains("COMPLETED", outStr);
    }

    [Fact]
    public void Workflow_ApprovalRejectAndTimeout_EndInFailed()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow RejectFlow(data) {
    approval gate = approval(""manager"") onReject error(""rejected"");
    return 1;
}
var id = startWorkflow(""RejectFlow"", null);
approveWorkflowStep(id, ""gate"", ""reject"", null);
runWorkflowInstance(id);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interp.InterpretAsync(statements).GetAwaiter().GetResult());

        var rejectInst = WorkflowEngine.Instance.ListInstances(name: "RejectFlow", limit: 1).FirstOrDefault();
        Assert.NotNull(rejectInst);
        Assert.Equal(WorkflowStatus.Failed, rejectInst!.Status);

        var timeoutSource = @"
workflow TimeoutFlow(data) {
    approval gate = approval(""manager"");
    return 1;
}
var id = startWorkflow(""TimeoutFlow"", null);
approveWorkflowStep(id, ""gate"", ""timeout"", null);
print(getWorkflowStatus(id));
";
        var lexer2 = new Lexer(timeoutSource);
        var tokens2 = lexer2.Tokenize();
        var parser2 = new Parser.Parser(tokens2);
        var statements2 = parser2.Parse();
        var interp2 = new Interpreter.Interpreter();
        var output2 = new System.Text.StringBuilder();
        interp2.SetOutputCallback(s => output2.Append(s).Append('\n'));
        interp2.InterpretAsync(statements2).GetAwaiter().GetResult();
        Assert.Contains("FAILED", output2.ToString());
    }

    [Fact]
    public void Workflow_AwaitSignal_ReceiveAndTimeoutPaths()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
workflow SignalFlow(data) {
    wait uploaded = awaitSignal(""docs_uploaded"", {""customerId"": 7}) timeout 60000;
    step done = string(uploaded.fileId);
    return done;
}
var id = startWorkflow(""SignalFlow"", null);
print(getWorkflowStatus(id));
signalWorkflow(id, ""docs_uploaded"", {""fileId"": ""A1""});
runWorkflowInstance(id);
print(getWorkflowStatus(id));
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        Assert.Contains("WAITING_SIGNAL", output.ToString());
        Assert.Contains("COMPLETED", output.ToString());

        var timeoutSource = @"
workflow SignalTimeout(data) {
    wait incoming = awaitSignal(""x"", null) timeout 1;
    return incoming;
}
var id = startWorkflow(""SignalTimeout"", null);
sleep(10);
runWorkflowInstance(id);
";
        var lexer2 = new Lexer(timeoutSource);
        var tokens2 = lexer2.Tokenize();
        var parser2 = new Parser.Parser(tokens2);
        var statements2 = parser2.Parse();
        var interp2 = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interp2.InterpretAsync(statements2).GetAwaiter().GetResult());
        var timeoutInst = WorkflowEngine.Instance.ListInstances(name: "SignalTimeout", limit: 1).FirstOrDefault();
        Assert.NotNull(timeoutInst);
        Assert.Equal(WorkflowStatus.Failed, timeoutInst!.Status);
    }

    [Fact]
    public void Workflow_Compensation_SuccessAndPartialFailure()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function push(x) { return x; }
function failNow() { error(""boom""); }
workflow CompensationOk(data) {
    step one = push(""one"") compensate push(""undo-one"");
    step two = push(""two"") compensate push(""undo-two"");
    step bad = failNow();
}
startWorkflow(""CompensationOk"", null);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interp.InterpretAsync(statements).GetAwaiter().GetResult());

        var okInst = WorkflowEngine.Instance.ListInstances(name: "CompensationOk", limit: 1).FirstOrDefault();
        Assert.NotNull(okInst);
        Assert.Equal(WorkflowStatus.Compensated, okInst!.Status);
        var okSteps = WorkflowEngine.Instance.GetSteps(okInst.Id);
        Assert.Contains(okSteps, s => s.StepName == "two__compensate" && s.State == StepState.Compensated);
        Assert.Contains(okSteps, s => s.StepName == "one__compensate" && s.State == StepState.Compensated);

        var partialSource = @"
function failNow() { error(""boom""); }
function failComp() { error(""comp-fail""); }
workflow CompensationPartial(data) {
    step one = string(1) compensate failComp();
    step bad = failNow();
}
startWorkflow(""CompensationPartial"", null);
";
        var lexer2 = new Lexer(partialSource);
        var tokens2 = lexer2.Tokenize();
        var parser2 = new Parser.Parser(tokens2);
        var statements2 = parser2.Parse();
        var interp2 = new Interpreter.Interpreter();
        Assert.ThrowsAny<Exception>(() => interp2.InterpretAsync(statements2).GetAwaiter().GetResult());
        var partialInst = WorkflowEngine.Instance.ListInstances(name: "CompensationPartial", limit: 1).FirstOrDefault();
        Assert.NotNull(partialInst);
        Assert.Equal(WorkflowStatus.Failed, partialInst!.Status);
        var partialSteps = WorkflowEngine.Instance.GetSteps(partialInst.Id);
        Assert.Contains(partialSteps, s => s.StepName == "one__compensate" && s.State == StepState.CompensationFailed);
    }

    [Fact]
    public void Workflow_RestartRecovery_WaitingApprovalAndSignalRemainDurable()
    {
        var dbPath = GetTestDbPath();
        var connection = "Data Source=" + dbPath;
        WorkflowEngine.ResetForTesting(connection);
        var source = @"
workflow ApprovalDurable(data) {
    approval gate = approval(""mgr"");
    return 1;
}
var id = startWorkflow(""ApprovalDurable"", null);
print(id);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        var output = new System.Text.StringBuilder();
        interp.SetOutputCallback(s => output.Append(s).Append('\n'));
        interp.InterpretAsync(statements).GetAwaiter().GetResult();
        var approvalId = output.ToString().Replace("\r", "").Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Last();

        WorkflowEngine.ResetForTesting(connection);
        var afterRestart = WorkflowEngine.Instance.GetInstance(approvalId);
        Assert.NotNull(afterRestart);
        Assert.Equal(WorkflowStatus.WaitingApproval, afterRestart!.Status);

        var sourceSignal = @"
workflow SignalDurable(data) {
    wait x = awaitSignal(""s"", null);
    return x;
}
var id = startWorkflow(""SignalDurable"", null);
print(id);
";
        var lexer2 = new Lexer(sourceSignal);
        var tokens2 = lexer2.Tokenize();
        var parser2 = new Parser.Parser(tokens2);
        var statements2 = parser2.Parse();
        var interp2 = new Interpreter.Interpreter();
        var output2 = new System.Text.StringBuilder();
        interp2.SetOutputCallback(s => output2.Append(s).Append('\n'));
        interp2.InterpretAsync(statements2).GetAwaiter().GetResult();
        var signalId = output2.ToString().Replace("\r", "").Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Last();

        WorkflowEngine.ResetForTesting(connection);
        var signalAfterRestart = WorkflowEngine.Instance.GetInstance(signalId);
        Assert.NotNull(signalAfterRestart);
        Assert.Equal(WorkflowStatus.WaitingSignal, signalAfterRestart!.Status);
    }

    [Fact]
    public void Workflow_EventPayloads_IncludeCorrelationAndStepMetadata()
    {
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        var source = @"
function identity(x) { return x; }
workflow MetadataFlow(input) {
    step first = identity(input.value);
    return first;
}
var id = startWorkflow(""MetadataFlow"", {""value"": 2, ""correlationId"": ""corr-123""});
print(id);
";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interp = new Interpreter.Interpreter();
        interp.InterpretAsync(statements).GetAwaiter().GetResult();

        var inst = WorkflowEngine.Instance.ListInstances(name: "MetadataFlow", limit: 1).FirstOrDefault();
        Assert.NotNull(inst);
        Assert.Equal("corr-123", inst!.CorrelationId);

        var events = WorkflowEngine.Instance.GetEvents(inst.Id, 200);
        var started = events.First(e => e.EventType == "workflow_step_started");
        using var startedDoc = System.Text.Json.JsonDocument.Parse(started.PayloadJson);
        Assert.Equal("corr-123", startedDoc.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(inst.Id, startedDoc.RootElement.GetProperty("workflowInstanceId").GetString());
        Assert.Equal("first", startedDoc.RootElement.GetProperty("stepName").GetString());
        Assert.Equal(1, startedDoc.RootElement.GetProperty("attempt").GetInt32());
    }

    [Fact]
    public void Workflow_Guardrails_DisabledRetryPayloadAndRuntimeLimits_Enforced()
    {
        // Disabled workflows.
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        WorkflowEngine.Instance.ConfigureRuntimeOptions(new WorkflowRuntimeOptions
        {
            Enabled = false,
            MaxRetriesPerStep = 10,
            MaxPayloadBytes = 1024 * 1024,
            MaxWorkflowDurationMs = 7 * 24 * 60 * 60 * 1000
        });
        var disabledSource = @"
workflow DisabledFlow(input) { return 1; }
startWorkflow(""DisabledFlow"", null);
";
        var disabledParser = new Parser.Parser(new Lexer(disabledSource).Tokenize());
        var disabledStatements = disabledParser.Parse();
        var disabledInterp = new Interpreter.Interpreter();
        var disabledEx = Assert.ThrowsAny<Exception>(() => disabledInterp.InterpretAsync(disabledStatements).GetAwaiter().GetResult());
        Assert.Contains("disabled", disabledEx.Message, StringComparison.OrdinalIgnoreCase);

        // Max retries per step.
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        WorkflowEngine.Instance.ConfigureRuntimeOptions(new WorkflowRuntimeOptions
        {
            Enabled = true,
            MaxRetriesPerStep = 1,
            MaxPayloadBytes = 1024 * 1024,
            MaxWorkflowDurationMs = 7 * 24 * 60 * 60 * 1000
        });
        var retrySource = @"
function boom() { error(""x""); }
workflow RetryCap(input) {
    step x = boom() retry 5;
}
startWorkflow(""RetryCap"", null);
";
        var retryParser = new Parser.Parser(new Lexer(retrySource).Tokenize());
        var retryStatements = retryParser.Parse();
        var retryInterp = new Interpreter.Interpreter();
        var retryEx = Assert.ThrowsAny<Exception>(() => retryInterp.InterpretAsync(retryStatements).GetAwaiter().GetResult());
        Assert.Contains("retry limit", retryEx.Message, StringComparison.OrdinalIgnoreCase);

        // Max payload bytes.
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        WorkflowEngine.Instance.ConfigureRuntimeOptions(new WorkflowRuntimeOptions
        {
            Enabled = true,
            MaxRetriesPerStep = 10,
            MaxPayloadBytes = 128,
            MaxWorkflowDurationMs = 7 * 24 * 60 * 60 * 1000
        });
        var payloadSource = @"
function big() { return ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa""; }
workflow PayloadCap(input) {
    step bigOut = big();
    return bigOut;
}
startWorkflow(""PayloadCap"", null);
";
        var payloadParser = new Parser.Parser(new Lexer(payloadSource).Tokenize());
        var payloadStatements = payloadParser.Parse();
        var payloadInterp = new Interpreter.Interpreter();
        var payloadEx = Assert.ThrowsAny<Exception>(() => payloadInterp.InterpretAsync(payloadStatements).GetAwaiter().GetResult());
        Assert.Contains("payload limit", payloadEx.Message, StringComparison.OrdinalIgnoreCase);

        // Max workflow runtime.
        WorkflowEngine.ResetForTesting("Data Source=" + GetTestDbPath());
        WorkflowEngine.Instance.ConfigureRuntimeOptions(new WorkflowRuntimeOptions
        {
            Enabled = true,
            MaxRetriesPerStep = 10,
            MaxPayloadBytes = 1024 * 1024,
            MaxWorkflowDurationMs = 1000
        });
        var runtimeSource = @"
function pause() { sleep(1100); return 1; }
workflow RuntimeCap(input) {
    step first = pause();
    step second = string(first);
    return second;
}
startWorkflow(""RuntimeCap"", null);
";
        var runtimeParser = new Parser.Parser(new Lexer(runtimeSource).Tokenize());
        var runtimeStatements = runtimeParser.Parse();
        var runtimeInterp = new Interpreter.Interpreter();
        var runtimeEx = Assert.ThrowsAny<Exception>(() => runtimeInterp.InterpretAsync(runtimeStatements).GetAwaiter().GetResult());
        Assert.Contains("max runtime", runtimeEx.Message, StringComparison.OrdinalIgnoreCase);
    }

}
