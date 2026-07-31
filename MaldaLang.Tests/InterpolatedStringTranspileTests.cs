// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpolatedStringTranspileTests : TestBase
{
    [Fact]
    public void TranspileInterpolatedString_ReEscapesNewlinesAndTabs()
    {
        var source = """
            var x = 1;
            io.print($"line one {x}\nline two\tend");
            """;

        var csharp = new Compiler.Compiler().TranspileToCSharpFromSource(source);

        // Lexer-decoded \n/\t must be re-escaped into the C# source (same as plain strings).
        Assert.Contains("\\nline two\\tend", csharp, StringComparison.Ordinal);
        Assert.DoesNotContain("line one {\r\n", csharp, StringComparison.Ordinal);
        Assert.DoesNotContain("line one {\n", csharp, StringComparison.Ordinal);
    }
}
