namespace MaldaLang.Scaffolding;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public sealed class TemplateScaffolder
{
    private static readonly Regex ValidProjectNameRegex = new("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private static readonly Regex ConditionalSectionRegex = new(
        @"\{\{([#\^])([A-Z0-9_]+)\}\}(.*?)\{\{/([A-Z0-9_]+)\}\}",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly string[] TemplateNames = { "webapi", "fullstack", "game" };

    public IReadOnlyList<string> SupportedTemplates => TemplateNames;

    public bool IsSupportedTemplate(string templateName)
    {
        return TemplateNames.Contains(templateName, StringComparer.OrdinalIgnoreCase);
    }

    public int Scaffold(string templateName, string destinationPath, TextWriter output, TextWriter error, NewCommandOptions? options = null)
    {
        options ??= new NewCommandOptions
        {
            TemplateName = templateName,
            DestinationPath = destinationPath
        };

        if (!ValidateInputs(templateName, destinationPath, options, error, out var normalizedTemplateName, out var projectName))
        {
            return 1;
        }

        var sourceTemplateName = IsGameFullstack(normalizedTemplateName, options)
            ? "game-fullstack"
            : normalizedTemplateName;
        var sourceRoot = ResolveTemplateDirectory(sourceTemplateName);
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            error.WriteLine($"Template '{templateName}' not found in Templates/{sourceTemplateName}.");
            return 1;
        }

        var fullDestination = Path.GetFullPath(destinationPath);
        bool destinationExists = Directory.Exists(fullDestination);
        if (destinationExists && !options.Force && Directory.EnumerateFileSystemEntries(fullDestination).Any())
        {
            error.WriteLine($"Destination '{fullDestination}' already exists and is not empty.");
            error.WriteLine("Use '--force' to overwrite scaffolded files.");
            return 1;
        }

        Directory.CreateDirectory(fullDestination);

        var variables = BuildTemplateVariables(normalizedTemplateName, projectName, options);
        var stats = CopyTemplateTree(sourceRoot, fullDestination, variables, options);
        var isGame = IsGameTemplate(normalizedTemplateName);
        var isGameFullstack = IsGameFullstack(normalizedTemplateName, options);
        if (!isGame)
        {
            var generatedProfiles = GenerateEnvironmentProfiles(fullDestination, variables);
            output.WriteLine($"Created {normalizedTemplateName} project at {fullDestination}");
            WriteScaffoldFileCount(output, stats, options.Force);
            output.WriteLine($"Environment profiles generated: {generatedProfiles}");
        }
        else
        {
            var createdLabel = isGameFullstack ? "game --fullstack" : normalizedTemplateName;
            output.WriteLine($"Created {createdLabel} project at {fullDestination}");
            WriteScaffoldFileCount(output, stats, options.Force);
        }

        output.WriteLine("Next steps:");
        output.WriteLine($"  cd \"{fullDestination}\"");
        var hasTests = Directory.Exists(Path.Combine(fullDestination, "tests"));
        if (options.IncludeTests && hasTests)
        {
            output.WriteLine("  malda test --format human");
        }

        if (isGameFullstack)
        {
            output.WriteLine("  malda compile app.malda --mode fullstack -o dist");
            output.WriteLine("  Review README.md to run dist/server with MALDA_WEB_DIRECTORY");
        }
        else if (isGame)
        {
            output.WriteLine("  malda play app.malda");
        }
        else
        {
            output.WriteLine(normalizedTemplateName == "fullstack" ? "  malda backend/app.malda" : "  malda app.malda");
        }

        if (options.LocalFirst && !isGame)
        {
            output.WriteLine("  malda db status");
            output.WriteLine("  malda db migrate");
            output.WriteLine("  malda db seed");
            output.WriteLine("  Review config/data.example.json and the generated local-first migration module");
        }
        return 0;
    }

    private static bool ValidateInputs(
        string templateName,
        string destinationPath,
        NewCommandOptions options,
        TextWriter error,
        out string normalizedTemplateName,
        out string projectName)
    {
        normalizedTemplateName = templateName.ToLowerInvariant();
        projectName = string.Empty;

        if (!TemplateNames.Contains(templateName, StringComparer.OrdinalIgnoreCase))
        {
            error.WriteLine($"Unsupported template '{templateName}'. Supported templates: {string.Join(", ", TemplateNames)}.");
            return false;
        }

        if (options.Fullstack && !IsGameTemplate(normalizedTemplateName))
        {
            error.WriteLine("Option '--fullstack' is only valid with 'malda new game'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            error.WriteLine("Destination path cannot be empty.");
            return false;
        }

        var destinationTrimmed = destinationPath.Trim();
        if (destinationTrimmed.IndexOfAny(new[] { '\0' }) >= 0)
        {
            error.WriteLine("Destination path contains invalid characters.");
            return false;
        }

        projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? Path.GetFileName(Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : options.ProjectName!;

        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = normalizedTemplateName + "-app";
        }

        if (!ValidProjectNameRegex.IsMatch(projectName))
        {
            error.WriteLine($"Invalid project name '{projectName}'.");
            error.WriteLine("Project name must start with a letter and contain only letters, numbers, '-' or '_'.");
            return false;
        }

        return true;
    }

    private static string? ResolveTemplateDirectory(string templateName)
    {
        static string? FindTemplatesRoot(string startDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "Templates");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                current = current.Parent;
            }
            return null;
        }

        var roots = new List<string?>();
        roots.Add(FindTemplatesRoot(Directory.GetCurrentDirectory()));
        roots.Add(FindTemplatesRoot(AppContext.BaseDirectory));

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var candidate = Path.Combine(root!, templateName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static TemplateWriteStats CopyTemplateTree(
        string sourceRoot,
        string destinationRoot,
        IReadOnlyDictionary<string, string> variables,
        NewCommandOptions options)
    {
        var stats = new TemplateWriteStats();
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (ShouldSkipPath(relative, options))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (ShouldSkipPath(relative, options))
            {
                continue;
            }

            var targetPath = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var content = RenderTemplate(File.ReadAllText(file), variables);

            if (File.Exists(targetPath))
            {
                stats.FilesOverwritten++;
            }

            File.WriteAllText(targetPath, content);
            stats.FilesWritten++;
        }

        return stats;
    }

    private static int GenerateEnvironmentProfiles(string destinationRoot, IReadOnlyDictionary<string, string> variables)
    {
        var envDir = Path.Combine(destinationRoot, "config", "environments");
        Directory.CreateDirectory(envDir);
        WriteEnvironmentProfile(envDir, "dev", "debug", variables);
        WriteEnvironmentProfile(envDir, "test", "test", variables);
        WriteEnvironmentProfile(envDir, "prod", "release", variables);
        return 3;
    }

    private static void WriteEnvironmentProfile(
        string envDir,
        string profile,
        string runtimeMode,
        IReadOnlyDictionary<string, string> variables)
    {
        var serviceName = variables["PROJECT_SLUG"];
        var content = "{\n" +
            $"  \"profile\": \"{profile}\",\n" +
            $"  \"service\": \"{serviceName}\",\n" +
            $"  \"runtimeMode\": \"{runtimeMode}\",\n" +
            "  \"http\": {\n" +
            "    \"port\": 8080,\n" +
            $"    \"baseUrl\": \"http://localhost:8080\"\n" +
            "  },\n" +
            "  \"observability\": {\n" +
            $"    \"enableMetrics\": {(profile == "prod" ? "true" : "false")},\n" +
            "    \"metricsPath\": \"/metrics\",\n" +
            "    \"healthPath\": \"/api/health\",\n" +
            "    \"readinessPath\": \"/api/readiness\"\n" +
            "  }\n" +
            "}\n";
        File.WriteAllText(Path.Combine(envDir, $"{profile}.json"), content);
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateVariables(string templateName, string projectName, NewCommandOptions options)
    {
        var slug = ToKebabCase(projectName);
        var pascal = ToPascalCase(projectName);
        var camel = string.IsNullOrEmpty(pascal)
            ? projectName
            : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PROJECT_NAME"] = projectName,
            ["PROJECT_SLUG"] = slug,
            ["PROJECT_PASCAL"] = pascal,
            ["PROJECT_CAMEL"] = camel,
            ["PROJECT_UPPER"] = projectName.ToUpperInvariant(),
            ["PROJECT_LOWER"] = projectName.ToLowerInvariant(),
            ["TEMPLATE_NAME"] = templateName,
            ["HAS_FRONTEND"] = string.Equals(templateName, "fullstack", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            ["HAS_BACKEND"] = IsGameTemplate(templateName) ? "false" : "true",
            ["LOCAL_FIRST"] = options.LocalFirst && !IsGameTemplate(templateName) ? "true" : "false"
        };
    }

    private static bool IsGameTemplate(string templateName)
    {
        return string.Equals(templateName, "game", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameFullstack(string templateName, NewCommandOptions options)
    {
        return IsGameTemplate(templateName) && options.Fullstack;
    }

    private static void WriteScaffoldFileCount(TextWriter output, TemplateWriteStats stats, bool force)
    {
        if (force)
        {
            output.WriteLine($"Scaffold files written: {stats.FilesWritten} (overwritten: {stats.FilesOverwritten})");
        }
        else
        {
            output.WriteLine($"Scaffold files written: {stats.FilesWritten}");
        }
    }

    private static bool ShouldSkipPath(string relativePath, NewCommandOptions options)
    {
        if (options.IncludeTests)
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        return normalized.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderTemplate(string content, IReadOnlyDictionary<string, string> variables)
    {
        var rendered = RenderConditionalSections(content, variables);
        foreach (var pair in variables.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            rendered = rendered.Replace($"__{pair.Key}__", pair.Value, StringComparison.Ordinal);
            rendered = rendered.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string RenderConditionalSections(string content, IReadOnlyDictionary<string, string> variables)
    {
        return ConditionalSectionRegex.Replace(content, match =>
        {
            var mode = match.Groups[1].Value;
            var openingName = match.Groups[2].Value;
            var body = match.Groups[3].Value;
            var closingName = match.Groups[4].Value;
            if (!openingName.Equals(closingName, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (!variables.TryGetValue(openingName, out var value))
            {
                return string.Empty;
            }

            var isTruthy = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            if (mode == "#")
            {
                return isTruthy ? body : string.Empty;
            }

            return !isTruthy ? body : string.Empty;
        });
    }

    private static string ToKebabCase(string value)
    {
        var sanitized = SanitizeName(value);
        return string.Join("-", SplitWords(sanitized)).ToLowerInvariant();
    }

    private static string ToPascalCase(string value)
    {
        var sanitized = SanitizeName(value);
        var builder = new StringBuilder();
        foreach (var part in SplitWords(sanitized))
        {
            if (part.Length == 0)
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part.Substring(1).ToLowerInvariant());
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> SplitWords(string value)
    {
        return value
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string SanitizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private sealed class TemplateWriteStats
    {
        public int FilesWritten { get; set; }
        public int FilesOverwritten { get; set; }
    }
}
