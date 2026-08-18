// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Conformance.Tier0;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7-style helper for JavaScript: same <c>.malda</c>, same normalized stdout on
/// interpret and <c>--mode js</c> (Node + <c>malda-js-runtime.js</c>).
/// </summary>
public static class InterpretJsPair
{
    public static string Normalize(string text) => InterpretTranspilePair.Normalize(text);

    public static void AssertSameFromSource(string source, string label)
    {
        SkipIfJavaScriptUnavailable();
        var interpreted = TestBase.CaptureInterpretAsync(source).GetAwaiter().GetResult();
        string js;
        try
        {
            js = new Compiler.Compiler().TranspileToJavaScriptFromSource(source);
        }
        catch (Exception ex)
        {
            throw new Exception($"{label}: JS transpile failed.{Environment.NewLine}{ex.Message}", ex);
        }

        var compiled = Tier0JavaScriptRunner.RunCompiledJavaScriptAsync(js).GetAwaiter().GetResult();
        Assert.Equal(Normalize(interpreted), Normalize(compiled));
    }

    public static void AssertSameFromFile(string sourcePath, string label)
    {
        SkipIfJavaScriptUnavailable();
        Assert.True(File.Exists(sourcePath), $"Missing pair source: {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        var interpreted = TestBase.CaptureInterpretAsync(source, sourcePath).GetAwaiter().GetResult();

        string js;
        try
        {
            js = new Compiler.Compiler().TranspileToJavaScript(sourcePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"{label}: JS transpile failed.{Environment.NewLine}{ex.Message}", ex);
        }

        var compiled = Tier0JavaScriptRunner.RunCompiledJavaScriptAsync(js).GetAwaiter().GetResult();
        Assert.Equal(Normalize(interpreted), Normalize(compiled));
    }

    public static void SkipIfJavaScriptUnavailable()
    {
        Assert.True(
            Tier0JavaScriptRunner.IsAvailable(out var reason),
            "JavaScript backend unavailable: " + reason);
    }
}
