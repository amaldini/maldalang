// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MaldaLang.PackageManager.Models;

public class PackageStorage
{
    private readonly string _packagesDirectory;
    private readonly Assembly? _embeddedResourceAssembly;
    
    public PackageStorage()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _packagesDirectory = Path.Combine(userProfile, ".maldalang", "packages");
        Directory.CreateDirectory(_packagesDirectory);
        
        // Try to find embedded resources in the executing assembly (for transpiled executables)
        try
        {
            _embeddedResourceAssembly = Assembly.GetExecutingAssembly();
        }
        catch
        {
            _embeddedResourceAssembly = null;
        }
    }
    
    public PackageStorage(string packagesDirectory)
    {
        _packagesDirectory = packagesDirectory;
        Directory.CreateDirectory(_packagesDirectory);
        _embeddedResourceAssembly = null;
    }
    
    public PackageStorage(Assembly? embeddedResourceAssembly)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _packagesDirectory = Path.Combine(userProfile, ".maldalang", "packages");
        Directory.CreateDirectory(_packagesDirectory);
        _embeddedResourceAssembly = embeddedResourceAssembly;
    }
    
    public string GetPackagePath(string packageName, string version)
    {
        return Path.Combine(_packagesDirectory, packageName, version);
    }
    
    public string GetPackageJsonPath(string packageName, string version)
    {
        return Path.Combine(GetPackagePath(packageName, version), "package.json");
    }
    
    private string GetEmbeddedResourceName(string relativePath)
    {
        // Embedded resources use forward slashes and namespace prefix
        var normalizedPath = relativePath.Replace('\\', '/');
        return $"MaldaLang.Executable.Resources.{normalizedPath}";
    }
    
    private bool TryReadEmbeddedResource(string resourceName, out string? content)
    {
        content = null;
        if (_embeddedResourceAssembly == null)
        {
            return false;
        }
        
        try
        {
            using var stream = _embeddedResourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return false;
            }
            
            using var reader = new StreamReader(stream, Encoding.UTF8);
            content = reader.ReadToEnd();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private bool EmbeddedResourceExists(string resourceName)
    {
        if (_embeddedResourceAssembly == null)
        {
            return false;
        }
        
        try
        {
            var resourceNames = _embeddedResourceAssembly.GetManifestResourceNames();
            return resourceNames.Contains(resourceName);
        }
        catch
        {
            return false;
        }
    }
    
    public bool IsPackageInstalled(string packageName, string version)
    {
        // First check embedded resources (for transpiled executables)
        var embeddedPackageJsonPath = GetEmbeddedResourceName($"packages/{packageName}/{version}/package.json");
        if (EmbeddedResourceExists(embeddedPackageJsonPath))
        {
            return true;
        }
        
        // Fall back to file system
        var packagePath = GetPackagePath(packageName, version);
        var packageJsonPath = GetPackageJsonPath(packageName, version);
        return Directory.Exists(packagePath) && File.Exists(packageJsonPath);
    }
    
    public PackageMetadata? LoadPackageMetadata(string packageName, string version)
    {
        // First try embedded resources (for transpiled executables)
        var embeddedPackageJsonPath = GetEmbeddedResourceName($"packages/{packageName}/{version}/package.json");
        if (TryReadEmbeddedResource(embeddedPackageJsonPath, out var embeddedJson))
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<PackageMetadata>(embeddedJson, options);
            }
            catch
            {
                // Fall through to file system
            }
        }
        
        // Fall back to file system
        var packageJsonPath = GetPackageJsonPath(packageName, version);
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }
        
        try
        {
            var json = File.ReadAllText(packageJsonPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<PackageMetadata>(json, options);
        }
        catch
        {
            return null;
        }
    }
    
    public bool TryReadPackageFile(string packageName, string version, string relativeFilePath, out string? content)
    {
        content = null;
        
        // First try embedded resources
        var embeddedPath = GetEmbeddedResourceName($"packages/{packageName}/{version}/{relativeFilePath}");
        if (TryReadEmbeddedResource(embeddedPath, out content))
        {
            return true;
        }
        
        // Fall back to file system
        var packagePath = GetPackagePath(packageName, version);
        var filePath = Path.Combine(packagePath, relativeFilePath);
        if (File.Exists(filePath))
        {
            try
            {
                content = File.ReadAllText(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        return false;
    }
    
    public void SavePackageMetadata(string packageName, string version, PackageMetadata metadata)
    {
        var packagePath = GetPackagePath(packageName, version);
        Directory.CreateDirectory(packagePath);
        
        var packageJsonPath = GetPackageJsonPath(packageName, version);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(metadata, options);
        File.WriteAllText(packageJsonPath, json);
    }
    
    public void InstallPackage(string packageName, string version, string sourcePath)
    {
        var packagePath = GetPackagePath(packageName, version);
        
        // Remove existing installation if any
        if (Directory.Exists(packagePath))
        {
            Directory.Delete(packagePath, true);
        }
        
        Directory.CreateDirectory(packagePath);
        
        // Copy all files from source to package directory
        CopyDirectory(sourcePath, packagePath);
    }
    
    public void UninstallPackage(string packageName, string version)
    {
        var packagePath = GetPackagePath(packageName, version);
        if (Directory.Exists(packagePath))
        {
            Directory.Delete(packagePath, true);
        }
        
        // Clean up parent directory if empty
        var parentDir = Path.GetDirectoryName(packagePath);
        if (parentDir != null && Directory.Exists(parentDir))
        {
            try
            {
                if (Directory.GetDirectories(parentDir).Length == 0 && Directory.GetFiles(parentDir).Length == 0)
                {
                    Directory.Delete(parentDir);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
    
    public string[] GetInstalledVersions(string packageName)
    {
        var versions = new HashSet<string>();
        
        // Check embedded resources first
        if (_embeddedResourceAssembly != null)
        {
            try
            {
                var resourceNames = _embeddedResourceAssembly.GetManifestResourceNames();
                var prefix = GetEmbeddedResourceName($"packages/{packageName}/");
                
                foreach (var resourceName in resourceNames)
                {
                    if (resourceName.StartsWith(prefix))
                    {
                        // Extract version from resource path: Resources.packages.{packageName}/{version}/...
                        var afterPrefix = resourceName.Substring(prefix.Length);
                        var parts = afterPrefix.Split('/');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        {
                            versions.Add(parts[0]);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }
        
        // Also check file system
        var packageDir = Path.Combine(_packagesDirectory, packageName);
        if (Directory.Exists(packageDir))
        {
            var fileSystemVersions = Directory.GetDirectories(packageDir)
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>();
            
            foreach (var version in fileSystemVersions)
            {
                versions.Add(version);
            }
        }
        
        return versions.ToArray();
    }
    
    public string[] GetInstalledPackages()
    {
        var packages = new HashSet<string>();
        
        // Check embedded resources first
        if (_embeddedResourceAssembly != null)
        {
            try
            {
                var resourceNames = _embeddedResourceAssembly.GetManifestResourceNames();
                var prefix = GetEmbeddedResourceName("packages/");
                
                foreach (var resourceName in resourceNames)
                {
                    if (resourceName.StartsWith(prefix))
                    {
                        // Extract package name from resource path: Resources.packages/{packageName}/{version}/...
                        var afterPrefix = resourceName.Substring(prefix.Length);
                        var parts = afterPrefix.Split('/');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        {
                            packages.Add(parts[0]);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }
        
        // Also check file system
        if (Directory.Exists(_packagesDirectory))
        {
            var fileSystemPackages = Directory.GetDirectories(_packagesDirectory)
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>();
            
            foreach (var package in fileSystemPackages)
            {
                packages.Add(package);
            }
        }
        
        return packages.ToArray();
    }
    
    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }
        
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var destSubDir = Path.Combine(destDir, dirName);
            CopyDirectory(dir, destSubDir);
        }
    }
}
