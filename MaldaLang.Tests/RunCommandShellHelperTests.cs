// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class RunCommandShellHelperTests
{
    [Fact]
    public void ValidateAndNormalize_CmdDashC_BecomesSlashC()
    {
        var args = new List<string> { "-c", "echo hi" };
        var error = RunCommandShellHelper.ValidateAndNormalize("cmd", args);
        Assert.Null(error);
        Assert.Equal("/c", args[0]);
    }

    [Fact]
    public void ValidateAndNormalize_CmdWithoutArgs_Blocked()
    {
        var error = RunCommandShellHelper.ValidateAndNormalize("cmd", new List<string>());
        Assert.NotNull(error);
        var stderr = error!.AsObject().Get("stderr").AsString();
        Assert.Contains("blocked", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveTimeoutMs_ShellGetsDefaultWhenUnset()
    {
        var ms = RunCommandShellHelper.ResolveTimeoutMs("cmd", null);
        Assert.Equal(120_000, ms);
    }
}
