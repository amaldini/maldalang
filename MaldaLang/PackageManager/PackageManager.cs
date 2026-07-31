// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.PackageManager.Models;

public class PackageManager
{
    private readonly PackageStorage _storage;
    private readonly PackageRegistry _registry;
    private readonly DependencyResolver _dependencyResolver;
    
    public PackageManager()
    {
        _storage = new PackageStorage();
        _registry = new PackageRegistry(_storage);
        _dependencyResolver = new DependencyResolver(_registry, _storage);
    }
    
    public PackageManager(PackageStorage storage)
    {
        _storage = storage;
        _registry = new PackageRegistry(storage);
        _dependencyResolver = new DependencyResolver(_registry, _storage);
    }
    
    public async Task<bool> InstallAsync(string packageName, string? version = null)
    {
        try
        {
            Console.WriteLine($"Installing {packageName}...");
            
            // Fetch package metadata
            PackageMetadata? metadata;
            if (version != null)
            {
                metadata = await _registry.FetchPackageMetadataAsync(packageName, version);
            }
            else
            {
                metadata = await _registry.FetchPackageMetadataAsync(packageName);
            }
            
            if (metadata == null)
            {
                Console.WriteLine($"Error: Package {packageName} not found in registry");
                return false;
            }
            
            var installVersion = version ?? metadata.Version;
            
            // Check if already installed
            if (_storage.IsPackageInstalled(packageName, installVersion))
            {
                Console.WriteLine($"Package {packageName}@{installVersion} is already installed");
                return true;
            }
            
            // Resolve dependencies
            var dependencies = new Dictionary<string, string>();
            if (metadata.Dependencies != null)
            {
                foreach (var dep in metadata.Dependencies)
                {
                    dependencies[dep.Key] = dep.Value;
                }
            }
            
            var resolved = _dependencyResolver.ResolveDependencies(dependencies);
            var installOrder = _dependencyResolver.GetInstallOrder();
            
            // Install dependencies first
            foreach (var packageNameToInstall in installOrder)
            {
                if (packageNameToInstall == packageName)
                    continue;
                    
                if (resolved.TryGetValue(packageNameToInstall, out var resolvedDep))
                {
                    if (resolvedDep.NeedsInstall && resolvedDep.Version != null)
                    {
                        await InstallPackageAsync(resolvedDep.PackageName, resolvedDep.Version);
                    }
                }
            }
            
            // Install the main package
            await InstallPackageAsync(packageName, installVersion, metadata);
            
            Console.WriteLine($"Successfully installed {packageName}@{installVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error installing {packageName}: {ex.Message}");
            return false;
        }
    }
    
    private async Task InstallPackageAsync(string packageName, string version, PackageMetadata? metadata = null)
    {
        // Create temporary directory for package
        var tempDir = Path.Combine(Path.GetTempPath(), $"maldalang_package_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Download package
            var downloadedPath = await _registry.DownloadPackageAsync(packageName, version, tempDir);
            if (downloadedPath == null)
            {
                throw new Exception("Failed to download package");
            }
            
            // Load or use provided metadata
            if (metadata == null)
            {
                var packageJsonPath = Path.Combine(tempDir, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var json = File.ReadAllText(packageJsonPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    metadata = JsonSerializer.Deserialize<PackageMetadata>(json, options);
                }
            }
            
            if (metadata == null)
            {
                throw new Exception("Package metadata not found");
            }
            
            // Install package
            _storage.InstallPackage(packageName, version, tempDir);
            
            // Save metadata
            _storage.SavePackageMetadata(packageName, version, metadata);
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
    
    public bool Uninstall(string packageName, string? version = null)
    {
        try
        {
            if (version != null)
            {
                if (!_storage.IsPackageInstalled(packageName, version))
                {
                    Console.WriteLine($"Package {packageName}@{version} is not installed");
                    return false;
                }
                
                _storage.UninstallPackage(packageName, version);
                Console.WriteLine($"Successfully uninstalled {packageName}@{version}");
            }
            else
            {
                var versions = _storage.GetInstalledVersions(packageName);
                if (versions.Length == 0)
                {
                    Console.WriteLine($"Package {packageName} is not installed");
                    return false;
                }
                
                foreach (var v in versions)
                {
                    _storage.UninstallPackage(packageName, v);
                }
                
                Console.WriteLine($"Successfully uninstalled {packageName}");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uninstalling {packageName}: {ex.Message}");
            return false;
        }
    }
    
    public void List()
    {
        var packages = _storage.GetInstalledPackages();
        if (packages.Length == 0)
        {
            Console.WriteLine("No packages installed");
            return;
        }
        
        Console.WriteLine("Installed packages:");
        foreach (var packageName in packages)
        {
            var versions = _storage.GetInstalledVersions(packageName);
            foreach (var version in versions)
            {
                var metadata = _storage.LoadPackageMetadata(packageName, version);
                var description = metadata?.Description ?? "No description";
                Console.WriteLine($"  {packageName}@{version} - {description}");
            }
        }
    }
    
    public async Task<List<PackageInfo>> SearchAsync(string query)
    {
        return await _registry.SearchPackagesAsync(query);
    }
    
    public async Task<List<PackageInfo>> ListAllPackagesAsync()
    {
        return await _registry.ListAllPackagesAsync();
    }
    
    public bool Init(string? directory = null)
    {
        try
        {
            var targetDir = directory ?? Directory.GetCurrentDirectory();
            var packageJsonPath = Path.Combine(targetDir, "package.json");
            
            var existed = File.Exists(packageJsonPath);
            
            var metadata = new PackageMetadata
            {
                Name = Path.GetFileName(targetDir),
                Version = "1.0.0",
                Description = "",
                Main = "main.malda"
            };
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(metadata, options);
            File.WriteAllText(packageJsonPath, json);
            
            if (existed)
            {
                Console.WriteLine($"Overwritten package.json in {targetDir}");
            }
            else
            {
                Console.WriteLine($"Initialized package.json in {targetDir}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing package.json: {ex.Message}");
            return false;
        }
    }
}
