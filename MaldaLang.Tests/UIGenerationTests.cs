// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using System.IO;
using System.Text;

namespace MaldaLang.Tests;

// Use Collection attribute to ensure tests in this class run sequentially
// This prevents race conditions when multiple tests redirect Console.Out in parallel
[Collection("Sequential")]
public class UIGenerationTests : TestBase
{
    // RunProgramAsync is now provided by TestBase
    
    [Fact]
    public async Task ExtractHTML_FromMarkdownCodeBlock_ExtractsHTML()
    {
        var source = @"
var markdown = ""```html
<html><body>Test</body></html>
```"";
var html = extractHTML(markdown);
print(html);
";
        var output = await RunProgramAsync(source);
        Assert.Contains("<html><body>Test</body></html>", output);
    }
    
    [Fact]
    public async Task ExtractHTML_FromPlainHTML_ReturnsAsIs()
    {
        var source = @"
var html = ""<html><body>Test</body></html>"";
var extracted = extractHTML(html);
print(extracted);
";
        var output = await RunProgramAsync(source);
        Assert.Contains("<html><body>Test</body></html>", output);
    }
    
    [Fact]
    public async Task HTMLCache_GetSet_Works()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "spl_test_" + Guid.NewGuid().ToString("N")[..8]);
        var source = $@"
var cache = new HTMLCache(""{cacheDir}"");
cache.set(""test prompt"", ""<html>test</html>"");
var cached = cache.get(""test prompt"");
print(cached);
";
        var output = await RunProgramAsync(source);
        Assert.Contains("<html>test</html>", output);
        
        // Cleanup
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public async Task HTMLCache_CacheHit_ReturnsCached()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "spl_test_" + Guid.NewGuid().ToString("N")[..8]);
        var source = $@"
var cache = new HTMLCache(""{cacheDir}"");
cache.set(""same prompt"", ""<html>cached</html>"");
var cached1 = cache.get(""same prompt"");
var cached2 = cache.get(""same prompt"");
print(cached1);
print(cached2);
";
        var output = await RunProgramAsync(source);
        Assert.Contains("<html>cached</html>", output);
        
        // Cleanup
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, true);
    }
    
    [Fact]
    public async Task HTMLCache_Stats_ReturnsCount()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "spl_test_" + Guid.NewGuid().ToString("N")[..8]);
        var source = $@"
var cache = new HTMLCache(""{cacheDir}"");
cache.set(""prompt1"", ""<html>1</html>"");
cache.set(""prompt2"", ""<html>2</html>"");
var stats = cache.stats();
print(stats.count);
";
        var output = await RunProgramAsync(source);
        Assert.Contains("2", output);
        
        // Cleanup
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, true);
    }
}