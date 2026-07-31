// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Rate limiter that enforces request rate limits using a sliding window algorithm.
/// </summary>
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestHistory = new();
    private int _limit;
    private TimeSpan _window;
    private readonly object _configLock = new object();
    
    public RateLimiter(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }
    
    /// <summary>
    /// Sets the rate limit.
    /// </summary>
    public void SetRateLimit(int limit, TimeSpan window)
    {
        lock (_configLock)
        {
            _limit = limit;
            _window = window;
        }
    }
    
    /// <summary>
    /// Checks if a request is allowed for the given user/identifier.
    /// </summary>
    public bool CheckRateLimit(string? userId = null)
    {
        var key = userId ?? "default";
        var now = DateTime.UtcNow;
        int limitSnapshot;
        TimeSpan windowSnapshot;
        lock (_configLock)
        {
            limitSnapshot = _limit;
            windowSnapshot = _window;
        }
        
        var queue = _requestHistory.GetOrAdd(key, _ => new Queue<DateTime>());
        
        lock (queue)
        {
            // Remove old entries outside the window
            while (queue.Count > 0 && (now - queue.Peek()) > windowSnapshot)
            {
                queue.Dequeue();
            }
            
            // Check if we're at the limit
            if (queue.Count >= limitSnapshot)
            {
                return false;
            }
            
            // Add current request
            queue.Enqueue(now);
            return true;
        }
    }
    
    /// <summary>
    /// Gets the number of requests remaining in the current window.
    /// </summary>
    public int GetRemainingRequests(string? userId = null)
    {
        var key = userId ?? "default";
        var now = DateTime.UtcNow;
        int limitSnapshot;
        TimeSpan windowSnapshot;
        lock (_configLock)
        {
            limitSnapshot = _limit;
            windowSnapshot = _window;
        }
        
        if (!_requestHistory.TryGetValue(key, out var queue))
            return limitSnapshot;
        
        lock (queue)
        {
            // Remove old entries
            while (queue.Count > 0 && (now - queue.Peek()) > windowSnapshot)
            {
                queue.Dequeue();
            }
            
            return Math.Max(0, limitSnapshot - queue.Count);
        }
    }

    /// <summary>
    /// Returns the current rate-limit window size in seconds.
    /// </summary>
    public int GetWindowSeconds()
    {
        lock (_configLock)
        {
            return Math.Max(1, (int)Math.Ceiling(_window.TotalSeconds));
        }
    }

    /// <summary>
    /// Returns a Retry-After value in seconds for the given key.
    /// Returns 0 when no wait is required.
    /// </summary>
    public int GetRetryAfterSeconds(string? userId = null)
    {
        var key = userId ?? "default";
        var now = DateTime.UtcNow;
        int limitSnapshot;
        TimeSpan windowSnapshot;
        lock (_configLock)
        {
            limitSnapshot = _limit;
            windowSnapshot = _window;
        }

        if (!_requestHistory.TryGetValue(key, out var queue))
        {
            return 0;
        }

        lock (queue)
        {
            while (queue.Count > 0 && (now - queue.Peek()) > windowSnapshot)
            {
                queue.Dequeue();
            }

            if (queue.Count < limitSnapshot || queue.Count == 0)
            {
                return 0;
            }

            var oldest = queue.Peek();
            var retryAfter = windowSnapshot - (now - oldest);
            if (retryAfter <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }
    }
    
    /// <summary>
    /// Resets the rate limit for a user.
    /// </summary>
    public void ResetRateLimit(string? userId = null)
    {
        var key = userId ?? "default";
        _requestHistory.TryRemove(key, out _);
    }
}