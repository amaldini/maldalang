// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphInterviewTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string InterviewModules(string workDir)
    {
        var root = RepoRoot().Replace("\\", "/");
        return $@"
include ""{root}/Examples/RalphWiggum/ralph/00-env.malda"";
include ""{root}/Examples/RalphWiggum/ralph/02-prd.malda"";
include ""{root}/Examples/RalphWiggum/ralph/08-interview.malda"";
var workDir = ""{workDir.Replace("\\", "/")}"";
";
    }

    [Fact]
    public void ValidateInterviewPrd_AcceptsSnakeDemoPrd()
    {
        var snakePrd = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "snake-demo", "PRD.md");
        var tempDir = CreateTempDirectory("ralph_int_");
        try
        {
            File.Copy(snakePrd, Path.Combine(tempDir, "PRD.md"));
            var source = InterviewModules(tempDir) + @"
var r = validateInterviewPrdStrict(pathJoin(workDir, ""PRD.md""));
print(string(r.ok));
print(string(r.progress.open));
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
            Assert.Contains("7", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateInterviewPrd_RejectsMissingAcceptance()
    {
        var tempDir = CreateTempDirectory("ralph_int_");
        try
        {
            var source = InterviewModules(tempDir) + @"
writeFile(pathJoin(workDir, ""PRD.md""), ""# Test\n\n- [TODO] [P0] **F1 — Item** (depends: none)\n"");
var r = validateInterviewPrdStrict(pathJoin(workDir, ""PRD.md""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateInterviewPrd_RejectsUnknownDependency()
    {
        var tempDir = CreateTempDirectory("ralph_int_");
        try
        {
            var source = InterviewModules(tempDir) + @"
writeFile(pathJoin(workDir, ""PRD.md""), ""# Test\n\n- [TODO] [P0] **F2 — Item** (depends: F9)\n  - Acceptance: works\n"");
var r = validateInterviewPrdStrict(pathJoin(workDir, ""PRD.md""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExtractPrdFeatureId_ParsesFeatureMarker()
    {
        var tempDir = CreateTempDirectory("ralph_int_");
        try
        {
            var source = InterviewModules(tempDir) + @"
print(extractPrdFeatureIdFromLine(""- [TODO] [P0] **F3 — Title** (depends: F1)""));
";
            var output = RunProgram(source);
            Assert.Contains("F3", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ResponseHasPrdReady_DetectsSignal()
    {
        var tempDir = CreateTempDirectory("ralph_int_");
        try
        {
            var source = InterviewModules(tempDir) + @"
print(string(responseHasPrdReady(""All done. PRD_READY"")));
print(string(responseHasPrdReady(""still working"")));
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void PrdAuthorOnly_DoesNotRegisterDiagnoseOrPlanTools()
    {
        var tempDir = CreateTempDirectory("ralph_prd_tools_");
        try
        {
            var client = new LLMClientInstance
            {
                ApiUrl = "https://example.com",
                ApiKey = "test",
                Model = "test"
            };
            var agent = new DevAgentInstance(
                "Prd",
                "author",
                "Write PRDs",
                client,
                tempDir,
                includeSymbols: false,
                readOnly: false,
                prdAuthorOnly: true);
            var convVal = agent.GetConversation();
            var conv = Assert.IsType<ConversationInstance>(convVal.AsObject());
            var toolsField = typeof(ConversationInstance).GetField("_tools", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(toolsField);
            var tools = Assert.IsType<Dictionary<string, ToolInstance>>(toolsField!.GetValue(conv));
            Assert.Contains("write_file", tools.Keys);
            Assert.Contains("ask_user", tools.Keys);
            Assert.Contains("glob", tools.Keys);
            Assert.DoesNotContain("check_malda", tools.Keys);
            Assert.DoesNotContain("validate_json", tools.Keys);
            Assert.DoesNotContain("web_search", tools.Keys);
            Assert.DoesNotContain("web_fetch", tools.Keys);
            Assert.DoesNotContain("test_malda", tools.Keys);
            Assert.DoesNotContain("update_plan", tools.Keys);
            Assert.DoesNotContain("git_commit", tools.Keys);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
