// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MaldaLang.Tests;

public class PackageRegistryTests
{
    private PackageStorage CreateTestStorage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        return new PackageStorage(tempDir);
    }
    
    private PackageRegistry CreateTestRegistry(PackageStorage? storage = null)
    {
        // Set test registry URL for tests (registry reads it in constructor)
        var originalValue = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", "https://test-registry.maldalang.com");
        try
        {
            return new PackageRegistry(storage ?? CreateTestStorage());
        }
        finally
        {
            // Restore original value
            if (originalValue != null)
            {
                Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", originalValue);
            }
            else
            {
                Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", null);
            }
        }
    }
    
    [Fact]
    public void PackageRegistry_WithoutEnvironmentVariable_ThrowsException()
    {
        var storage = CreateTestStorage();
        
        // Ensure environment variable is not set
        var originalValue = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", null);
        
        try
        {
            Assert.Throws<InvalidOperationException>(() => new PackageRegistry(storage));
        }
        finally
        {
            // Restore original value
            if (originalValue != null)
            {
                Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", originalValue);
            }
        }
    }
    
    [Fact]
    public void GetPackageMetadata_FromLocalStorage_ReturnsMetadata()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0",
            Description = "Test package"
        };
        
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir);
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        var result = registry.GetPackageMetadata("test-package", "1.0.0");
        
        Assert.NotNull(result);
        Assert.Equal("test-package", result!.Name);
        Assert.Equal("1.0.0", result.Version);
        
        // Cleanup
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void GetPackageMetadata_NonExistentPackage_ReturnsNull()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        
        var result = registry.GetPackageMetadata("nonexistent", "1.0.0");
        
        Assert.Null(result);
    }
    
    [Fact]
    public void GetPackageMetadata_WithoutVersion_ReturnsLatestInstalled()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        
        var metadata1 = new PackageMetadata { Name = "test-package", Version = "1.0.0" };
        var metadata2 = new PackageMetadata { Name = "test-package", Version = "2.0.0" };
        
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir1);
        storage.InstallPackage("test-package", "2.0.0", sourceDir2);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata1);
        storage.SavePackageMetadata("test-package", "2.0.0", metadata2);
        
        var result = registry.GetPackageMetadata("test-package");
        
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result!.Version); // Should return latest
        
        // Cleanup
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
    
    [Fact]
    public void ClearCache_ClearsCachedMetadata()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0"
        };
        
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir);
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        // Get metadata (should cache it)
        var result1 = registry.GetPackageMetadata("test-package", "1.0.0");
        Assert.NotNull(result1);
        
        // Clear cache
        registry.ClearCache();
        
        // Should still work (reads from storage)
        var result2 = registry.GetPackageMetadata("test-package", "1.0.0");
        Assert.NotNull(result2);
        
        // Cleanup
        Directory.Delete(sourceDir, true);
    }
    
    // Note: Tests for FetchPackageMetadataAsync, DownloadPackageAsync, SearchPackagesAsync, 
    // and ListAllPackagesAsync would require HTTP mocking or a test server.
    // These are integration tests that would need additional setup.
}
