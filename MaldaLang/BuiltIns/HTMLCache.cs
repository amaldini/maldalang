// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;

public class HTMLCacheInstance : ObjectInstance
{
    private Dictionary<string, CachedPage> _cache;
    private string _cacheDirectory;
    private int? _maxSize;
    private int? _expirationHours;
    private readonly object _cacheLock = new object();
    
    private class CachedPage
    {
        public string Prompt { get; set; } = "";
        public string HTML { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public Dictionary<string, RuntimeValue> Metadata { get; set; } = new();
        public DateTime LastAccessed { get; set; }
    }
    
    public HTMLCacheInstance(string? cacheDir = null, int? maxSize = null, int? expirationHours = null) : base(null)
    {
        _cache = new Dictionary<string, CachedPage>();
        _cacheDirectory = cacheDir ?? GetDefaultCacheDirectory();
        _maxSize = maxSize;
        _expirationHours = expirationHours;
        
        Directory.CreateDirectory(_cacheDirectory);
        LoadCacheFromDisk();
    }
    
    private string GetDefaultCacheDirectory()
    {
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "MALDA", "html_cache");
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "directory")
            return RuntimeValue.String(_cacheDirectory);
        if (name == "count")
        {
            lock (_cacheLock)
            {
                return RuntimeValue.Integer(_cache.Count);
            }
        }
        if (name == "totalSize")
        {
            lock (_cacheLock)
            {
                long totalSize = 0;
                foreach (var entry in _cache.Values)
                {
                    totalSize += Encoding.UTF8.GetByteCount(entry.HTML);
                }
                return RuntimeValue.Integer((int)totalSize);
            }
        }
        
        // Handle method access
        if (name == "get" || name == "set" || name == "has" || name == "remove" || 
            name == "clear" || name == "list" || name == "stats")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on HTMLCache.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "get":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("get() expects 1 string argument");
                return Get(args[0].AsString());
            
            case "set":
                if (args.Count < 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("set() expects 2-3 arguments: (prompt, html, metadata?)");
                var metadata = args.Count > 2 && args[2].Type == ValueType.Object ? args[2].AsObject() : null;
                Set(args[0].AsString(), args[1].AsString(), metadata);
                return RuntimeValue.Null();
            
            case "has":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("has() expects 1 string argument");
                return RuntimeValue.Boolean(Has(args[0].AsString()));
            
            case "remove":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("remove() expects 1 string argument");
                Remove(args[0].AsString());
                return RuntimeValue.Null();
            
            case "clear":
                if (args.Count != 0)
                    throw new Exception("clear() expects 0 arguments");
                Clear();
                return RuntimeValue.Null();
            
            case "list":
                if (args.Count != 0)
                    throw new Exception("list() expects 0 arguments");
                return List();
            
            case "stats":
                if (args.Count != 0)
                    throw new Exception("stats() expects 0 arguments");
                return Stats();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private string GenerateCacheKey(string prompt)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToBase64String(hash)
            .Replace("/", "_")
            .Replace("+", "-")
            .Substring(0, 16);
    }
    
    private RuntimeValue Get(string prompt)
    {
        var key = GenerateCacheKey(prompt);
        
        lock (_cacheLock)
        {
            if (!_cache.ContainsKey(key))
                return RuntimeValue.Null();
            
            var cached = _cache[key];
            
            // Check expiration
            if (_expirationHours.HasValue)
            {
                var age = DateTime.UtcNow - cached.GeneratedAt;
                if (age.TotalHours > _expirationHours.Value)
                {
                    _cache.Remove(key);
                    DeleteCacheFile(key);
                    return RuntimeValue.Null();
                }
            }
            
            // Update last accessed for LRU
            cached.LastAccessed = DateTime.UtcNow;
            
            return RuntimeValue.String(cached.HTML);
        }
    }
    
    private void Set(string prompt, string html, ObjectInstance? metadata = null)
    {
        var key = GenerateCacheKey(prompt);
        
        lock (_cacheLock)
        {
            // Check if we need to evict entries (LRU)
            if (_maxSize.HasValue && _cache.Count >= _maxSize.Value && !_cache.ContainsKey(key))
            {
                EvictLRU();
            }
            
            var metadataDict = new Dictionary<string, RuntimeValue>();
            if (metadata != null)
            {
                // Extract metadata from object (simplified - assumes it's a JsonObject)
                if (metadata is JsonObject jsonObj)
                {
                    // Copy metadata from JsonObject
                    // This is a simplified implementation
                }
            }
            
            var cached = new CachedPage
            {
                Prompt = prompt,
                HTML = html,
                GeneratedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                Metadata = metadataDict
            };
            
            _cache[key] = cached;
            SaveToDisk(key, cached);
        }
    }
    
    private bool Has(string prompt)
    {
        var key = GenerateCacheKey(prompt);
        lock (_cacheLock)
        {
            if (!_cache.ContainsKey(key))
                return false;
            
            // Check expiration
            if (_expirationHours.HasValue)
            {
                var cached = _cache[key];
                var age = DateTime.UtcNow - cached.GeneratedAt;
                if (age.TotalHours > _expirationHours.Value)
                {
                    _cache.Remove(key);
                    DeleteCacheFile(key);
                    return false;
                }
            }
            
            return true;
        }
    }
    
    private void Remove(string prompt)
    {
        var key = GenerateCacheKey(prompt);
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(key))
            {
                _cache.Remove(key);
                DeleteCacheFile(key);
            }
        }
    }
    
    private void Clear()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            
            // Delete all cache files
            try
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore errors
            }
        }
    }
    
    private RuntimeValue List()
    {
        lock (_cacheLock)
        {
            var list = new List<RuntimeValue>();
            foreach (var entry in _cache.Values)
            {
                var obj = new JsonObject();
                obj.Set("prompt", RuntimeValue.String(entry.Prompt));
                obj.Set("generatedAt", RuntimeValue.String(entry.GeneratedAt.ToString("O")));
                obj.Set("lastAccessed", RuntimeValue.String(entry.LastAccessed.ToString("O")));
                obj.Set("size", RuntimeValue.Integer(Encoding.UTF8.GetByteCount(entry.HTML)));
                list.Add(RuntimeValue.Object(obj));
            }
            return RuntimeValue.Array(list);
        }
    }
    
    private RuntimeValue Stats()
    {
        lock (_cacheLock)
        {
            var stats = new JsonObject();
            stats.Set("count", RuntimeValue.Integer(_cache.Count));
            
            long totalSize = 0;
            foreach (var entry in _cache.Values)
            {
                totalSize += Encoding.UTF8.GetByteCount(entry.HTML);
            }
            stats.Set("totalSize", RuntimeValue.Integer((int)totalSize));
            stats.Set("maxSize", RuntimeValue.Integer(_maxSize ?? -1));
            stats.Set("expirationHours", RuntimeValue.Integer(_expirationHours ?? -1));
            stats.Set("directory", RuntimeValue.String(_cacheDirectory));
            
            return RuntimeValue.Object(stats);
        }
    }
    
    private void EvictLRU()
    {
        if (_cache.Count == 0)
            return;
        
        var oldest = _cache.OrderBy(kvp => kvp.Value.LastAccessed).First();
        var key = oldest.Key;
        _cache.Remove(key);
        DeleteCacheFile(key);
    }
    
    private void LoadCacheFromDisk()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;
            
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    var prompt = root.GetProperty("prompt").GetString() ?? "";
                    var html = root.GetProperty("html").GetString() ?? "";
                    var generatedAtStr = root.GetProperty("generatedAt").GetString() ?? "";
                    
                    if (DateTime.TryParse(generatedAtStr, out var generatedAt))
                    {
                        var key = GenerateCacheKey(prompt);
                        var cached = new CachedPage
                        {
                            Prompt = prompt,
                            HTML = html,
                            GeneratedAt = generatedAt,
                            LastAccessed = generatedAt
                        };
                        
                        _cache[key] = cached;
                    }
                }
                catch
                {
                    // Skip invalid cache files
                }
            }
        }
        catch
        {
            // Ignore errors loading cache
        }
    }
    
    private void SaveToDisk(string key, CachedPage page)
    {
        try
        {
            var filePath = Path.Combine(_cacheDirectory, $"{key}.json");
            var json = JsonSerializer.Serialize(new
            {
                prompt = page.Prompt,
                html = page.HTML,
                generatedAt = page.GeneratedAt.ToString("O"),
                metadata = new Dictionary<string, object>()
            });
            
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore errors saving cache
        }
    }
    
    private void DeleteCacheFile(string key)
    {
        try
        {
            var filePath = Path.Combine(_cacheDirectory, $"{key}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore errors deleting cache file
        }
    }
}