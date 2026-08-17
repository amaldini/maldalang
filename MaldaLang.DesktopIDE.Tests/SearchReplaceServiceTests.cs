// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class SearchReplaceServiceTests
{
    [Fact]
    public void ReplaceAll_AppliesMatchesInReverseOffsetOrder()
    {
        const string source = "aa-bb-aa";
        var matches = new[]
        {
            new SearchMatch(0, 2),
            new SearchMatch(6, 2)
        };

        var replaced = SearchReplaceService.ReplaceAll(source, matches, "xx");

        Assert.Equal("xx-bb-xx", replaced);
    }

    [Fact]
    public void ReplaceAt_ReplacesSingleMatch()
    {
        var replaced = SearchReplaceService.ReplaceAt("hello world", new SearchMatch(6, 5), "MALDA");
        Assert.Equal("hello MALDA", replaced);
    }
}
