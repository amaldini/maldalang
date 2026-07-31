using MaldaLang.Parser.AST.Declarations;

namespace MaldaLang.Tests;

public enum PropertyBackend
{
    Interpreter,
    CSharp,
    Js
}

public sealed record BackendEligibility(
    PropertyBackend Backend,
    bool IsEligible,
    string Status,
    string? Reason);

public static class BackendCapabilityMatrix
{
    private static readonly IReadOnlyDictionary<PropertyBackend, IReadOnlySet<string>> SupportedCapabilities =
        new Dictionary<PropertyBackend, IReadOnlySet<string>>
        {
            [PropertyBackend.Interpreter] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "core",
                "file-io",
                "actors",
                "workflows",
                "dotnet-interop",
                "host-interop"
            },
            [PropertyBackend.CSharp] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "core",
                "file-io",
                "actors",
                "workflows",
                "dotnet-interop",
                "host-interop"
            },
            [PropertyBackend.Js] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "core",
                "web-dom",
                "game-canvas"
            }
        };

    public static IReadOnlyList<BackendEligibility> Evaluate(PropertyDeclaration property)
    {
        var requiredCapabilities = property.GetRequiredCapabilities();
        var targetModes = NormalizeTargetModes(property.GetTargetModes());
        var results = new List<BackendEligibility>(3);
        foreach (var backend in new[] { PropertyBackend.Interpreter, PropertyBackend.CSharp, PropertyBackend.Js })
        {
            results.Add(EvaluateBackend(backend, requiredCapabilities, targetModes));
        }

        return results;
    }

    private static BackendEligibility EvaluateBackend(
        PropertyBackend backend,
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlySet<string> targetModes)
    {
        var backendName = ToModeName(backend);
        if (targetModes.Count > 0 && !targetModes.Contains(backendName))
        {
            return new BackendEligibility(
                backend,
                IsEligible: false,
                Status: "not-applicable",
                Reason: $"Property target modes exclude backend '{backendName}'.");
        }

        if (!SupportedCapabilities.TryGetValue(backend, out var supported))
        {
            return new BackendEligibility(
                backend,
                IsEligible: false,
                Status: "not-applicable",
                Reason: $"No capability profile is registered for backend '{backendName}'.");
        }

        var unsupported = requiredCapabilities
            .Where(cap => !supported.Contains(cap))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(cap => cap, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unsupported.Length == 0)
        {
            return new BackendEligibility(backend, IsEligible: true, Status: "eligible", Reason: null);
        }

        return new BackendEligibility(
            backend,
            IsEligible: false,
            Status: "not-applicable",
            Reason: $"Missing capabilities on '{backendName}': {string.Join(", ", unsupported)}.");
    }

    private static IReadOnlySet<string> NormalizeTargetModes(IReadOnlyList<string> rawModes)
    {
        if (rawModes.Count == 0)
        {
            // Preserve historical behavior where properties are interpreted as interpreter+csharp parity by default.
            return new HashSet<string>(new[] { "interpreter", "csharp" }, StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(rawModes, StringComparer.OrdinalIgnoreCase);
    }

    public static string ToModeName(PropertyBackend backend)
    {
        return backend switch
        {
            PropertyBackend.Interpreter => "interpreter",
            PropertyBackend.CSharp => "csharp",
            PropertyBackend.Js => "js",
            _ => backend.ToString().ToLowerInvariant()
        };
    }
}
