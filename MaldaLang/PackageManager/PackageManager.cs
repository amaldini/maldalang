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
    private PackageRegistry? _registry;
    private DependencyResolver? _dependencyResolver;

    /// <summary>Output for CLI messages. Defaults to <see cref="Console.Out"/>; tests may redirect.</summary>
    public TextWriter Out { get; set; } = Console.Out;
    
    public PackageManager()
    {
        _storage = new PackageStorage();
    }
    
    public PackageManager(PackageStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    private PackageRegistry GetRegistry()
    {
        return _registry ??= new PackageRegistry(_storage);
    }

    private DependencyResolver GetDependencyResolver()
    {
        return _dependencyResolver ??= new DependencyResolver(GetRegistry(), _storage);
    }
    
    public async Task<bool> InstallAsync(string packageName, string? version = null)
    {
        try
        {
            Out.WriteLine($"Installing {packageName}...");
            
            // Fetch package metadata
            PackageMetadata? metadata;
            if (version != null)
            {
                metadata = await GetRegistry().FetchPackageMetadataAsync(packageName, version);
            }
            else
            {
                metadata = await GetRegistry().FetchPackageMetadataAsync(packageName);
            }
            
            if (metadata == null)
            {
                Out.WriteLine($"Error: Package {packageName} not found in registry");
                return false;
            }
            
            var installVersion = version ?? metadata.Version;
            
            // Check if already installed
            if (_storage.IsPackageInstalled(packageName, installVersion))
            {
                Out.WriteLine($"Package {packageName}@{installVersion} is already installed");
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
            
            var resolved = GetDependencyResolver().ResolveDependencies(dependencies);
            var installOrder = GetDependencyResolver().GetInstallOrder();
            
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
            
            Out.WriteLine($"Successfully installed {packageName}@{installVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Out.WriteLine($"Error installing {packageName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Installs a package from a local directory, package.json, or .malda entry (no registry).
    /// </summary>
    public bool InstallFromPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Out.WriteLine("Error: path is required");
                return false;
            }

            var full = Path.GetFullPath(path);
            string sourceDir;
            string? singleFileName = null;

            if (File.Exists(full))
            {
                var fileName = Path.GetFileName(full);
                if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase))
                {
                    sourceDir = Path.GetDirectoryName(full)
                        ?? throw new InvalidOperationException("Could not resolve package directory");
                }
                else if (fileName.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
                {
                    sourceDir = Path.GetDirectoryName(full)
                        ?? throw new InvalidOperationException("Could not resolve package directory");
                    singleFileName = fileName;
                }
                else
                {
                    Out.WriteLine($"Error: unsupported local package file: {full}");
                    return false;
                }
            }
            else if (Directory.Exists(full))
            {
                sourceDir = full;
            }
            else
            {
                Out.WriteLine($"Error: path not found: {full}");
                return false;
            }

            var metadata = LoadOrCreateLocalMetadata(sourceDir, singleFileName);
            var packageName = metadata.Name;
            var version = string.IsNullOrWhiteSpace(metadata.Version) ? "1.0.0" : metadata.Version;

            if (_storage.IsPackageInstalled(packageName, version))
            {
                Out.WriteLine($"Package {packageName}@{version} is already installed; reinstalling...");
            }

            Out.WriteLine($"Installing {packageName}@{version} from {sourceDir}...");
            _storage.InstallPackage(packageName, version, sourceDir);
            _storage.SavePackageMetadata(packageName, version, metadata);
            Out.WriteLine($"Successfully installed {packageName}@{version}");
            return true;
        }
        catch (Exception ex)
        {
            Out.WriteLine($"Error installing from path: {ex.Message}");
            return false;
        }
    }

    private static PackageMetadata LoadOrCreateLocalMetadata(string sourceDir, string? singleFileName)
    {
        var packageJsonPath = Path.Combine(sourceDir, "package.json");
        if (File.Exists(packageJsonPath))
        {
            var json = File.ReadAllText(packageJsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, options);
            if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Name))
            {
                if (string.IsNullOrWhiteSpace(metadata.Main) && !string.IsNullOrEmpty(singleFileName))
                    metadata.Main = singleFileName;
                if (string.IsNullOrWhiteSpace(metadata.Version))
                    metadata.Version = "1.0.0";
                return metadata;
            }
        }

        var folderName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var main = singleFileName
            ?? (File.Exists(Path.Combine(sourceDir, "index.malda")) ? "index.malda"
                : File.Exists(Path.Combine(sourceDir, "main.malda")) ? "main.malda"
                : Directory.GetFiles(sourceDir, "*.malda").Select(Path.GetFileName).FirstOrDefault());

        return new PackageMetadata
        {
            Name = string.IsNullOrWhiteSpace(folderName) ? "local-package" : folderName,
            Version = "1.0.0",
            Description = "Installed from local path",
            Main = main
        };
    }
    
    private async Task InstallPackageAsync(string packageName, string version, PackageMetadata? metadata = null)
    {
        // Create temporary directory for package
        var tempDir = Path.Combine(Path.GetTempPath(), $"maldalang_package_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            // Download package
            var downloadedPath = await GetRegistry().DownloadPackageAsync(packageName, version, tempDir);
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
                    Out.WriteLine($"Package {packageName}@{version} is not installed");
                    return false;
                }
                
                _storage.UninstallPackage(packageName, version);
                Out.WriteLine($"Successfully uninstalled {packageName}@{version}");
            }
            else
            {
                var versions = _storage.GetInstalledVersions(packageName);
                if (versions.Length == 0)
                {
                    Out.WriteLine($"Package {packageName} is not installed");
                    return false;
                }
                
                foreach (var v in versions)
                {
                    _storage.UninstallPackage(packageName, v);
                }
                
                Out.WriteLine($"Successfully uninstalled {packageName}");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Out.WriteLine($"Error uninstalling {packageName}: {ex.Message}");
            return false;
        }
    }
    
    public void List()
    {
        var packages = _storage.GetInstalledPackages();
        if (packages.Length == 0)
        {
            Out.WriteLine("No packages installed");
            return;
        }
        
        Out.WriteLine("Installed packages:");
        foreach (var packageName in packages)
        {
            var versions = _storage.GetInstalledVersions(packageName);
            foreach (var version in versions)
            {
                var metadata = _storage.LoadPackageMetadata(packageName, version);
                var description = metadata?.Description ?? "No description";
                Out.WriteLine($"  {packageName}@{version} - {description}");
            }
        }
    }

    public void ListWorkspace()
    {
        var packages = WorkspacePackageResolver.ListWorkspacePackages();
        if (packages.Count == 0)
        {
            Out.WriteLine("No workspace packages found");
            Out.WriteLine("Hint: create packages/<name>/*.malda, or set MALDA_PACKAGES_DIR");
            return;
        }

        Out.WriteLine("Workspace packages:");
        foreach (var (name, entryPath) in packages)
        {
            Out.WriteLine($"  {name} -> {entryPath}");
        }
    }
    
    public async Task<List<PackageInfo>> SearchAsync(string query)
    {
        return await GetRegistry().SearchPackagesAsync(query);
    }
    
    public async Task<List<PackageInfo>> ListAllPackagesAsync()
    {
        return await GetRegistry().ListAllPackagesAsync();
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
                Out.WriteLine($"Overwritten package.json in {targetDir}");
            }
            else
            {
                Out.WriteLine($"Initialized package.json in {targetDir}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Out.WriteLine($"Error initializing package.json: {ex.Message}");
            return false;
        }
    }
}
