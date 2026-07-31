// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Manages quotas (e.g., token limits per day).
/// </summary>
public class QuotaManager
{
    private readonly ConcurrentDictionary<string, QuotaEntry> _quotas = new();
    private long _limit;
    private TimeSpan _window;
    private readonly object _configLock = new object();
    
    private class QuotaEntry
    {
        public long Used { get; set; }
        public DateTime ResetTime { get; set; }
    }
    
    public QuotaManager(long limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }
    
    /// <summary>
    /// Sets the quota limit.
    /// </summary>
    public void SetQuota(long limit, TimeSpan window)
    {
        lock (_configLock)
        {
            _limit = limit;
            _window = window;
        }
    }
    
    /// <summary>
    /// Checks if the quota allows the given amount.
    /// </summary>
    public bool CheckQuota(long amount, string? userId = null)
    {
        var key = userId ?? "default";
        var now = DateTime.UtcNow;
        
        var entry = _quotas.GetOrAdd(key, _ => new QuotaEntry { Used = 0, ResetTime = now.Add(_window) });
        
        lock (entry)
        {
            // Reset if window has passed
            if (now >= entry.ResetTime)
            {
                entry.Used = 0;
                entry.ResetTime = now.Add(_window);
            }
            
            // Check if adding this amount would exceed quota
            if (entry.Used + amount > _limit)
            {
                return false;
            }
            
            // Reserve the amount
            entry.Used += amount;
            return true;
        }
    }
    
    /// <summary>
    /// Gets the remaining quota.
    /// </summary>
    public long GetRemainingQuota(string? userId = null)
    {
        var key = userId ?? "default";
        var now = DateTime.UtcNow;
        
        if (!_quotas.TryGetValue(key, out var entry))
            return _limit;
        
        lock (entry)
        {
            // Reset if window has passed
            if (now >= entry.ResetTime)
            {
                entry.Used = 0;
                entry.ResetTime = now.Add(_window);
            }
            
            return Math.Max(0, _limit - entry.Used);
        }
    }
    
    /// <summary>
    /// Resets the quota for a user.
    /// </summary>
    public void ResetQuota(string? userId = null)
    {
        var key = userId ?? "default";
        _quotas.TryRemove(key, out _);
    }
}