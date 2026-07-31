namespace MaldaLang.Deployment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public sealed class DeployContract
{
    public string Environment { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int Replicas { get; set; }
    public int EffectivePort { get; set; }
    public string LivenessPath { get; set; } = string.Empty;
    public string ReadinessPath { get; set; } = string.Empty;
    public int HealthIntervalSeconds { get; set; }
}

public sealed class EnvironmentProfileContract
{
    public string Profile { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string RuntimeMode { get; set; } = string.Empty;
    public int HttpPort { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public bool EnableMetrics { get; set; }
    public string MetricsPath { get; set; } = string.Empty;
    public string HealthPath { get; set; } = string.Empty;
    public string ReadinessPath { get; set; } = string.Empty;
}

public sealed class ObservabilityContract
{
    public string LoggingFormat { get; set; } = string.Empty;
    public string LoggingLevel { get; set; } = string.Empty;
    public bool IncludeCorrelationId { get; set; }
    public bool MetricsEnabled { get; set; }
    public string MetricsEndpoint { get; set; } = string.Empty;
    public bool IncludeRuntimeMetrics { get; set; }
}

public sealed class DeployContractBundle
{
    public string DeployConfigPath { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public string ObservabilityConfigPath { get; set; } = string.Empty;
    public DeployContract Deploy { get; set; } = new DeployContract();
    public EnvironmentProfileContract Profile { get; set; } = new EnvironmentProfileContract();
    public ObservabilityContract Observability { get; set; } = new ObservabilityContract();
}

public static class DeployContractLoader
{
    public static bool TryLoad(
        string deployConfigPath,
        string profilePath,
        string observabilityConfigPath,
        out DeployContractBundle? bundle,
        out List<string> errors)
    {
        bundle = null;
        errors = new List<string>();

        var deployConfig = LoadAndParseJson(deployConfigPath, "deploy", errors);
        var profileConfig = LoadAndParseJson(profilePath, "profile", errors);
        var observabilityConfig = LoadAndParseJson(observabilityConfigPath, "observability", errors);

        if (errors.Count > 0)
        {
            return false;
        }

        if (!TryParseDeployContract(deployConfig!.Value, errors, out var deployContract))
        {
            return false;
        }

        if (!TryParseEnvironmentProfile(profileConfig!.Value, errors, out var environmentProfile))
        {
            return false;
        }

        if (!TryParseObservabilityContract(observabilityConfig!.Value, errors, out var observabilityContract))
        {
            return false;
        }

        bundle = new DeployContractBundle
        {
            DeployConfigPath = Path.GetFullPath(deployConfigPath),
            ProfilePath = Path.GetFullPath(profilePath),
            ObservabilityConfigPath = Path.GetFullPath(observabilityConfigPath),
            Deploy = deployContract!,
            Profile = environmentProfile!,
            Observability = observabilityContract!
        };
        return true;
    }

    private static JsonElement? LoadAndParseJson(string path, string contractName, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"[{contractName}] Missing contract file: {Path.GetFullPath(path)}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            errors.Add($"[{contractName}] Invalid JSON in {Path.GetFullPath(path)}: {ex.Message}");
            return null;
        }
    }

    private static bool TryParseDeployContract(JsonElement root, List<string> errors, out DeployContract? contract)
    {
        contract = null;
        if (!TryGetObject(root, "deployment", "deploy", errors, out var deployment))
        {
            return false;
        }

        if (!TryGetObject(root, "healthChecks", "deploy", errors, out var healthChecks))
        {
            return false;
        }

        var parsed = new DeployContract();
        var ok = true;
        ok &= TryGetNonEmptyString(deployment, "environment", "deploy.deployment", errors, out var environment);
        ok &= TryGetNonEmptyString(deployment, "serviceName", "deploy.deployment", errors, out var serviceName);
        ok &= TryGetPositiveInt(deployment, "replicas", "deploy.deployment", errors, out var replicas);

        // webapi templates use deployment.port, fullstack templates use deployment.backend.port
        if (!TryGetPositiveInt(deployment, "port", "deploy.deployment", errors, out var effectivePort))
        {
            if (!TryGetObject(deployment, "backend", "deploy.deployment", errors, out var backend) ||
                !TryGetPositiveInt(backend, "port", "deploy.deployment.backend", errors, out effectivePort))
            {
                ok = false;
            }
        }

        ok &= TryGetRoutePath(healthChecks, "livenessPath", "deploy.healthChecks", errors, out var livenessPath);
        ok &= TryGetRoutePath(healthChecks, "readinessPath", "deploy.healthChecks", errors, out var readinessPath);
        ok &= TryGetPositiveInt(healthChecks, "intervalSeconds", "deploy.healthChecks", errors, out var intervalSeconds);

        if (ok && string.Equals(livenessPath, readinessPath, StringComparison.Ordinal))
        {
            errors.Add("[deploy.healthChecks] livenessPath and readinessPath must be different.");
            ok = false;
        }

        if (ok)
        {
            parsed.Environment = environment;
            parsed.ServiceName = serviceName;
            parsed.Replicas = replicas;
            parsed.EffectivePort = effectivePort;
            parsed.LivenessPath = livenessPath;
            parsed.ReadinessPath = readinessPath;
            parsed.HealthIntervalSeconds = intervalSeconds;
        }

        contract = ok ? parsed : null;
        return ok;
    }

    private static bool TryParseEnvironmentProfile(JsonElement root, List<string> errors, out EnvironmentProfileContract? profile)
    {
        profile = null;
        var parsed = new EnvironmentProfileContract();
        var ok = true;

        ok &= TryGetNonEmptyString(root, "profile", "profile", errors, out var profileName);
        ok &= TryGetNonEmptyString(root, "service", "profile", errors, out var serviceName);
        ok &= TryGetNonEmptyString(root, "runtimeMode", "profile", errors, out var runtimeMode);

        if (!TryGetObject(root, "http", "profile", errors, out var http))
        {
            return false;
        }

        ok &= TryGetPositiveInt(http, "port", "profile.http", errors, out var httpPort);
        ok &= TryGetNonEmptyString(http, "baseUrl", "profile.http", errors, out var baseUrl);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
        {
            errors.Add("[profile.http] baseUrl must be an absolute http or https URL.");
            ok = false;
        }

        if (!TryGetObject(root, "observability", "profile", errors, out var observability))
        {
            return false;
        }

        ok &= TryGetBool(observability, "enableMetrics", "profile.observability", errors, out var enableMetrics);
        ok &= TryGetRoutePath(observability, "metricsPath", "profile.observability", errors, out var metricsPath);
        ok &= TryGetRoutePath(observability, "healthPath", "profile.observability", errors, out var healthPath);
        ok &= TryGetRoutePath(observability, "readinessPath", "profile.observability", errors, out var readinessPath);

        if (ok && string.Equals(healthPath, readinessPath, StringComparison.Ordinal))
        {
            errors.Add("[profile.observability] healthPath and readinessPath must be different.");
            ok = false;
        }

        if (ok)
        {
            parsed.Profile = profileName;
            parsed.Service = serviceName;
            parsed.RuntimeMode = runtimeMode;
            parsed.HttpPort = httpPort;
            parsed.BaseUrl = baseUrl;
            parsed.EnableMetrics = enableMetrics;
            parsed.MetricsPath = metricsPath;
            parsed.HealthPath = healthPath;
            parsed.ReadinessPath = readinessPath;
        }

        profile = ok ? parsed : null;
        return ok;
    }

    private static bool TryParseObservabilityContract(JsonElement root, List<string> errors, out ObservabilityContract? observabilityContract)
    {
        observabilityContract = null;
        if (!TryGetObject(root, "logging", "observability", errors, out var logging))
        {
            return false;
        }
        if (!TryGetObject(root, "metrics", "observability", errors, out var metrics))
        {
            return false;
        }

        var parsed = new ObservabilityContract();
        var ok = true;
        ok &= TryGetNonEmptyString(logging, "format", "observability.logging", errors, out var format);
        ok &= TryGetNonEmptyString(logging, "level", "observability.logging", errors, out var level);
        ok &= TryGetBool(logging, "includeCorrelationId", "observability.logging", errors, out var includeCorrelationId);
        ok &= TryGetBool(metrics, "enabled", "observability.metrics", errors, out var enabled);
        ok &= TryGetRoutePath(metrics, "endpoint", "observability.metrics", errors, out var endpoint);
        ok &= TryGetBool(metrics, "includeRuntime", "observability.metrics", errors, out var includeRuntime);

        if (ok)
        {
            parsed.LoggingFormat = format;
            parsed.LoggingLevel = level;
            parsed.IncludeCorrelationId = includeCorrelationId;
            parsed.MetricsEnabled = enabled;
            parsed.MetricsEndpoint = endpoint;
            parsed.IncludeRuntimeMetrics = includeRuntime;
        }

        observabilityContract = ok ? parsed : null;
        return ok;
    }

    private static bool TryGetObject(JsonElement source, string propertyName, string scope, List<string> errors, out JsonElement value)
    {
        value = default;
        if (!source.TryGetProperty(propertyName, out var found))
        {
            errors.Add($"[{scope}] Missing required object '{propertyName}'.");
            return false;
        }

        if (found.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"[{scope}] '{propertyName}' must be a JSON object.");
            return false;
        }

        value = found;
        return true;
    }

    private static bool TryGetNonEmptyString(JsonElement source, string propertyName, string scope, List<string> errors, out string value)
    {
        value = string.Empty;
        if (!source.TryGetProperty(propertyName, out var found) || found.ValueKind != JsonValueKind.String)
        {
            errors.Add($"[{scope}] '{propertyName}' must be a non-empty string.");
            return false;
        }

        value = found.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"[{scope}] '{propertyName}' must be a non-empty string.");
            return false;
        }

        return true;
    }

    private static bool TryGetPositiveInt(JsonElement source, string propertyName, string scope, List<string> errors, out int value)
    {
        value = 0;
        if (!source.TryGetProperty(propertyName, out var found) || found.ValueKind != JsonValueKind.Number || !found.TryGetInt32(out value) || value <= 0)
        {
            errors.Add($"[{scope}] '{propertyName}' must be a positive integer.");
            return false;
        }

        return true;
    }

    private static bool TryGetBool(JsonElement source, string propertyName, string scope, List<string> errors, out bool value)
    {
        value = false;
        if (!source.TryGetProperty(propertyName, out var found) || (found.ValueKind != JsonValueKind.True && found.ValueKind != JsonValueKind.False))
        {
            errors.Add($"[{scope}] '{propertyName}' must be a boolean.");
            return false;
        }

        value = found.GetBoolean();
        return true;
    }

    private static bool TryGetRoutePath(JsonElement source, string propertyName, string scope, List<string> errors, out string value)
    {
        value = string.Empty;
        if (!TryGetNonEmptyString(source, propertyName, scope, errors, out value))
        {
            return false;
        }

        if (!IsValidRoutePath(value))
        {
            errors.Add($"[{scope}] '{propertyName}' must start with '/' and cannot contain whitespace.");
            return false;
        }

        return true;
    }

    private static bool IsValidRoutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in path)
        {
            if (char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return true;
    }
}
