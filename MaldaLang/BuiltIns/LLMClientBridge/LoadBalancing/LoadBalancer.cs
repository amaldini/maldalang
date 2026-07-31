// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.LoadBalancing;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Load balancer that distributes requests across backends.
/// </summary>
public class LoadBalancer
{
    private readonly Dictionary<int, int> _weights = new();
    private readonly Dictionary<int, int> _activeConnections = new();
    private readonly object _lock = new object();
    private string _strategy = "roundRobin";
    
    /// <summary>
    /// Sets the load balancing strategy.
    /// </summary>
    public void SetStrategy(string strategy)
    {
        lock (_lock)
        {
            _strategy = strategy;
        }
    }
    
    /// <summary>
    /// Sets the weight for a backend (for weighted distribution).
    /// </summary>
    public void SetBackendWeight(int backendIndex, int weight)
    {
        lock (_lock)
        {
            _weights[backendIndex] = weight;
        }
    }
    
    /// <summary>
    /// Gets the next backend based on the current strategy.
    /// </summary>
    public int GetNextBackend(List<int> availableBackends)
    {
        if (availableBackends.Count == 0)
            return -1;
        
        lock (_lock)
        {
            switch (_strategy)
            {
                case "leastConnections":
                    return GetLeastConnectionsBackend(availableBackends);
                
                case "weighted":
                    return GetWeightedBackend(availableBackends);
                
                case "roundRobin":
                default:
                    return availableBackends[0]; // Simple round-robin (caller should track index)
            }
        }
    }
    
    private int GetLeastConnectionsBackend(List<int> availableBackends)
    {
        return availableBackends
            .OrderBy(idx => _activeConnections.GetValueOrDefault(idx, 0))
            .First();
    }
    
    private int GetWeightedBackend(List<int> availableBackends)
    {
        // Weighted random selection
        var totalWeight = availableBackends.Sum(idx => _weights.GetValueOrDefault(idx, 1));
        if (totalWeight == 0)
            return availableBackends[0];
        
        var random = new System.Random();
        var target = random.Next(totalWeight);
        var current = 0;
        
        foreach (var idx in availableBackends)
        {
            current += _weights.GetValueOrDefault(idx, 1);
            if (current > target)
                return idx;
        }
        
        return availableBackends.Last();
    }
    
    /// <summary>
    /// Increments active connection count for a backend.
    /// </summary>
    public void IncrementConnections(int backendIndex)
    {
        lock (_lock)
        {
            _activeConnections[backendIndex] = _activeConnections.GetValueOrDefault(backendIndex, 0) + 1;
        }
    }
    
    /// <summary>
    /// Decrements active connection count for a backend.
    /// </summary>
    public void DecrementConnections(int backendIndex)
    {
        lock (_lock)
        {
            if (_activeConnections.ContainsKey(backendIndex))
            {
                _activeConnections[backendIndex] = System.Math.Max(0, _activeConnections[backendIndex] - 1);
            }
        }
    }
}