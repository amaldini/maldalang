// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Xml.Linq;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Phase 1.1: <see cref="MaldaLang.Compiler"/> must not compile against vertical pack assemblies.
/// Optional-pack transpile emits type names as strings; generated programs reference pack DLLs at publish time.
/// </summary>
public class CompilerPackDecouplingGuardTests
{
    private static readonly string[] ForbiddenProjectReferences =
    [
        "MaldaLang.Trading.Core",
        "MaldaLang.Timeseries",
        "MaldaLang.Trading.Plugin"
    ];

    [Fact]
    public void CompilerCsproj_DoesNotReferenceOptionalPackProjects()
    {
        var csprojPath = PlanningPaths.ResolveRepoFile("MaldaLang.Compiler", "MaldaLang.Compiler.csproj");
        var doc = XDocument.Load(csprojPath);
        var references = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        foreach (var forbidden in ForbiddenProjectReferences)
        {
            Assert.DoesNotContain(references, r =>
                r.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
