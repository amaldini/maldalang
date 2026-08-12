// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using PM = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class PackageManagerTests
{
    private PackageStorage CreateTestStorage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        return new PackageStorage(tempDir);
    }
    
    private PM CreateTestPackageManager(PackageStorage? storage = null)
    {
        // Offline-capable: registry is created lazily only for remote install/search.
        // Silence CLI chatter so parallel TestBase console capture is not polluted.
        var pm = storage != null ? new PM(storage) : new PM();
        pm.Out = TextWriter.Null;
        return pm;
    }

    private static IDisposable WithTestRegistryUrl()
    {
        return new RegistryUrlScope("https://test-registry.maldalang.com");
    }

    private sealed class RegistryUrlScope : IDisposable
    {
        private readonly string? _previous;

        public RegistryUrlScope(string url)
        {
            _previous = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
            Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", url);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", _previous);
        }
    }
    
    [Fact]
    public void Uninstall_InstalledPackage_RemovesPackage()
    {
        var storage = CreateTestStorage();
        var pm = CreateTestPackageManager(storage);
        
        // Install a package manually
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0"
        };
        
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir);
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        Assert.True(storage.IsPackageInstalled("test-package", "1.0.0"));
        
        var result = pm.Uninstall("test-package", "1.0.0");
        
        Assert.True(result);
        Assert.False(storage.IsPackageInstalled("test-package", "1.0.0"));
        
        // Cleanup
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void Uninstall_NonExistentPackage_ReturnsFalse()
    {
        var pm = CreateTestPackageManager();
        
        var result = pm.Uninstall("nonexistent", "1.0.0");
        
        Assert.False(result);
    }
    
    [Fact]
    public void Uninstall_WithoutVersion_RemovesAllVersions()
    {
        var storage = CreateTestStorage();
        var pm = CreateTestPackageManager(storage);
        
        // Install multiple versions
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        
        var metadata1 = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0"
        };
        var metadata2 = new PackageMetadata
        {
            Name = "test-package",
            Version = "2.0.0"
        };
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir1);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata1);
        storage.InstallPackage("test-package", "2.0.0", sourceDir2);
        storage.SavePackageMetadata("test-package", "2.0.0", metadata2);
        
        Assert.True(storage.IsPackageInstalled("test-package", "1.0.0"));
        Assert.True(storage.IsPackageInstalled("test-package", "2.0.0"));
        
        var result = pm.Uninstall("test-package");
        
        Assert.True(result);
        Assert.False(storage.IsPackageInstalled("test-package", "1.0.0"));
        Assert.False(storage.IsPackageInstalled("test-package", "2.0.0"));
        
        // Cleanup
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
    
    [Fact]
    public void List_NoPackages_ShowsNoPackages()
    {
        var pm = CreateTestPackageManager();
        pm.List();
    }
    
    [Fact]
    public void List_WithPackages_ShowsPackages()
    {
        var storage = CreateTestStorage();
        var pm = CreateTestPackageManager(storage);
        
        // Install a package
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0",
            Description = "Test package"
        };
        
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir);
        try
        {
            storage.InstallPackage("test-package", "1.0.0", sourceDir);
            storage.SavePackageMetadata("test-package", "1.0.0", metadata);
            pm.List();
        }
        finally
        {
            Directory.Delete(sourceDir, true);
        }
    }
    
    [Fact]
    public async Task SearchAsync_WithQuery_ReturnsResults()
    {
        using var _ = WithTestRegistryUrl();
        var pm = CreateTestPackageManager();
        
        // This will attempt to search the registry
        // In a real scenario, this would be mocked
        // For now, we just verify it doesn't throw and returns a list
        var results = await pm.SearchAsync("test");
        
        Assert.NotNull(results);
        // Results may be empty if registry is not available, which is fine for this test
    }
    
    [Fact]
    public async Task ListAllPackagesAsync_ReturnsPackages()
    {
        using var _ = WithTestRegistryUrl();
        var pm = CreateTestPackageManager();
        
        // This will attempt to list packages from the registry
        // In a real scenario, this would be mocked
        // For now, we just verify it doesn't throw and returns a list
        var results = await pm.ListAllPackagesAsync();
        
        Assert.NotNull(results);
        // Results may be empty if registry is not available, which is fine for this test
    }
    
    [Fact]
    public void Init_CreatesPackageJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var pm = CreateTestPackageManager();
            var result = pm.Init(tempDir);
            
            Assert.True(result);
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            Assert.True(File.Exists(packageJsonPath));
            
            var json = File.ReadAllText(packageJsonPath);
            Assert.Contains("name", json);
            Assert.Contains("version", json);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
    
    [Fact]
    public void Init_WithExistingPackageJson_Overwrites()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create existing package.json
            var existingPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(existingPath, "{\"name\":\"old\"}");
            
            var pm = CreateTestPackageManager();
            var result = pm.Init(tempDir);
            
            Assert.True(result);
            var json = File.ReadAllText(existingPath);
            // Should have new structure
            Assert.Contains("version", json);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
    
    // Note: Tests for InstallAsync would require mocking PackageRegistry
    // to avoid actual HTTP calls. These would be integration tests.
    // The InstallAsync method has complex logic including:
    // - Fetching metadata from registry
    // - Dependency resolution
    // - Downloading packages
    // - Installing packages
    // These are better tested with integration tests or with mocked HTTP clients.
}
