// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class LanguageServiceParseCacheTests
{
    [Fact]
    public void GetCompletions_MathDot_OffersStdLibMembersWithoutUserTypes()
    {
        var service = new LanguageService();
        var source = "print(math.)";
        var completions = service.GetCompletions(source, 0, source.IndexOf('.') + 1);

        Assert.Contains(completions, item => item.Label == "sqrt");
        Assert.Contains(completions, item => item.Label == "clamp");
    }

    [Fact]
    public void GetSignatureHelp_AfterGetCompletions_StillResolvesBuiltin()
    {
        var service = new LanguageService();
        var source = "print(";
        _ = service.GetCompletions(source, 0, source.Length);
        var help = service.GetSignatureHelp(source, 0, source.Length);

        Assert.NotNull(help);
        Assert.Contains("value", help!.Parameters);
    }
}
