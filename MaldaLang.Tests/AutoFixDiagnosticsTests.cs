// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class AutoFixDiagnosticsTests
{
    [Fact]
    public void GetDiagnostics_MissingCloser_AttachesParserAutofix()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("print(\"hi\"\n");

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.AutoFix != null &&
            diagnostic.Source == "parser" &&
            !string.IsNullOrEmpty(diagnostic.AutoFix.TextToInsert));
    }

    [Fact]
    public void GetDiagnostics_TypeMismatch_DoesNotAttachParserAutofix()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = \"abc\";");

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Source == "malda-types" && diagnostic.AutoFix != null);
    }
}
