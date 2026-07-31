// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.PackageManager.Models;

public class DependencyResolver
{
    private readonly PackageRegistry _registry;
    private readonly PackageStorage _storage;
    private readonly Dictionary<string, ResolvedDependency> _resolved = new();
    private readonly HashSet<string> _resolving = new();
    
    public DependencyResolver(PackageRegistry registry, PackageStorage storage)
    {
        _registry = registry;
        _storage = storage;
    }
    
    public Dictionary<string, ResolvedDependency> ResolveDependencies(Dictionary<string, string> dependencies)
    {
        _resolved.Clear();
        _resolving.Clear();
        
        foreach (var dep in dependencies)
        {
            ResolveDependency(dep.Key, dep.Value);
        }
        
        return new Dictionary<string, ResolvedDependency>(_resolved);
    }
    
    private void ResolveDependency(string packageName, string versionRange)
    {
        // Check for circular dependencies FIRST (before checking if already resolved)
        // This ensures we detect circular dependencies even if the package was resolved earlier
        if (_resolving.Contains(packageName))
        {
            throw new InvalidOperationException($"Circular dependency detected: {packageName}");
        }
        
        // Check if already resolved
        if (_resolved.ContainsKey(packageName))
        {
            var existing = _resolved[packageName];
            // Check if version range is satisfied
            if (existing.Version != null && PackageVersion.TryParse(existing.Version, out var existingVersion))
            {
                if (existingVersion.Satisfies(versionRange))
                {
                    return; // Already resolved with compatible version
                }
            }
        }
        
        _resolving.Add(packageName);
        
        try
        {
            // Try to find installed version that satisfies range
            var installedVersions = _storage.GetInstalledVersions(packageName);
            PackageVersion? bestVersion = null;
            string? bestVersionString = null;
            
            foreach (var versionString in installedVersions)
            {
                if (PackageVersion.TryParse(versionString, out var version))
                {
                    if (version.Satisfies(versionRange))
                    {
                        if (bestVersion == null || version > bestVersion)
                        {
                            bestVersion = version;
                            bestVersionString = versionString;
                        }
                    }
                }
            }
            
            if (bestVersionString != null)
            {
                var metadata = _storage.LoadPackageMetadata(packageName, bestVersionString);
                if (metadata != null)
                {
                    var resolved = new ResolvedDependency
                    {
                        PackageName = packageName,
                        Version = bestVersionString,
                        Metadata = metadata
                    };
                    
                    _resolved[packageName] = resolved;
                    
                    // Resolve transitive dependencies
                    if (metadata.Dependencies != null)
                    {
                        foreach (var dep in metadata.Dependencies)
                        {
                            ResolveDependency(dep.Key, dep.Value);
                        }
                    }
                    
                    return;
                }
            }
            
            // If not found locally, mark as needing installation
            var needsInstall = new ResolvedDependency
            {
                PackageName = packageName,
                VersionRange = versionRange,
                NeedsInstall = true
            };
            
            _resolved[packageName] = needsInstall;
        }
        finally
        {
            _resolving.Remove(packageName);
        }
    }
    
    public List<string> GetInstallOrder()
    {
        // Topological sort of dependencies
        var visited = new HashSet<string>();
        var result = new List<string>();
        
        void Visit(string packageName)
        {
            if (visited.Contains(packageName))
                return;
            
            if (!_resolved.TryGetValue(packageName, out var resolved))
                return;
            
            visited.Add(packageName);
            
            // Visit dependencies first
            if (resolved.Metadata?.Dependencies != null)
            {
                foreach (var dep in resolved.Metadata.Dependencies.Keys)
                {
                    Visit(dep);
                }
            }
            
            result.Add(packageName);
        }
        
        foreach (var packageName in _resolved.Keys)
        {
            Visit(packageName);
        }
        
        return result;
    }
}

public class ResolvedDependency
{
    public string PackageName { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? VersionRange { get; set; }
    public PackageMetadata? Metadata { get; set; }
    public bool NeedsInstall { get; set; }
}
