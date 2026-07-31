// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using System.Collections.Generic;
using System.IO;
using System;

namespace MaldaLang.Tests;

public class DependencyResolverTests
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
    
    private PackageMetadata CreateMetadata(string name, string version, Dictionary<string, string>? dependencies = null)
    {
        return new PackageMetadata
        {
            Name = name,
            Version = version,
            Dependencies = dependencies
        };
    }
    
    [Fact]
    public void ResolveDependencies_NoDependencies_ReturnsEmpty()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        var result = resolver.ResolveDependencies(new Dictionary<string, string>());
        
        Assert.Empty(result);
    }
    
    [Fact]
    public void ResolveDependencies_SingleDependency_ReturnsResolved()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        var dependencies = new Dictionary<string, string>
        {
            { "dep1", "1.0.0" }
        };
        
        var result = resolver.ResolveDependencies(dependencies);
        
        Assert.Single(result);
        Assert.True(result.ContainsKey("dep1"));
        Assert.True(result["dep1"].NeedsInstall);
        Assert.Equal("1.0.0", result["dep1"].VersionRange);
    }
    
    [Fact]
    public void ResolveDependencies_InstalledPackage_UsesInstalledVersion()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Install a package
        var metadata = CreateMetadata("dep1", "1.0.0");
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir);
        storage.InstallPackage("dep1", "1.0.0", sourceDir);
        storage.SavePackageMetadata("dep1", "1.0.0", metadata);
        
        var dependencies = new Dictionary<string, string>
        {
            { "dep1", "1.0.0" }
        };
        
        var result = resolver.ResolveDependencies(dependencies);
        
        Assert.Single(result);
        Assert.False(result["dep1"].NeedsInstall);
        Assert.Equal("1.0.0", result["dep1"].Version);
        Assert.NotNull(result["dep1"].Metadata);
        
        // Cleanup
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void ResolveDependencies_VersionRange_SelectsBestVersion()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Install multiple versions
        var metadata1 = CreateMetadata("dep1", "1.0.0");
        var metadata2 = CreateMetadata("dep1", "1.2.0");
        var metadata3 = CreateMetadata("dep1", "2.0.0");
        
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDir3 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        Directory.CreateDirectory(sourceDir3);
        
        storage.InstallPackage("dep1", "1.0.0", sourceDir1);
        storage.InstallPackage("dep1", "1.2.0", sourceDir2);
        storage.InstallPackage("dep1", "2.0.0", sourceDir3);
        storage.SavePackageMetadata("dep1", "1.0.0", metadata1);
        storage.SavePackageMetadata("dep1", "1.2.0", metadata2);
        storage.SavePackageMetadata("dep1", "2.0.0", metadata3);
        
        var dependencies = new Dictionary<string, string>
        {
            { "dep1", "^1.0.0" } // Should select 1.2.0 (highest matching)
        };
        
        var result = resolver.ResolveDependencies(dependencies);
        
        Assert.Single(result);
        Assert.False(result["dep1"].NeedsInstall);
        Assert.Equal("1.2.0", result["dep1"].Version);
        
        // Cleanup
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
        Directory.Delete(sourceDir3, true);
    }
    
    [Fact]
    public void ResolveDependencies_TransitiveDependencies_ResolvesAll()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Install transitive dependency
        var depMetadata = CreateMetadata("transitive", "1.0.0");
        var mainMetadata = CreateMetadata("main", "1.0.0", new Dictionary<string, string>
        {
            { "transitive", "1.0.0" }
        });
        
        var sourceDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDir1);
        Directory.CreateDirectory(sourceDir2);
        
        storage.InstallPackage("transitive", "1.0.0", sourceDir1);
        storage.InstallPackage("main", "1.0.0", sourceDir2);
        storage.SavePackageMetadata("transitive", "1.0.0", depMetadata);
        storage.SavePackageMetadata("main", "1.0.0", mainMetadata);
        
        var dependencies = new Dictionary<string, string>
        {
            { "main", "1.0.0" }
        };
        
        var result = resolver.ResolveDependencies(dependencies);
        
        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("main"));
        Assert.True(result.ContainsKey("transitive"));
        Assert.False(result["transitive"].NeedsInstall);
        
        // Cleanup
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
    
    [Fact]
    public void ResolveDependencies_CircularDependency_ThrowsException()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Create circular dependency: A -> B -> A
        var metadataA = CreateMetadata("A", "1.0.0", new Dictionary<string, string>
        {
            { "B", "1.0.0" }
        });
        var metadataB = CreateMetadata("B", "1.0.0", new Dictionary<string, string>
        {
            { "A", "1.0.0" }
        });
        
        var sourceDirA = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDirB = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDirA);
        Directory.CreateDirectory(sourceDirB);
        
        storage.InstallPackage("A", "1.0.0", sourceDirA);
        storage.InstallPackage("B", "1.0.0", sourceDirB);
        storage.SavePackageMetadata("A", "1.0.0", metadataA);
        storage.SavePackageMetadata("B", "1.0.0", metadataB);
        
        var dependencies = new Dictionary<string, string>
        {
            { "A", "1.0.0" }
        };
        
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveDependencies(dependencies));
        
        // Cleanup
        Directory.Delete(sourceDirA, true);
        Directory.Delete(sourceDirB, true);
    }
    
    [Fact]
    public void GetInstallOrder_ReturnsTopologicalOrder()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Create dependency graph: A -> B -> C, A -> D
        var metadataC = CreateMetadata("C", "1.0.0");
        var metadataD = CreateMetadata("D", "1.0.0");
        var metadataB = CreateMetadata("B", "1.0.0", new Dictionary<string, string>
        {
            { "C", "1.0.0" }
        });
        var metadataA = CreateMetadata("A", "1.0.0", new Dictionary<string, string>
        {
            { "B", "1.0.0" },
            { "D", "1.0.0" }
        });
        
        var sourceDirA = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDirB = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDirC = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDirD = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDirA);
        Directory.CreateDirectory(sourceDirB);
        Directory.CreateDirectory(sourceDirC);
        Directory.CreateDirectory(sourceDirD);
        
        storage.InstallPackage("A", "1.0.0", sourceDirA);
        storage.InstallPackage("B", "1.0.0", sourceDirB);
        storage.InstallPackage("C", "1.0.0", sourceDirC);
        storage.InstallPackage("D", "1.0.0", sourceDirD);
        storage.SavePackageMetadata("A", "1.0.0", metadataA);
        storage.SavePackageMetadata("B", "1.0.0", metadataB);
        storage.SavePackageMetadata("C", "1.0.0", metadataC);
        storage.SavePackageMetadata("D", "1.0.0", metadataD);
        
        var dependencies = new Dictionary<string, string>
        {
            { "A", "1.0.0" }
        };
        
        resolver.ResolveDependencies(dependencies);
        var order = resolver.GetInstallOrder();
        
        // C and D should come before B and A
        var indexC = order.IndexOf("C");
        var indexD = order.IndexOf("D");
        var indexB = order.IndexOf("B");
        var indexA = order.IndexOf("A");
        
        Assert.True(indexC < indexB);
        Assert.True(indexD < indexA);
        Assert.True(indexB < indexA);
        
        // Cleanup
        Directory.Delete(sourceDirA, true);
        Directory.Delete(sourceDirB, true);
        Directory.Delete(sourceDirC, true);
        Directory.Delete(sourceDirD, true);
    }
    
    [Fact]
    public void ResolveDependencies_AlreadyResolvedCompatibleVersion_SkipsResolution()
    {
        var storage = CreateTestStorage();
        var registry = CreateTestRegistry(storage);
        var resolver = new DependencyResolver(registry, storage);
        
        // Install dep1 version 1.2.0
        var depMetadata = CreateMetadata("dep1", "1.2.0");
        var depSourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(depSourceDir);
        storage.InstallPackage("dep1", "1.2.0", depSourceDir);
        storage.SavePackageMetadata("dep1", "1.2.0", depMetadata);
        
        // Create two packages that both depend on dep1 with different version ranges
        var pkg1Metadata = CreateMetadata("pkg1", "1.0.0", new Dictionary<string, string>
        {
            { "dep1", "^1.0.0" }
        });
        var pkg2Metadata = CreateMetadata("pkg2", "1.0.0", new Dictionary<string, string>
        {
            { "dep1", "^1.1.0" } // Same package, different range
        });
        
        var pkg1SourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var pkg2SourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(pkg1SourceDir);
        Directory.CreateDirectory(pkg2SourceDir);
        storage.InstallPackage("pkg1", "1.0.0", pkg1SourceDir);
        storage.InstallPackage("pkg2", "1.0.0", pkg2SourceDir);
        storage.SavePackageMetadata("pkg1", "1.0.0", pkg1Metadata);
        storage.SavePackageMetadata("pkg2", "1.0.0", pkg2Metadata);
        
        // Resolve dependencies for both packages
        var dependencies = new Dictionary<string, string>
        {
            { "pkg1", "1.0.0" },
            { "pkg2", "1.0.0" }
        };
        
        var result = resolver.ResolveDependencies(dependencies);
        
        // Should resolve both packages and dep1 only once (even though requested with different ranges)
        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey("pkg1"));
        Assert.True(result.ContainsKey("pkg2"));
        Assert.True(result.ContainsKey("dep1"));
        Assert.Equal("1.2.0", result["dep1"].Version);
        
        // Cleanup
        Directory.Delete(depSourceDir, true);
        Directory.Delete(pkg1SourceDir, true);
        Directory.Delete(pkg2SourceDir, true);
    }
}
