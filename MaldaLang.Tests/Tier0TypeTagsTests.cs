// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class Tier0TypeTagsTests
{
    [Theory]
    [InlineData("integer", "int")]
    [InlineData("boolean", "bool")]
    [InlineData("dictionary", "dict")]
    public void NormalizeToCanonical_MapsLegacyAliases(string legacy, string canonical)
    {
        Assert.Equal(canonical, Tier0TypeTags.NormalizeToCanonical(legacy));
    }

    [Fact]
    public void GetTag_DictionaryInstance_ReturnsDict()
    {
        var dict = RuntimeValue.Object(new DictionaryInstance(
            new Dictionary<string, RuntimeValue> { ["a"] = RuntimeValue.Integer(1) }));
        Assert.Equal("dict", Tier0TypeTags.GetTag(dict));
    }

    [Fact]
    public void MatchesTag_LegacyObjectAlias_DoesNotMatchDict()
    {
        var dict = RuntimeValue.Object(new DictionaryInstance());
        Assert.False(Tier0TypeTags.MatchesTag(Tier0TypeTags.GetTag(dict), "object"));
        Assert.True(Tier0TypeTags.MatchesTag(Tier0TypeTags.GetTag(dict), "dict"));
    }
}
