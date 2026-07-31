// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Singleton service for automatically reporting agent activities to a central dashboard.
/// Agents report their lifecycle events, think() calls, and tool executions without requiring
/// any modifications to MALDA scripts.
/// </summary>
public class AgentDashboardService
{
    private static AgentDashboardService? _instance;
    private static readonly object _lockObject = new object();
    
    private readonly HttpClient _httpClient;
    private readonly string _dashboardUrl;
    private readonly int _processId;
    private readonly bool _enabled;
    
    private AgentDashboardService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(2); // Short timeout to prevent hanging
        
        // Get dashboard URL from environment variable, default to localhost
        var envUrl = Environment.GetEnvironmentVariable("SPL_AGENT_DASHBOARD_URL");
        if (string.IsNullOrWhiteSpace(envUrl))
        {
            _dashboardUrl = "http://localhost:8080/api/agent/status";
        }
        else
        {
            _dashboardUrl = envUrl.TrimEnd('/');
            if (!_dashboardUrl.EndsWith("/api/agent/status"))
            {
                // If URL doesn't end with the endpoint, append it
                _dashboardUrl = _dashboardUrl.TrimEnd('/') + "/api/agent/status";
            }
        }
        
        _processId = Process.GetCurrentProcess().Id;
        _enabled = true; // Always enabled, but will fail gracefully if dashboard is unavailable
    }
    
    public static AgentDashboardService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new AgentDashboardService();
                    }
                }
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Reports when an agent is created/initialized.
    /// </summary>
    public void ReportAgentCreated(string agentName, string role)
    {
        if (!_enabled) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    agentId = agentName,
                    processId = _processId,
                    eventType = "agent_created",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    data = new
                    {
                        role = role
                    }
                };
                
                await SendReportAsync(payload);
            }
            catch
            {
                // Silently ignore errors - dashboard reporting should not affect agent execution
            }
        });
    }
    
    /// <summary>
    /// Reports when an agent performs a think() operation.
    /// </summary>
    public void ReportAgentThink(string agentName, string prompt)
    {
        if (!_enabled) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                // Truncate prompt if too long (max 500 chars for dashboard)
                var promptDisplay = prompt;
                if (promptDisplay.Length > 500)
                {
                    promptDisplay = promptDisplay.Substring(0, 500) + "...";
                }
                
                var payload = new
                {
                    agentId = agentName,
                    processId = _processId,
                    eventType = "think",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    data = new
                    {
                        prompt = promptDisplay,
                        promptLength = prompt.Length
                    }
                };
                
                await SendReportAsync(payload);
            }
            catch
            {
                // Silently ignore errors
            }
        });
    }
    
    /// <summary>
    /// Reports when a tool is called by an agent.
    /// </summary>
    public void ReportToolCall(string agentName, string toolName, bool success, string? errorMessage = null)
    {
        if (!_enabled) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    agentId = agentName,
                    processId = _processId,
                    eventType = "tool_call",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    data = new
                    {
                        toolName = toolName,
                        success = success,
                        errorMessage = errorMessage
                    }
                };
                
                await SendReportAsync(payload);
            }
            catch
            {
                // Silently ignore errors
            }
        });
    }
    
    /// <summary>
    /// Reports Ralph iteration status to the agent dashboard (optional).
    /// </summary>
    public void ReportRalphStatus(string agentName, string phase, int iteration, int maxIter, int prdPercent, bool validationOk, long elapsedMs, int promptTokens, int completionTokens, double costUsd = 0)
    {
        if (!_enabled) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    agentId = agentName,
                    processId = _processId,
                    eventType = "ralph_status",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    data = new
                    {
                        phase,
                        iteration,
                        maxIter,
                        prdPercent,
                        validationOk,
                        elapsedMs,
                        promptTokens,
                        completionTokens,
                        costUsd
                    }
                };

                await SendReportAsync(payload);
            }
            catch
            {
            }
        });
    }

    /// <summary>
    /// Reports when an agent is reset.
    /// </summary>
    public void ReportAgentReset(string agentName)
    {
        if (!_enabled) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    agentId = agentName,
                    processId = _processId,
                    eventType = "agent_reset",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    data = new { }
                };
                
                await SendReportAsync(payload);
            }
            catch
            {
                // Silently ignore errors
            }
        });
    }
    
    private async Task SendReportAsync(object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_dashboardUrl, content);
            
            // We don't check the response - fire and forget
            // If dashboard is unavailable, this will fail silently
        }
        catch
        {
            // Silently ignore all errors (timeout, network error, etc.)
            // Dashboard reporting should never break agent execution
        }
    }
}
