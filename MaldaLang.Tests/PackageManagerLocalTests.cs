// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using MaldaLang.PackageManager;
using MaldaLang.Tests.Planning;
using Xunit;
using PM = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class PackageManagerLocalTests
{
    [Fact]
    public void Init_And_List_Work_Without_RegistryUrl()
    {
        var original = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", null);
        var storageDir = Path.Combine(Path.GetTempPath(), "malda_pm_local_" + Guid.NewGuid().ToString("N"));
        var initDir = Path.Combine(Path.GetTempPath(), "malda_pm_init_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDir);
        Directory.CreateDirectory(initDir);
        try
        {
            var pm = new PM(new PackageStorage(storageDir)) { Out = TextWriter.Null };
            Assert.True(pm.Init(initDir));
            Assert.True(File.Exists(Path.Combine(initDir, "package.json")));
            pm.List(); // must not throw
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", original);
            TryDelete(storageDir);
            TryDelete(initDir);
        }
    }

    [Fact]
    public void InstallFromPath_CopiesIntoStorage()
    {
        var original = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", null);
        var storageDir = Path.Combine(Path.GetTempPath(), "malda_pm_install_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(Path.GetTempPath(), "malda_pm_src_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDir);
        Directory.CreateDirectory(sourceDir);
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "package.json"),
                """
                {
                  "name": "local-hello",
                  "version": "1.2.3",
                  "description": "from path",
                  "main": "main.malda"
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "main.malda"), "export function hi() { return 1; }\n");

            var storage = new PackageStorage(storageDir);
            var pm = new PM(storage) { Out = TextWriter.Null };
            Assert.True(pm.InstallFromPath(sourceDir));
            Assert.True(storage.IsPackageInstalled("local-hello", "1.2.3"));
            Assert.True(File.Exists(Path.Combine(storage.GetPackagePath("local-hello", "1.2.3"), "main.malda")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", original);
            TryDelete(storageDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void ListWorkspace_FindsRepoDemoMath()
    {
        var repoPackages = PlanningPaths.ResolveRepoPath("packages");
        var previous = Environment.GetEnvironmentVariable("MALDA_PACKAGES_DIR");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", repoPackages);
            var listed = WorkspacePackageResolver.ListWorkspacePackages();
            Assert.Contains(listed, p =>
                p.Name.Equals("malda-demo-math", StringComparison.OrdinalIgnoreCase) &&
                p.EntryPath.EndsWith("demo-math.malda", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", previous);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup
        }
    }
}
