// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.PackageManager;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class WorkspacePackageResolverTests
{
    [Fact]
    public void TryResolveModulePath_FromRepoPackages_FindsMaldaTimeseries()
    {
        var repoPackages = PlanningPaths.ResolveRepoPath("packages");
        var previous = Environment.GetEnvironmentVariable("MALDA_PACKAGES_DIR");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", repoPackages);
            var path = WorkspacePackageResolver.TryResolveModulePath("malda-timeseries");
            Assert.NotNull(path);
            Assert.True(File.Exists(path!), path);
            Assert.EndsWith("timeseries.malda", path!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", previous);
        }
    }

    [Fact]
    public void ModuleResolver_ResolveModulePath_UsesWorkspaceWhenNotInstalled()
    {
        var repoPackages = PlanningPaths.ResolveRepoPath("packages");
        var previous = Environment.GetEnvironmentVariable("MALDA_PACKAGES_DIR");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", repoPackages);
            var storage = new PackageStorage(Path.Combine(Path.GetTempPath(), "malda_empty_pkgs_" + Guid.NewGuid().ToString("N")));
            var resolver = new ModuleResolver(storage);
            var path = resolver.ResolveModulePath("malda-timeseries");
            Assert.NotNull(path);
            Assert.EndsWith("timeseries.malda", path!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", previous);
        }
    }
}
