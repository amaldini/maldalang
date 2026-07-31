namespace MaldaLang.Scaffolding;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class NewCommandOptions
{
    public string TemplateName { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public string? ProjectName { get; init; }
    public bool Force { get; init; }
    public bool IncludeTests { get; init; } = true;
    public bool LocalFirst { get; init; }
}

public static class NewCommandOptionsParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        TextWriter error,
        out NewCommandOptions? options)
    {
        options = null;
        if (args.Count < 2)
        {
            WriteUsage(error);
            return false;
        }

        var templateName = args[1].Trim();
        if (string.IsNullOrWhiteSpace(templateName))
        {
            error.WriteLine("Template name cannot be empty.");
            WriteUsage(error);
            return false;
        }

        bool force = false;
        bool includeTests = true;
        bool localFirst = false;
        string? projectName = null;
        string? directory = null;
        var seenFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 2; i < args.Count; i++)
        {
            var token = args[i];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                if (!seenFlags.Add(token))
                {
                    error.WriteLine($"Duplicate option '{token}'.");
                    return false;
                }

                if (string.Equals(token, "--force", StringComparison.OrdinalIgnoreCase))
                {
                    force = true;
                    continue;
                }

                if (string.Equals(token, "--no-tests", StringComparison.OrdinalIgnoreCase))
                {
                    includeTests = false;
                    continue;
                }

                if (string.Equals(token, "--local-first", StringComparison.OrdinalIgnoreCase))
                {
                    localFirst = true;
                    continue;
                }

                if (string.Equals(token, "--name", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        error.WriteLine("Option '--name' requires a value.");
                        return false;
                    }

                    i++;
                    projectName = args[i].Trim();
                    if (string.IsNullOrWhiteSpace(projectName))
                    {
                        error.WriteLine("Option '--name' cannot be empty.");
                        return false;
                    }

                    continue;
                }

                error.WriteLine($"Unknown option '{token}'.");
                WriteUsage(error);
                return false;
            }

            if (directory != null)
            {
                error.WriteLine($"Unexpected extra argument '{token}'.");
                WriteUsage(error);
                return false;
            }

            directory = token;
        }

        var resolvedDirectory = directory;
        if (string.IsNullOrWhiteSpace(resolvedDirectory))
        {
            var baseName = string.IsNullOrWhiteSpace(projectName) ? templateName : projectName!;
            resolvedDirectory = Path.Combine(System.Environment.CurrentDirectory, baseName);
        }

        options = new NewCommandOptions
        {
            TemplateName = templateName,
            DestinationPath = resolvedDirectory!,
            ProjectName = projectName,
            Force = force,
            IncludeTests = includeTests,
            LocalFirst = localFirst
        };

        return true;
    }

    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda new <webapi|fullstack> [directory] [options]");
        output.WriteLine("  Options:");
        output.WriteLine("    --name <project-name>  Override scaffolded project name");
        output.WriteLine("    --force                Overwrite template files in existing directories");
        output.WriteLine("    --local-first          Add SQLite/local-first starter files and migration bootstrap");
        output.WriteLine("    --no-tests             Skip generating test files/directories");
        output.WriteLine("  Examples:");
        output.WriteLine("    malda new webapi my-api");
        output.WriteLine("    malda new webapi my-api --local-first");
        output.WriteLine("    malda new fullstack --name SalesPortal");
        output.WriteLine("    malda new webapi . --force --no-tests");
    }
}
