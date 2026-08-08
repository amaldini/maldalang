// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using Xunit;

namespace MaldaLang.Tests;

public class ExampleProgramsOfflineFilterTests
{
    [Fact]
    public void IsOfflineFriendly_EmptyRequires_IsTrue()
    {
        var example = new ExampleProgram { Name = "x", Requires = new List<string>() };
        Assert.True(ExampleProgramsService.IsOfflineFriendly(example));
    }

    [Fact]
    public void IsOfflineFriendly_OfflineOnly_IsTrue()
    {
        var example = new ExampleProgram { Name = "x", Requires = new List<string> { "offline" } };
        Assert.True(ExampleProgramsService.IsOfflineFriendly(example));
    }

    [Fact]
    public void IsOfflineFriendly_ApiKey_IsFalse()
    {
        var example = new ExampleProgram { Name = "x", Requires = new List<string> { "api-key" } };
        Assert.False(ExampleProgramsService.IsOfflineFriendly(example));
    }
}
