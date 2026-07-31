// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager.Models;

using System.Collections.Generic;
using System.Text.Json.Serialization;

public class PackageMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("main")]
    public string? Main { get; set; }
    
    [JsonPropertyName("exports")]
    public Dictionary<string, string>? Exports { get; set; }
    
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }
    
    [JsonPropertyName("dotnetDependencies")]
    public Dictionary<string, string>? DotNetDependencies { get; set; }
    
    [JsonPropertyName("author")]
    public string? Author { get; set; }
    
    [JsonPropertyName("license")]
    public string? License { get; set; }
    
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }
}
