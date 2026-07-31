// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspilerEmbedBuiltInTests
{
    [Fact]
    public void EmbedHash_IsTranspilerSupported()
    {
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("embedHash"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("embedBagOfWords"));
        var descriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("embedHash"));
        Assert.True(descriptor.IsAlwaysSynchronousForCodegen);
    }

    [Fact]
    public void TranspiledEmbedHash_ReturnsNormalizedVector()
    {
        var source = @"
var vec = embedHash(""hello world"", 8);
print(string(length(vec)));
var sum = 0.0;
var i = 0;
while (i < length(vec)) {
    sum = sum + (vec[i] * vec[i]);
    i = i + 1;
}
print(string(sum > 0.9 && sum < 1.1));
";
        var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
        Assert.Contains("8", output);
        Assert.Contains("true", output);
    }

    [Fact]
    public void TranspiledEmbedBagOfWords_ReturnsVectorOfRequestedDimension()
    {
        var source = @"
var vec = embedBagOfWords(""hello world"", 16);
print(string(length(vec)));
";
        var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
        Assert.Contains("16", output);
    }
}
