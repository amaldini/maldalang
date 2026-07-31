// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.IO;
using System.Linq;
using MaldaLang.PackageManager.Models;

public class ModuleResolver
{
    internal readonly PackageStorage _storage;
    private readonly PackageRegistry? _registry;
    private PackageRegistry? _lazyRegistry;
    
    public ModuleResolver(PackageStorage? storage = null, PackageRegistry? registry = null)
    {
        _storage = storage ?? new PackageStorage();
        _registry = registry; // Don't create registry eagerly - only when needed
    }
    
    private PackageRegistry GetRegistry()
    {
        if (_registry != null)
            return _registry;
        
        if (_lazyRegistry == null)
        {
            _lazyRegistry = new PackageRegistry(_storage);
        }
        
        return _lazyRegistry;
    }
    
    private static string GetMaldaHomePath()
    {
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return Path.Combine(Directory.GetCurrentDirectory(), ".malda");
        return Path.Combine(userProfile, ".malda");
    }
    
    public string? ResolveModulePath(string packageName, string? subModule = null)
    {
        // Virtual package "skills": resolve to ~/.malda/skills/<subModule>.malda
        if (packageName == "skills" && !string.IsNullOrEmpty(subModule))
        {
            var skillsPath = Path.Combine(GetMaldaHomePath(), "skills", subModule + ".malda");
            if (File.Exists(skillsPath))
                return skillsPath;
            return null;
        }
        
        var versions = _storage.GetInstalledVersions(packageName);
        if (versions.Length == 0)
        {
            return WorkspacePackageResolver.TryResolveModulePath(packageName, subModule);
        }

        var metadata = GetRegistry().GetPackageMetadata(packageName);
        if (metadata == null)
        {
            return WorkspacePackageResolver.TryResolveModulePath(packageName, subModule);
        }
        
        // Sort versions and get latest
        var sortedVersions = versions
            .Select(v => new { Version = v, PackageVersion = PackageVersion.TryParse(v, out var pv) ? pv : null })
            .Where(x => x.PackageVersion != null)
            .OrderByDescending(x => x.PackageVersion)
            .ToList();
        
        if (sortedVersions.Count == 0)
        {
            return null;
        }
        
        var version = sortedVersions[0].Version;
        var packagePath = _storage.GetPackagePath(packageName, version);
        
        // Check sub-module resolution first when a sub-module is specified.
        if (subModule != null)
        {
            if (metadata.Exports != null)
            {
                var exportKey = $"./{subModule}";
                if (metadata.Exports.TryGetValue(exportKey, out var exportPath))
                {
                    // Try embedded resource first
                    if (_storage.TryReadPackageFile(packageName, version, exportPath, out _))
                    {
                        // Return a special path indicator for embedded resources
                        return $"embedded:{packageName}:{version}:{exportPath}";
                    }
                    
                    // Fall back to file system
                    var fullPath = Path.Combine(packagePath, exportPath);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
            
            // Try default sub-module path
            var defaultSubModulePath = $"lib/{subModule}.malda";
            if (_storage.TryReadPackageFile(packageName, version, defaultSubModulePath, out _))
            {
                return $"embedded:{packageName}:{version}:{defaultSubModulePath}";
            }
            
            var fileSystemSubModulePath = Path.Combine(packagePath, "lib", $"{subModule}.malda");
            if (File.Exists(fileSystemSubModulePath))
            {
                return fileSystemSubModulePath;
            }
        }
        
        // Check main entry point
        if (metadata.Main != null)
        {
            // Try embedded resource first
            if (_storage.TryReadPackageFile(packageName, version, metadata.Main, out _))
            {
                return $"embedded:{packageName}:{version}:{metadata.Main}";
            }
            
            // Fall back to file system
            var mainPath = Path.Combine(packagePath, metadata.Main);
            if (File.Exists(mainPath))
            {
                return mainPath;
            }
        }
        
        // Default: lib/index.malda
        var defaultPath = "lib/index.malda";
        if (_storage.TryReadPackageFile(packageName, version, defaultPath, out _))
        {
            return $"embedded:{packageName}:{version}:{defaultPath}";
        }
        
        var fileSystemDefaultPath = Path.Combine(packagePath, "lib", "index.malda");
        if (File.Exists(fileSystemDefaultPath))
        {
            return fileSystemDefaultPath;
        }
        
        // Fallback: look for any .malda file in lib directory
        // For embedded resources, we can't list files, so we try common names
        var commonNames = new[] { "index.malda", "main.malda", "module.malda" };
        foreach (var name in commonNames)
        {
            var libPath = $"lib/{name}";
            if (_storage.TryReadPackageFile(packageName, version, libPath, out _))
            {
                return $"embedded:{packageName}:{version}:{libPath}";
            }
        }
        
        // Fall back to file system directory listing
        var libDir = Path.Combine(packagePath, "lib");
        if (Directory.Exists(libDir))
        {
            var maldaFiles = Directory.GetFiles(libDir, "*.malda");
            if (maldaFiles.Length > 0)
            {
                return maldaFiles[0];
            }
        }
        
        return WorkspacePackageResolver.TryResolveModulePath(packageName, subModule);
    }
    
    public bool IsPackageInstalled(string packageName)
    {
        var versions = _storage.GetInstalledVersions(packageName);
        return versions.Length > 0;
    }
    
    public string? GetInstalledVersion(string packageName)
    {
        var versions = _storage.GetInstalledVersions(packageName);
        if (versions.Length == 0)
        {
            return null;
        }
        
        // Return latest version
        var sortedVersions = versions
            .Select(v => new { Version = v, PackageVersion = PackageVersion.TryParse(v, out var pv) ? pv : null })
            .Where(x => x.PackageVersion != null)
            .OrderByDescending(x => x.PackageVersion)
            .ToList();
        
        return sortedVersions.Count > 0 ? sortedVersions[0].Version : versions[0];
    }
}
