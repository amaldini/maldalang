using System.Text.RegularExpressions;

namespace MaldaLang.Tests.Planning;

internal static class BuiltInRegistryInventoryLoader
{
    /// <summary>
    /// Names in the GetDescriptor switch. Most are an "or" alternative, but the last name in
    /// each arm carries the "=>" instead, so matching only the "or" form silently loses one
    /// real built-in per arm.
    /// </summary>
    private static readonly Regex RegistrySymbolPattern = new(
        @"^\s+""(?<name>[a-zA-Z][a-zA-Z0-9_]*)""\s*(?:or\s*$|=>)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlySet<string> LoadSymbolsFromRegistrySource()
    {
        var path = PlanningPaths.ResolveRepoFile("MaldaLang", "BuiltIns", "BuiltInRegistry.cs");
        var text = File.ReadAllText(path);

        var start = text.IndexOf("GetDescriptor(string name)", StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf("_ => null", start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
            throw new InvalidOperationException("Could not locate the BuiltInRegistry.GetDescriptor switch.");

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in RegistrySymbolPattern.Matches(text[start..end]))
            symbols.Add(match.Groups["name"].Value);
        return symbols;
    }

    public static IReadOnlySet<string> LoadSymbolsFromCoreInventory()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "planning", "core-builtin-inventory.txt");
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
                continue;

            var arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow <= 0)
                continue;

            var symbol = line[..arrow].Trim();
            if (symbol.Length > 0)
                symbols.Add(symbol);
        }

        return symbols;
    }

    public static IReadOnlySet<string> LoadForbiddenPackSymbols()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "planning", "optional-pack-builtin-inventory.txt");
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
                continue;

            var arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow <= 0)
                continue;

            var symbol = line[..arrow].Trim();
            if (symbol.Length > 0)
                symbols.Add(symbol);
        }

        return symbols;
    }
}
