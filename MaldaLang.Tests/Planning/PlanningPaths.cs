namespace MaldaLang.Tests.Planning;

internal static class PlanningPaths
{
    public static string RepoRoot
    {
        get
        {
            var anchor = ResolveRepoFile("docs", "planning", "core-builtin-inventory.txt");
            var dir = new DirectoryInfo(Path.GetDirectoryName(anchor)!);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "MaldaLang")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root (MaldaLang/).");
        }
    }

    public static string ResolveRepoFile(params string[] relativeParts)
    {
        var relative = Path.Combine(relativeParts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;

            var nested = Path.Combine(dir.FullName, "MaldaLang", relative);
            if (File.Exists(nested))
                return nested;

            dir = dir.Parent;
        }

        var fromTestProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", relative));
        if (File.Exists(fromTestProject))
            return fromTestProject;

        throw new FileNotFoundException($"Planning file not found: {relative}");
    }

    public static string ResolveRepoPath(params string[] relativeParts) =>
        Path.Combine(RepoRoot, Path.Combine(relativeParts));
}
