// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Failover;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Handles failover logic with health checking and circuit breaker pattern.
/// </summary>
public class FailoverHandler
{
    private readonly List<BackendInfo> _backends = new();
    private readonly object _lock = new object();
    private int _maxRetries = 3;
    private TimeSpan _backoffBase = TimeSpan.FromSeconds(1);
    private bool _healthCheckEnabled = false;
    private TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);
    private Timer? _healthCheckTimer;
    
    public class BackendInfo
    {
        public int Index { get; set; }
        public bool IsHealthy { get; set; } = true;
        public DateTime LastFailure { get; set; }
        public int FailureCount { get; set; }
        public int Priority { get; set; }
    }
    
    public FailoverHandler()
    {
    }
    
    /// <summary>
    /// Adds a backend with a priority (lower number = higher priority).
    /// </summary>
    public void AddBackend(int index, int priority = 0)
    {
        lock (_lock)
        {
            _backends.Add(new BackendInfo
            {
                Index = index,
                Priority = priority,
                IsHealthy = true
            });
        }
    }
    
    /// <summary>
    /// Sets the retry policy.
    /// </summary>
    public void SetRetryPolicy(int maxAttempts, string backoffType = "exponential")
    {
        _maxRetries = maxAttempts;
        // backoffType can be "exponential" or "linear" - for now we use exponential
    }
    
    /// <summary>
    /// Enables health checking.
    /// </summary>
    public void EnableHealthCheck(TimeSpan interval)
    {
        _healthCheckEnabled = true;
        _healthCheckInterval = interval;
        
        // Start health check timer
        _healthCheckTimer = new Timer(PerformHealthCheck, null, interval, interval);
    }
    
    /// <summary>
    /// Gets healthy backends ordered by priority.
    /// </summary>
    public List<int> GetHealthyBackends()
    {
        lock (_lock)
        {
            return _backends
                .Where(b => b.IsHealthy)
                .OrderBy(b => b.Priority)
                .ThenBy(b => b.Index)
                .Select(b => b.Index)
                .ToList();
        }
    }
    
    /// <summary>
    /// Records a failure for a backend.
    /// </summary>
    public void RecordFailure(int backendIndex)
    {
        lock (_lock)
        {
            var backend = _backends.FirstOrDefault(b => b.Index == backendIndex);
            if (backend != null)
            {
                backend.FailureCount++;
                backend.LastFailure = DateTime.UtcNow;
                
                // Mark as unhealthy if too many failures
                if (backend.FailureCount >= 3)
                {
                    backend.IsHealthy = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Records a success for a backend.
    /// </summary>
    public void RecordSuccess(int backendIndex)
    {
        lock (_lock)
        {
            var backend = _backends.FirstOrDefault(b => b.Index == backendIndex);
            if (backend != null)
            {
                backend.FailureCount = 0;
                backend.IsHealthy = true;
            }
        }
    }
    
    /// <summary>
    /// Calculates backoff delay for retry attempt.
    /// </summary>
    public TimeSpan GetBackoffDelay(int attemptNumber)
    {
        // Exponential backoff: 1s, 2s, 4s, 8s...
        var delaySeconds = Math.Pow(2, attemptNumber - 1);
        return TimeSpan.FromSeconds(delaySeconds);
    }
    
    private void PerformHealthCheck(object? state)
    {
        // Health check implementation would go here
        // For now, we just reset failure counts periodically
        lock (_lock)
        {
            foreach (var backend in _backends)
            {
                // Reset failure count if enough time has passed
                if (backend.LastFailure != default && 
                    DateTime.UtcNow - backend.LastFailure > TimeSpan.FromMinutes(5))
                {
                    backend.FailureCount = 0;
                    if (!backend.IsHealthy)
                    {
                        backend.IsHealthy = true; // Try again
                    }
                }
            }
        }
    }
}