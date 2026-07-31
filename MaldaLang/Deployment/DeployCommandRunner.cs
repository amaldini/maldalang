namespace MaldaLang.Deployment;

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.Observability;

public sealed class DeployCommandRunner
{
    private sealed class DeployCommandOptions
    {
        public bool ShowHelp { get; set; }
        public string DeployConfigPath { get; set; } = string.Empty;
        public string ProfilePath { get; set; } = string.Empty;
        public string ObservabilityConfigPath { get; set; } = string.Empty;
    }

    public int Run(string[] args, TextWriter output, TextWriter error, string? workingDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory!;
        var options = ParseOptions(args, root, error);
        if (options == null)
        {
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage(output);
            return 0;
        }

        if (!DeployContractLoader.TryLoad(
            options.DeployConfigPath,
            options.ProfilePath,
            options.ObservabilityConfigPath,
            out var bundle,
            out var contractErrors))
        {
            WriteErrors(error, contractErrors);
            return 1;
        }

        var observabilityErrors = ObservabilityContractValidator.Validate(bundle!);
        if (observabilityErrors.Count > 0)
        {
            WriteErrors(error, observabilityErrors);
            return 1;
        }

        var logger = new StructuredLogger(bundle!.Deploy.Environment, bundle.Profile.Profile, bundle.Observability.IncludeCorrelationId);
        logger.LogInfo(output, "Deploy contract validation completed.", "deploy-contract-validation");

        output.WriteLine("Deploy skeleton mode: no runtime orchestration was executed.");
        output.WriteLine($"Resolved deploy contract: {bundle.DeployConfigPath}");
        output.WriteLine($"Resolved profile contract: {bundle.ProfilePath}");
        output.WriteLine($"Resolved observability contract: {bundle.ObservabilityConfigPath}");
        output.WriteLine($"Service: {bundle.Deploy.ServiceName} | Env: {bundle.Deploy.Environment} | Port: {bundle.Deploy.EffectivePort} | Replicas: {bundle.Deploy.Replicas}");
        output.WriteLine($"Health: {bundle.Deploy.LivenessPath} | Readiness: {bundle.Deploy.ReadinessPath} | Metrics: {(bundle.Profile.EnableMetrics ? bundle.Profile.MetricsPath : "disabled")}");
        return 0;
    }

    private static DeployCommandOptions? ParseOptions(string[] args, string root, TextWriter error)
    {
        var options = new DeployCommandOptions
        {
            DeployConfigPath = Path.Combine(root, "config", "deploy.example.json"),
            ProfilePath = Path.Combine(root, "config", "environments", "prod.json"),
            ObservabilityConfigPath = Path.Combine(root, "config", "observability.example.json")
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "-h" || arg == "--help")
            {
                options.ShowHelp = true;
                continue;
            }

            if (arg == "--config" || arg == "-c")
            {
                if (!TryReadValue(args, ref i, "deploy: --config requires a value.", error, out var value))
                {
                    return null;
                }

                options.DeployConfigPath = ResolvePath(root, value!);
                continue;
            }

            if (arg == "--profile" || arg == "-p")
            {
                if (!TryReadValue(args, ref i, "deploy: --profile requires a value.", error, out var value))
                {
                    return null;
                }

                options.ProfilePath = ResolveProfilePath(root, value!);
                continue;
            }

            if (arg == "--observability" || arg == "-o")
            {
                if (!TryReadValue(args, ref i, "deploy: --observability requires a value.", error, out var value))
                {
                    return null;
                }

                options.ObservabilityConfigPath = ResolvePath(root, value!);
                continue;
            }

            if (arg == "--dry-run")
            {
                // Accepted for forward compatibility; skeleton mode is always safe and non-destructive.
                continue;
            }

            error.WriteLine($"deploy: unknown option '{arg}'.");
            error.WriteLine("Run 'malda deploy --help' for usage.");
            return null;
        }

        return options;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda deploy [options]");
        output.WriteLine("  --config, -c <path>         Deploy contract JSON path (default: config/deploy.example.json)");
        output.WriteLine("  --profile, -p <name|path>   Environment profile name or path (default: prod => config/environments/prod.json)");
        output.WriteLine("  --observability, -o <path>  Observability contract JSON path (default: config/observability.example.json)");
        output.WriteLine("  --dry-run                   Validate contracts only (default behavior)");
        output.WriteLine("  --help, -h                  Show deploy command help");
    }

    private static string ResolveProfilePath(string root, string raw)
    {
        var looksLikePath = raw.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                            raw.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                            raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        if (looksLikePath)
        {
            return ResolvePath(root, raw);
        }

        return Path.Combine(root, "config", "environments", $"{raw}.json");
    }

    private static string ResolvePath(string root, string raw)
    {
        if (Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(raw);
        }

        return Path.GetFullPath(Path.Combine(root, raw));
    }

    private static bool TryReadValue(string[] args, ref int index, string message, TextWriter error, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
        {
            error.WriteLine(message);
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void WriteErrors(TextWriter error, IReadOnlyList<string> errors)
    {
        error.WriteLine("deploy: contract validation failed.");
        foreach (var item in errors)
        {
            error.WriteLine($"  - {item}");
        }
    }
}
