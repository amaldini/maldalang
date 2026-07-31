// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Tests;

public class WorkflowParserTests
{
    private static (List<MaldaLang.Parser.AST.Statements.Statement> statements, MaldaLang.Parser.Parser parser) Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens);
        var statements = parser.Parse();
        return (statements, parser);
    }

    [Fact]
    public void WorkflowDeclaration_ParsesSimpleWorkflow()
    {
        var source = @"
workflow OnboardCustomer(data) {
    step validated = validateInput(data);
    return validated;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        Assert.Equal("OnboardCustomer", wf.Name);
        Assert.Single(wf.Parameters);
        Assert.Equal("data", wf.Parameters[0]);
        Assert.Equal(2, wf.Body.Statements.Count);
    }

    [Fact]
    public void WorkflowDeclaration_AllowsInputKeywordAsParameterName()
    {
        var source = @"
workflow UsesInput(input) {
    return input;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        Assert.Single(wf.Parameters);
        Assert.Equal("input", wf.Parameters[0]);
    }

    [Fact]
    public void WorkflowDeclaration_ParsesReferenceExample()
    {
        var source = @"
workflow OnboardCustomer(data) {
    step validated = validateInput(data);

    step account = createAccount(validated)
        retry 3 backoff ""exponential"" delay 1000 maxDelay 30000
        timeout 120000
        compensate deleteAccount(account.id);

    approval approved = approval(""sales-manager"", {""accountId"": account.id})
        timeout 86400000
        onReject reject(""Approval rejected"");

    step workspace = provisionWorkspace(account.id)
        retry 5 backoff ""linear"" delay 2000
        timeout 180000
        compensate deprovisionWorkspace(workspace.id);

    step welcome = sendWelcomeEmail(data.email)
        retry 2 backoff ""fixed"" delay 5000;

    return {
        ""accountId"": account.id,
        ""workspaceId"": workspace.id,
        ""status"": ""onboarded""
    };
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        Assert.Equal("OnboardCustomer", wf.Name);
        Assert.Equal(6, wf.Body.Statements.Count);

        var step1 = Assert.IsType<WorkflowStepStatement>(wf.Body.Statements[0]);
        Assert.Equal("validated", step1.StepId);
        Assert.Null(step1.Options);

        var step2 = Assert.IsType<WorkflowStepStatement>(wf.Body.Statements[1]);
        Assert.Equal("account", step2.StepId);
        Assert.NotNull(step2.Options);
        Assert.Equal(3, step2.Options.RetryCount);
        Assert.Equal("exponential", step2.Options.Backoff);
        Assert.Equal(1000, step2.Options.DelayMs);
        Assert.Equal(30000, step2.Options.MaxDelayMs);
        Assert.Equal(120000, step2.Options.TimeoutMs);
        Assert.NotNull(step2.Options.Compensate);

        var approval = Assert.IsType<WorkflowApprovalStatement>(wf.Body.Statements[2]);
        Assert.Equal("approved", approval.ApprovalId);
        Assert.Equal(86400000, approval.TimeoutMs);
        Assert.NotNull(approval.OnReject);

        var step3 = Assert.IsType<WorkflowStepStatement>(wf.Body.Statements[3]);
        Assert.Equal("workspace", step3.StepId);
        Assert.Equal(5, step3.Options!.RetryCount);
        Assert.Equal("linear", step3.Options.Backoff);

        var step4 = Assert.IsType<WorkflowStepStatement>(wf.Body.Statements[4]);
        Assert.Equal("welcome", step4.StepId);
        Assert.Equal(2, step4.Options!.RetryCount);
        Assert.Equal("fixed", step4.Options.Backoff);
    }

    [Fact]
    public void WorkflowDeclaration_ParsesAwaitSignal()
    {
        var source = @"
workflow DocUpload(data) {
    wait documentUploaded = awaitSignal(""docs_uploaded"", {""customerId"": data.customerId})
        timeout 259200000;
    return documentUploaded;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        var waitStmt = Assert.IsType<WorkflowAwaitSignalStatement>(wf.Body.Statements[0]);
        Assert.Equal("documentUploaded", waitStmt.SignalId);
        Assert.Equal(259200000, waitStmt.TimeoutMs);
    }

    [Fact]
    public void WorkflowDeclaration_ApprovalWithoutPayload()
    {
        var source = @"
workflow Simple(data) {
    approval ok = approval(""manager"");
    return ok;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        var approval = Assert.IsType<WorkflowApprovalStatement>(wf.Body.Statements[0]);
        Assert.Equal("ok", approval.ApprovalId);
    }

    [Fact]
    public void WF1003_DuplicateStepId_ReportsError()
    {
        var source = @"
workflow Dup(data) {
    step a = foo();
    step a = bar();
}
";
        var (_, parser) = Parse(source);
        Assert.NotEmpty(parser.Errors);
        var wf1003 = parser.Errors.FirstOrDefault(e => e.DiagnosticCode == "WF1003");
        Assert.NotNull(wf1003);
        Assert.Contains("Duplicate step identifier", wf1003.Message);
    }

    [Fact]
    public void WF1004_BackoffWithoutRetry_ReportsError()
    {
        var source = @"
workflow Bad(data) {
    step a = foo() backoff ""exponential"";
}
";
        var (_, parser) = Parse(source);
        Assert.NotEmpty(parser.Errors);
        var wf1004 = parser.Errors.FirstOrDefault(e => e.DiagnosticCode == "WF1004");
        Assert.NotNull(wf1004);
        Assert.Contains("'backoff' requires 'retry'", wf1004.Message);
    }

    [Fact]
    public void WF1004_InvalidBackoffValue_ReportsError()
    {
        var source = @"
workflow Bad(data) {
    step a = foo() retry 3 backoff ""invalid"";
}
";
        var (_, parser) = Parse(source);
        Assert.NotEmpty(parser.Errors);
        var wf1004 = parser.Errors.FirstOrDefault(e => e.DiagnosticCode == "WF1004");
        Assert.NotNull(wf1004);
        Assert.Contains("Invalid backoff", wf1004.Message);
    }

    [Fact]
    public void WorkflowDeclaration_WithControlFlow()
    {
        var source = @"
workflow WithIf(data) {
    step x = validate(data);
    if (x.valid) {
        step y = process(x);
        return y;
    }
    return null;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        Assert.Equal(3, wf.Body.Statements.Count);
    }

    [Fact]
    public void WorkflowDeclaration_StepOptionsOrderFlexible()
    {
        var source = @"
workflow Flex(data) {
    step a = foo() timeout 5000 retry 2 compensate bar();
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);
        var wf = Assert.IsType<WorkflowDeclaration>(statements[0]);
        var step = Assert.IsType<WorkflowStepStatement>(wf.Body.Statements[0]);
        Assert.Equal(5000, step.Options!.TimeoutMs);
        Assert.Equal(2, step.Options.RetryCount);
        Assert.NotNull(step.Options.Compensate);
    }
}
