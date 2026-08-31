// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Anti-drift guard: the fixed WF1001/WF1002 deny-list must stay in sync with
/// <see cref="BuiltInRegistry.WorkflowNonDeterministicBuiltIns"/> /
/// <see cref="BuiltInRegistry.WorkflowSideEffectingBuiltIns"/>.
/// </summary>
public class WorkflowDeterminismDenyListTests
{
    private static readonly string[] ExpectedNonDeterministic =
    [
        "now",
        "random",
        "randomInt",
        "randomFloat",
        "randomChoiceWeighted",
        "randn",
        "sleep"
    ];

    private static readonly string[] ExpectedSideEffecting =
    [
        "runCommand",
        "writeFile",
        "copyFile",
        "replaceInFile",
        "editFile",
        "deleteFile",
        "runMALDA",
        "compileMALDA",
        "httpGet",
        "httpPost",
        "httpPut",
        "httpDelete",
        "httpPatch"
    ];

    [Fact]
    public void DenyList_MatchesRegistryPublicLists()
    {
        Assert.Equal(ExpectedNonDeterministic, BuiltInRegistry.WorkflowNonDeterministicBuiltIns);
        Assert.Equal(ExpectedSideEffecting, BuiltInRegistry.WorkflowSideEffectingBuiltIns);
    }

    [Fact]
    public void GetWorkflowBehavior_MatchesDenyLists()
    {
        foreach (var name in ExpectedNonDeterministic)
        {
            Assert.Equal(
                WorkflowBuiltInBehavior.NonDeterministic,
                BuiltInRegistry.GetWorkflowBehavior(name));
        }

        foreach (var name in ExpectedSideEffecting)
        {
            Assert.Equal(
                WorkflowBuiltInBehavior.SideEffecting,
                BuiltInRegistry.GetWorkflowBehavior(name));
        }

        Assert.Equal(
            WorkflowBuiltInBehavior.Deterministic,
            BuiltInRegistry.GetWorkflowBehavior("print"));
        Assert.Equal(
            WorkflowBuiltInBehavior.Deterministic,
            BuiltInRegistry.GetWorkflowBehavior("readFile"));
        Assert.Equal(
            WorkflowBuiltInBehavior.Deterministic,
            BuiltInRegistry.GetWorkflowBehavior("getEnv"));
    }

    [Fact]
    public void DenyList_CountIsTwenty()
    {
        // Documented in Reference Manual Determinism Boundary (7 WF1001 + 13 WF1002).
        Assert.Equal(7, ExpectedNonDeterministic.Length);
        Assert.Equal(13, ExpectedSideEffecting.Length);
        Assert.Equal(20, ExpectedNonDeterministic.Length + ExpectedSideEffecting.Length);
    }
}
