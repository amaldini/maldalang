// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Globalization;
using System.Threading;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Italian (and other comma-decimal) locales used to emit <c>0,5</c> into generated C#,
/// which fails with CS1001 and was masked by Spectre markup when packing Second Brain.
/// </summary>
[Collection("Sequential")]
public class TranspileFloatLiteralCultureTests : TestBase
{
    [Fact]
    public void TranspileFloatLiteral_UsesInvariantDecimalPoint_UnderItalianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var italian = CultureInfo.GetCultureInfo("it-IT");
            CultureInfo.CurrentCulture = italian;
            CultureInfo.CurrentUICulture = italian;
            Thread.CurrentThread.CurrentCulture = italian;
            Thread.CurrentThread.CurrentUICulture = italian;

            // Sanity: culture-sensitive formatting would use a comma.
            Assert.Equal("0,5", (0.5).ToString(CultureInfo.CurrentCulture));

            var csharp = new Compiler.Compiler().TranspileToCSharpFromSource(
                "var bestScore = 0.5;\n");

            Assert.Contains("0.5", csharp, StringComparison.Ordinal);
            Assert.DoesNotContain("0,5", csharp, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previous;
            Thread.CurrentThread.CurrentCulture = previous;
            Thread.CurrentThread.CurrentUICulture = previous;
        }
    }
}
