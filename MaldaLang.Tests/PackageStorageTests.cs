// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using System.IO;
using System.Text.Json;

namespace MaldaLang.Tests;

public class PackageStorageTests
{
    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }
    
    [Fact]
    public void GetPackagePath_ReturnsCorrectPath()
    {
        var storage = new PackageStorage(CreateTempDirectory());
        var path = storage.GetPackagePath("test-package", "1.0.0");
        Assert.EndsWith(Path.Combine("test-package", "1.0.0"), path);
    }
    
    [Fact]
    public void GetPackageJsonPath_ReturnsCorrectPath()
    {
        var storage = new PackageStorage(CreateTempDirectory());
        var path = storage.GetPackageJsonPath("test-package", "1.0.0");
        Assert.EndsWith(Path.Combine("test-package", "1.0.0", "package.json"), path);
    }
    
    [Fact]
    public void IsPackageInstalled_NotInstalled_ReturnsFalse()
    {
        var storage = new PackageStorage(CreateTempDirectory());
        Assert.False(storage.IsPackageInstalled("nonexistent", "1.0.0"));
    }
    
    [Fact]
    public void InstallPackage_CreatesPackageDirectory()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir = CreateTempDirectory();
        
        // Create a test file in source
        File.WriteAllText(Path.Combine(sourceDir, "test.txt"), "test content");
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        
        var packagePath = storage.GetPackagePath("test-package", "1.0.0");
        Assert.True(Directory.Exists(packagePath));
        Assert.True(File.Exists(Path.Combine(packagePath, "test.txt")));
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void SavePackageMetadata_CreatesMetadataFile()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0",
            Description = "Test package"
        };
        
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        var metadataPath = storage.GetPackageJsonPath("test-package", "1.0.0");
        Assert.True(File.Exists(metadataPath));
        
        var json = File.ReadAllText(metadataPath);
        var loaded = JsonSerializer.Deserialize<PackageMetadata>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        Assert.NotNull(loaded);
        Assert.Equal("test-package", loaded!.Name);
        Assert.Equal("1.0.0", loaded.Version);
        Assert.Equal("Test package", loaded.Description);
        
        // Cleanup
        Directory.Delete(storageDir, true);
    }
    
    [Fact]
    public void LoadPackageMetadata_ExistingPackage_ReturnsMetadata()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0",
            Description = "Test package"
        };
        
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        var loaded = storage.LoadPackageMetadata("test-package", "1.0.0");
        Assert.NotNull(loaded);
        Assert.Equal("test-package", loaded!.Name);
        Assert.Equal("1.0.0", loaded.Version);
        Assert.Equal("Test package", loaded.Description);
        
        // Cleanup
        Directory.Delete(storageDir, true);
    }
    
    [Fact]
    public void LoadPackageMetadata_NonExistentPackage_ReturnsNull()
    {
        var storage = new PackageStorage(CreateTempDirectory());
        var loaded = storage.LoadPackageMetadata("nonexistent", "1.0.0");
        Assert.Null(loaded);
    }
    
    [Fact]
    public void IsPackageInstalled_AfterInstall_ReturnsTrue()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir = CreateTempDirectory();
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0"
        };
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        
        Assert.True(storage.IsPackageInstalled("test-package", "1.0.0"));
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void UninstallPackage_RemovesPackageDirectory()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir = CreateTempDirectory();
        
        var metadata = new PackageMetadata
        {
            Name = "test-package",
            Version = "1.0.0"
        };
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        storage.SavePackageMetadata("test-package", "1.0.0", metadata);
        Assert.True(storage.IsPackageInstalled("test-package", "1.0.0"));
        
        storage.UninstallPackage("test-package", "1.0.0");
        Assert.False(storage.IsPackageInstalled("test-package", "1.0.0"));
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void GetInstalledVersions_ReturnsCorrectVersions()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir1 = CreateTempDirectory();
        var sourceDir2 = CreateTempDirectory();
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir1);
        storage.InstallPackage("test-package", "2.0.0", sourceDir2);
        
        var versions = storage.GetInstalledVersions("test-package");
        Assert.Contains("1.0.0", versions);
        Assert.Contains("2.0.0", versions);
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
    
    [Fact]
    public void GetInstalledPackages_ReturnsCorrectPackages()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir1 = CreateTempDirectory();
        var sourceDir2 = CreateTempDirectory();
        
        storage.InstallPackage("package1", "1.0.0", sourceDir1);
        storage.InstallPackage("package2", "1.0.0", sourceDir2);
        
        var packages = storage.GetInstalledPackages();
        Assert.Contains("package1", packages);
        Assert.Contains("package2", packages);
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
    
    [Fact]
    public void TryReadPackageFile_ExistingFile_ReturnsContent()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir = CreateTempDirectory();
        
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "Package readme");
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        
        var result = storage.TryReadPackageFile("test-package", "1.0.0", "readme.txt", out var content);
        
        Assert.True(result);
        Assert.Equal("Package readme", content);
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void TryReadPackageFile_NonExistentFile_ReturnsFalse()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir = CreateTempDirectory();
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir);
        
        var result = storage.TryReadPackageFile("test-package", "1.0.0", "nonexistent.txt", out var content);
        
        Assert.False(result);
        Assert.Null(content);
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir, true);
    }
    
    [Fact]
    public void InstallPackage_OverwritesExistingInstallation()
    {
        var storageDir = CreateTempDirectory();
        var storage = new PackageStorage(storageDir);
        var sourceDir1 = CreateTempDirectory();
        var sourceDir2 = CreateTempDirectory();
        
        File.WriteAllText(Path.Combine(sourceDir1, "file1.txt"), "version 1");
        File.WriteAllText(Path.Combine(sourceDir2, "file2.txt"), "version 2");
        
        storage.InstallPackage("test-package", "1.0.0", sourceDir1);
        storage.InstallPackage("test-package", "1.0.0", sourceDir2);
        
        var packagePath = storage.GetPackagePath("test-package", "1.0.0");
        Assert.False(File.Exists(Path.Combine(packagePath, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(packagePath, "file2.txt")));
        
        // Cleanup
        Directory.Delete(storageDir, true);
        Directory.Delete(sourceDir1, true);
        Directory.Delete(sourceDir2, true);
    }
}
