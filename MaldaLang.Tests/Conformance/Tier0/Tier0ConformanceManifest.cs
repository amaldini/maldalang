using System.Text.Json;
using System.Text.Json.Serialization;
using MaldaLang.Tests.Planning;

namespace MaldaLang.Tests.Conformance.Tier0;

public sealed class Tier0BackendFlags
{
    [JsonPropertyName("interpreter")]
    public bool Interpreter { get; init; } = true;

    [JsonPropertyName("csharp")]
    public bool CSharp { get; init; } = true;

    [JsonPropertyName("javascript")]
    public bool JavaScript { get; init; }
}

public sealed class Tier0ConformanceCase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("spec")]
    public string Spec { get; init; } = "";

    [JsonPropertyName("backends")]
    public Tier0BackendFlags Backends { get; init; } = new();

    [JsonPropertyName("jsSkipReason")]
    public string? JsSkipReason { get; init; }

    [JsonPropertyName("csharpSkipReason")]
    public string? CSharpSkipReason { get; init; }

    public string MaldaPath => Path.Combine(Tier0ConformancePaths.CasesDirectory, File);

    public string ExpectPath => Path.ChangeExtension(MaldaPath, ".expect");
}

public sealed class Tier0ConformanceManifest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("cases")]
    public List<Tier0ConformanceCase> Cases { get; init; } = [];

    public static string ManifestPath => Tier0ConformancePaths.ManifestPath;

    public static Tier0ConformanceManifest Load()
    {
        var json = File.ReadAllText(ManifestPath);
        var manifest = JsonSerializer.Deserialize<Tier0ConformanceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse {ManifestPath}");
        if (manifest.Cases.Count == 0)
            throw new InvalidOperationException("Tier 0 manifest has no cases.");
        return manifest;
    }

    public static IReadOnlyList<Tier0ConformanceCase> LoadCases() => Load().Cases;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal static class Tier0ConformancePaths
{
    public static string RootDirectory => PlanningPaths.ResolveRepoPath("conformance", "tier0");

    public static string CasesDirectory => Path.Combine(RootDirectory, "cases");

    public static string ManifestPath => Path.Combine(RootDirectory, "manifest.json");
}
