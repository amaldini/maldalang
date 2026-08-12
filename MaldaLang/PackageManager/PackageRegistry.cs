// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.PackageManager.Models;

public class PackageRegistry
{
    private readonly PackageStorage _storage;
    private readonly Dictionary<string, PackageMetadata> _cache = new();
    private readonly HttpClient _httpClient;
    private readonly string _registryUrl;
    
    public PackageRegistry(PackageStorage? storage = null)
    {
        _storage = storage ?? new PackageStorage();
        _httpClient = new HttpClient();
        // Optional: only remote install/search need MALDA_REGISTRY_URL.
        // Local metadata, workspace packages, list/init/uninstall work offline.
        _registryUrl = Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL") ?? string.Empty;
    }

    private void RequireRegistryUrl()
    {
        if (!string.IsNullOrWhiteSpace(_registryUrl))
            return;

        throw new InvalidOperationException(
            "MALDA_REGISTRY_URL is not set. Remote install/search need a registry URL. " +
            "For workspace packages, put libs under packages/ and import them (no install). " +
            "Or install a local folder: malda install ./path-to-package");
    }
    
    public PackageMetadata? GetPackageMetadata(string packageName, string? version = null)
    {
        // Check cache first
        var cacheKey = $"{packageName}@{version ?? "latest"}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        
        // Check local storage
        if (version != null)
        {
            var localMetadata = _storage.LoadPackageMetadata(packageName, version);
            if (localMetadata != null)
            {
                _cache[cacheKey] = localMetadata;
                return localMetadata;
            }
        }
        else
        {
            // Try to find latest version locally
            var versions = _storage.GetInstalledVersions(packageName);
            if (versions.Length > 0)
            {
                // Sort versions and get latest
                var sortedVersions = versions
                    .Select(v => new { Version = v, PackageVersion = PackageVersion.TryParse(v, out var pv) ? pv : null })
                    .Where(x => x.PackageVersion != null)
                    .OrderByDescending(x => x.PackageVersion)
                    .ToList();
                
                if (sortedVersions.Count > 0)
                {
                    var latestVersion = sortedVersions[0].Version;
                    var localMetadata = _storage.LoadPackageMetadata(packageName, latestVersion);
                    if (localMetadata != null)
                    {
                        _cache[cacheKey] = localMetadata;
                        return localMetadata;
                    }
                }
            }
        }
        
        return null;
    }
    
    public async Task<PackageMetadata?> FetchPackageMetadataAsync(string packageName, string? version = null)
    {
        RequireRegistryUrl();
        try
        {
            var url = $"{_registryUrl}/api/packages/{packageName}";
            if (version != null)
            {
                url += $"/{version}";
            }
            
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, options);
                if (metadata != null)
                {
                    var cacheKey = $"{packageName}@{version ?? "latest"}";
                    _cache[cacheKey] = metadata;
                }
                return metadata;
            }
        }
        catch
        {
            // Network errors - return null
        }
        
        return null;
    }
    
    public async Task<string?> DownloadPackageAsync(string packageName, string version, string destinationPath)
    {
        RequireRegistryUrl();
        try
        {
            var url = $"{_registryUrl}/api/packages/{packageName}/{version}/download";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"{packageName}-{version}.zip");
                using (var fileStream = File.Create(zipPath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
                
                // Extract zip (simplified - in production, use proper zip extraction)
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, destinationPath);
                File.Delete(zipPath);
                
                return destinationPath;
            }
        }
        catch
        {
            // Download errors
        }
        
        return null;
    }
    
    public async Task<List<PackageInfo>> SearchPackagesAsync(string query)
    {
        RequireRegistryUrl();
        try
        {
            var url = $"{_registryUrl}/api/packages/search?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var results = JsonSerializer.Deserialize<List<PackageInfo>>(json, options);
                return results ?? new List<PackageInfo>();
            }
        }
        catch
        {
            // Search errors
        }
        
        return new List<PackageInfo>();
    }
    
    public async Task<List<PackageInfo>> ListAllPackagesAsync()
    {
        RequireRegistryUrl();
        // Use empty search query to get all packages, or try a dedicated endpoint
        try
        {
            // First try a dedicated list endpoint
            var url = $"{_registryUrl}/api/packages";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var results = JsonSerializer.Deserialize<List<PackageInfo>>(json, options);
                return results ?? new List<PackageInfo>();
            }
        }
        catch
        {
            // If dedicated endpoint fails, try empty search
        }
        
        // Fallback to empty search query
        return await SearchPackagesAsync("");
    }
    
    public void ClearCache()
    {
        _cache.Clear();
    }
}

public class PackageInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
}
