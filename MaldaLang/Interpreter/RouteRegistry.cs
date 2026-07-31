// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.BuiltIns;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

public class RouteMetadata
{
    public string? GroupPrefix { get; }
    public string? VersionPrefix { get; }
    public List<string> MiddlewareFunctionNames { get; }
    public RuntimeValue ValidationSchema { get; }

    public RouteMetadata(
        string? groupPrefix = null,
        string? versionPrefix = null,
        List<string>? middlewareFunctionNames = null,
        RuntimeValue? validationSchema = null)
    {
        GroupPrefix = groupPrefix;
        VersionPrefix = versionPrefix;
        MiddlewareFunctionNames = middlewareFunctionNames ?? new List<string>();
        ValidationSchema = validationSchema ?? RuntimeValue.Null();
    }
}

public class Route
{
    public string Method { get; }
    public string PathPattern { get; }
    public FunctionValue? Function { get; }  // Nullable for transpiled routes
    public string FunctionName { get; }  // Store function name for lookup in new interpreter
    public List<string> ParameterNames { get; }
    public List<Decorator>? ParameterDecorators { get; }
    public List<string> PathParameterNames { get; }  // Names from {param} placeholders
    public RouteMetadata Metadata { get; }
    
    public Route(
        string method,
        string pathPattern,
        FunctionValue? function,
        string functionName,
        List<string> parameterNames,
        List<Decorator>? parameterDecorators,
        RouteMetadata? metadata = null)
    {
        Method = method;
        PathPattern = pathPattern;
        Function = function;
        FunctionName = functionName;
        ParameterNames = parameterNames;
        ParameterDecorators = parameterDecorators;
        PathParameterNames = ExtractPathParameterNames(pathPattern);
        Metadata = metadata ?? new RouteMetadata();
    }
    
    private List<string> ExtractPathParameterNames(string pattern)
    {
        var names = new List<string>();
        var regex = new Regex(@"\{([^}]+)\}");
        var matches = regex.Matches(pattern);
        foreach (Match match in matches)
        {
            names.Add(match.Groups[1].Value);
        }
        return names;
    }
}

public class RouteRegistry
{
    private readonly ConcurrentBag<Route> _routes = new();
    
    public void RegisterRoute(string method, string path, FunctionValue function, string functionName, List<string> paramNames, List<Decorator>? paramDecorators)
    {
        RegisterRoute(method, path, function, functionName, paramNames, paramDecorators, null);
    }

    public void RegisterRoute(
        string method,
        string path,
        FunctionValue function,
        string functionName,
        List<string> paramNames,
        List<Decorator>? paramDecorators,
        RouteMetadata? metadata)
    {
        _routes.Add(new Route(method, path, function, functionName, paramNames, paramDecorators, metadata));
    }
    
    public void RegisterTranspiledRoute(string method, string path, string functionName, List<string> paramNames, List<Decorator>? paramDecorators)
    {
        RegisterTranspiledRoute(method, path, functionName, paramNames, paramDecorators, null);
    }

    public void RegisterTranspiledRoute(
        string method,
        string path,
        string functionName,
        List<string> paramNames,
        List<Decorator>? paramDecorators,
        RouteMetadata? metadata)
    {
        _routes.Add(new Route(method, path, null, functionName, paramNames, paramDecorators, metadata));
    }
    
    public bool MatchRoute(string method, string path, out Route? route, out Dictionary<string, string> pathParams)
    {
        route = null;
        pathParams = new Dictionary<string, string>();

        var effectiveMethod = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? "GET"
            : method;
        
        // Try exact match first
        var exactMatch = _routes.FirstOrDefault(r => r.Method == effectiveMethod && r.PathPattern == path);
        if (exactMatch != null)
        {
            route = exactMatch;
            return true;
        }
        
        // Try pattern match
        foreach (var r in _routes)
        {
            if (r.Method != effectiveMethod) continue;
            
            if (MatchPathPattern(r.PathPattern, path, out var extractedParams))
            {
                route = r;
                pathParams = extractedParams;
                return true;
            }
        }
        
        return false;
    }
    
    private bool MatchPathPattern(string pattern, string path, out Dictionary<string, string> pathParams)
    {
        pathParams = new Dictionary<string, string>();
        
        // Convert pattern to regex: /api/users/{id} -> ^/api/users/([^/]+)$
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\{", "{").Replace("\\}", "}") + "$";
        regexPattern = Regex.Replace(regexPattern, @"\{([^}]+)\}", @"([^/]+)");
        
        var regex = new Regex(regexPattern);
        var match = regex.Match(path);
        
        if (!match.Success)
            return false;
        
        // Extract parameter names from pattern
        var paramNameRegex = new Regex(@"\{([^}]+)\}");
        var paramNames = paramNameRegex.Matches(pattern).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        
        // Extract values from path
        for (int i = 0; i < paramNames.Count && i + 1 < match.Groups.Count; i++)
        {
            pathParams[paramNames[i]] = match.Groups[i + 1].Value;
        }
        
        return true;
    }
    
    public Dictionary<string, string> ExtractQueryParams(string queryString)
    {
        var queryParams = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(queryString))
            return queryParams;
        
        // Remove leading '?' if present
        if (queryString.StartsWith("?"))
            queryString = queryString.Substring(1);
        
        var pairs = queryString.Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                queryParams[key] = value;
            }
        }
        
        return queryParams;
    }
    
    public void ValidateRouteConflicts()
    {
        var conflicts = new List<string>();
        var seen = new HashSet<string>();
        
        foreach (var route in _routes)
        {
            var key = $"{route.Method}:{route.PathPattern}";
            if (seen.Contains(key))
            {
                conflicts.Add($"Duplicate route: {route.Method} {route.PathPattern}");
            }
            seen.Add(key);
        }
        
        if (conflicts.Count > 0)
        {
            throw new Exception($"Route conflicts detected:\n{string.Join("\n", conflicts)}");
        }
    }
    
    public List<Route> GetAllRoutes()
    {
        return new List<Route>(_routes);
    }
    
    public string GetRoutesSummary()
    {
        var routesList = _routes.ToList();
        if (routesList.Count == 0)
            return "No routes registered";
        
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"Registered {routesList.Count} route(s):");
        foreach (var route in routesList)
        {
            summary.AppendLine($"  {route.Method} {route.PathPattern}");
        }
        return summary.ToString();
    }
}