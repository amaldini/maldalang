namespace MaldaLang.Observability;

using System;
using System.Collections.Generic;
using MaldaLang.Deployment;

public static class ObservabilityContractValidator
{
    public static List<string> Validate(DeployContractBundle bundle)
    {
        var errors = new List<string>();
        ValidateHealthReadinessWiring(bundle, errors);
        ValidateStructuredLoggingBaseline(bundle, errors);
        ValidateMetricsContract(bundle, errors);
        return errors;
    }

    private static void ValidateHealthReadinessWiring(DeployContractBundle bundle, List<string> errors)
    {
        if (!string.Equals(bundle.Deploy.LivenessPath, bundle.Profile.HealthPath, StringComparison.Ordinal))
        {
            errors.Add($"[health] deploy.healthChecks.livenessPath ('{bundle.Deploy.LivenessPath}') must match profile.observability.healthPath ('{bundle.Profile.HealthPath}').");
        }

        if (!string.Equals(bundle.Deploy.ReadinessPath, bundle.Profile.ReadinessPath, StringComparison.Ordinal))
        {
            errors.Add($"[readiness] deploy.healthChecks.readinessPath ('{bundle.Deploy.ReadinessPath}') must match profile.observability.readinessPath ('{bundle.Profile.ReadinessPath}').");
        }
    }

    private static void ValidateStructuredLoggingBaseline(DeployContractBundle bundle, List<string> errors)
    {
        var format = bundle.Observability.LoggingFormat.Trim().ToLowerInvariant();
        if (format != "json")
        {
            errors.Add("[logging] observability.logging.format must be 'json' for structured baseline.");
        }

        if (string.IsNullOrWhiteSpace(bundle.Observability.LoggingLevel))
        {
            errors.Add("[logging] observability.logging.level must be provided.");
        }
    }

    private static void ValidateMetricsContract(DeployContractBundle bundle, List<string> errors)
    {
        if (!bundle.Observability.MetricsEnabled && bundle.Profile.EnableMetrics)
        {
            errors.Add("[metrics] profile.observability.enableMetrics is true, but observability.metrics.enabled is false.");
        }

        if (bundle.Observability.MetricsEnabled &&
            !string.Equals(bundle.Observability.MetricsEndpoint, bundle.Profile.MetricsPath, StringComparison.Ordinal))
        {
            errors.Add($"[metrics] observability.metrics.endpoint ('{bundle.Observability.MetricsEndpoint}') must match profile.observability.metricsPath ('{bundle.Profile.MetricsPath}').");
        }
    }
}
