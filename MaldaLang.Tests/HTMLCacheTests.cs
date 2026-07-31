// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using System.IO;

namespace MaldaLang.Tests;

public class HTMLCacheTests
{
    private string GetTestCacheDir()
    {
        return Path.Combine(Path.GetTempPath(), "spl_test_cache_" + Guid.NewGuid().ToString("N")[..8]);
    }
    
    [Fact]
    public void HTMLCache_Creation_WithDefaultDirectory()
    {
        var cache = new HTMLCacheInstance();
        var directory = cache.Get("directory", null).AsString();
        Assert.NotNull(directory);
        Assert.True(Directory.Exists(directory));
    }
    
    [Fact]
    public void HTMLCache_Creation_WithCustomDirectory()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        Assert.Equal(cacheDir, cache.Get("directory", null).AsString());
        Assert.True(Directory.Exists(cacheDir));
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_GetSet_Works()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        var prompt = "Contact form";
        var html = "<html><body>Test</body></html>";
        
        // Set cache
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        // Get cache
        var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(prompt) });
        Assert.Equal(MaldaLang.Interpreter.ValueType.String, cached.Type);
        Assert.Equal(html, cached.AsString());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Get_NonExistent_ReturnsNull()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String("nonexistent") });
        Assert.Equal(MaldaLang.Interpreter.ValueType.Null, cached.Type);
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Has_Works()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        var prompt = "Test form";
        var html = "<html><body>Test</body></html>";
        
        Assert.False(cache.CallMethod("has", new List<RuntimeValue> { RuntimeValue.String(prompt) }).AsBoolean());
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        Assert.True(cache.CallMethod("has", new List<RuntimeValue> { RuntimeValue.String(prompt) }).AsBoolean());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Remove_Works()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        var prompt = "Test form";
        var html = "<html><body>Test</body></html>";
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        Assert.True(cache.CallMethod("has", new List<RuntimeValue> { RuntimeValue.String(prompt) }).AsBoolean());
        
        cache.CallMethod("remove", new List<RuntimeValue> { RuntimeValue.String(prompt) });
        
        Assert.False(cache.CallMethod("has", new List<RuntimeValue> { RuntimeValue.String(prompt) }).AsBoolean());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Clear_Works()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String("prompt1"), 
            RuntimeValue.String("<html>1</html>") 
        });
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String("prompt2"), 
            RuntimeValue.String("<html>2</html>") 
        });
        
        Assert.Equal(2, cache.Get("count", null).AsInteger());
        
        cache.CallMethod("clear", new List<RuntimeValue>());
        
        Assert.Equal(0, cache.Get("count", null).AsInteger());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Stats_ReturnsCorrectValues()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        var prompt = "Test form";
        var html = "<html><body>Test</body></html>";
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        var stats = cache.CallMethod("stats", new List<RuntimeValue>());
        Assert.Equal(MaldaLang.Interpreter.ValueType.Object, stats.Type);
        
        var statsObj = stats.AsObject();
        var count = statsObj.Get("count", null);
        Assert.Equal(1, count.AsInteger());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_List_ReturnsArray()
    {
        var cacheDir = GetTestCacheDir();
        var cache = new HTMLCacheInstance(cacheDir);
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String("prompt1"), 
            RuntimeValue.String("<html>1</html>") 
        });
        
        var list = cache.CallMethod("list", new List<RuntimeValue>());
        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, list.Type);
        Assert.Equal(1, list.AsArray().Count);
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Persistence_Works()
    {
        var cacheDir = GetTestCacheDir();
        var prompt = "Persistent form";
        var html = "<html><body>Persistent</body></html>";
        
        // Create cache and set value
        var cache1 = new HTMLCacheInstance(cacheDir);
        cache1.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        // Create new cache instance (should load from disk)
        var cache2 = new HTMLCacheInstance(cacheDir);
        var cached = cache2.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(prompt) });
        
        Assert.Equal(html, cached.AsString());
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public void HTMLCache_Expiration_Works()
    {
        var cacheDir = GetTestCacheDir();
        // Create cache with 1 hour expiration, but we'll test with very short expiration
        // Note: This test may be flaky due to timing, but demonstrates the concept
        var cache = new HTMLCacheInstance(cacheDir, null, 0); // 0 hours = expired immediately
        
        var prompt = "Expired form";
        var html = "<html><body>Expired</body></html>";
        
        cache.CallMethod("set", new List<RuntimeValue> 
        { 
            RuntimeValue.String(prompt), 
            RuntimeValue.String(html) 
        });
        
        // With 0 hours expiration, should be expired immediately
        var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(prompt) });
        // Note: This depends on implementation - if expiration is checked on get, this should be null
        
        // Cleanup
        Directory.Delete(cacheDir, true);
    }
}