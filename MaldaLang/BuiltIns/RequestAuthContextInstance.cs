// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

public class RequestAuthContextInstance : ObjectInstance
{
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _cookies;

    public bool IsVerified { get; private set; }
    public string VerifiedSubject { get; private set; } = string.Empty;
    public bool HasVerifiedSubject => IsVerified && !string.IsNullOrWhiteSpace(VerifiedSubject);
    public RuntimeValue Claims { get; private set; } = RuntimeValue.Null();
    public string BearerToken { get; private set; } = string.Empty;

    public RequestAuthContextInstance(
        string verifiedSubject,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? cookies = null) : base(null)
    {
        _headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(verifiedSubject))
        {
            IsVerified = true;
            VerifiedSubject = verifiedSubject.Trim();
        }
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "verified")
        {
            return RuntimeValue.Boolean(IsVerified);
        }

        if (name == "sub" || name == "subject")
        {
            return HasVerifiedSubject ? RuntimeValue.String(VerifiedSubject) : RuntimeValue.Null();
        }

        if (name == "claims")
        {
            return Claims;
        }

        if (name == "roles")
        {
            return RuntimeValue.Array(BuildClaimArray("role", "roles"));
        }

        if (name == "permissions")
        {
            return RuntimeValue.Array(BuildClaimArray("permission", "permissions", "scope", "scp"));
        }

        if (name is "token" or "bearerToken")
        {
            return string.IsNullOrWhiteSpace(BearerToken) ? RuntimeValue.Null() : RuntimeValue.String(BearerToken);
        }

        if (name is
            "setVerifiedSub" or "setAnonymous" or "clear" or
            "authenticateBearerJwt" or "authenticateCookieJwt" or
            "claim" or "hasClaim" or
            "hasRole" or "requireRole" or
            "hasPermission" or "requirePermission" or
            "requireVerified")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on Request auth context.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "setVerifiedSub":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.setVerifiedSub() expects 1 string argument");
                }

                var sub = args[0].AsString().Trim();
                if (string.IsNullOrWhiteSpace(sub))
                {
                    throw new Exception("req.auth.setVerifiedSub() subject cannot be empty");
                }

                SetVerifiedPrincipal(sub, RuntimeValue.Null(), string.Empty);
                return RuntimeValue.Object(this);

            case "setAnonymous":
            case "clear":
                if (args.Count != 0)
                {
                    throw new Exception($"req.auth.{methodName}() expects 0 arguments");
                }
                ClearPrincipal();
                return RuntimeValue.Object(this);

            case "authenticateBearerJwt":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.authenticateBearerJwt() expects 1 string secret argument");
                }

                return AuthenticateBearerJwt(args[0].AsString());

            case "authenticateCookieJwt":
                if (args.Count < 2 || args.Count > 3 ||
                    args[0].Type != ValueType.String ||
                    args[1].Type != ValueType.String ||
                    (args.Count == 3 && args[2].Type != ValueType.String))
                {
                    throw new Exception("req.auth.authenticateCookieJwt() expects cookieName, jwtSecret, and optional cookieSecret");
                }

                return AuthenticateCookieJwt(
                    args[0].AsString(),
                    args[1].AsString(),
                    args.Count == 3 ? args[2].AsString() : string.Empty);

            case "claim":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.claim() expects claimName and optional defaultValue");
                }

                return GetClaimOrDefault(
                    args[0].AsString(),
                    args.Count == 2 ? args[1] : RuntimeValue.Null());

            case "hasClaim":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasClaim() expects 1 string claimName argument");
                }

                return RuntimeValue.Boolean(HasClaim(args[0].AsString()));

            case "hasRole":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasRole() expects 1 string role argument");
                }

                return RuntimeValue.Boolean(HasRole(args[0].AsString()));

            case "requireRole":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.requireRole() expects 1 string role argument");
                }

                RequireVerified();
                var requiredRole = args[0].AsString();
                if (!HasRole(requiredRole))
                {
                    throw new WebRuntimeException(403, "Forbidden", $"Role '{requiredRole}' is required.");
                }

                return RuntimeValue.Object(this);

            case "hasPermission":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasPermission() expects 1 string permission argument");
                }

                return RuntimeValue.Boolean(HasPermission(args[0].AsString()));

            case "requirePermission":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.requirePermission() expects 1 string permission argument");
                }

                RequireVerified();
                var requiredPermission = args[0].AsString();
                if (!HasPermission(requiredPermission))
                {
                    throw new WebRuntimeException(403, "Forbidden", $"Permission '{requiredPermission}' is required.");
                }

                return RuntimeValue.Object(this);

            case "requireVerified":
                if (args.Count != 0)
                {
                    throw new Exception("req.auth.requireVerified() expects 0 arguments");
                }

                RequireVerified();
                return RuntimeValue.Object(this);

            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    public override IEnumerable<string> GetAllKeys()
    {
        foreach (var key in new[]
                 {
                     "verified", "sub", "subject", "claims", "roles", "permissions", "token", "bearerToken",
                     "setVerifiedSub", "setAnonymous", "clear",
                     "authenticateBearerJwt", "authenticateCookieJwt",
                     "claim", "hasClaim",
                     "hasRole", "requireRole",
                     "hasPermission", "requirePermission",
                     "requireVerified"
                 })
        {
            yield return key;
        }
    }

    private RuntimeValue AuthenticateBearerJwt(string secret)
    {
        if (!WebRuntimeHelpers.TryGetHeaderValue(_headers, "Authorization", out var rawAuthorization) ||
            string.IsNullOrWhiteSpace(rawAuthorization))
        {
            throw new WebRuntimeException(401, "MissingToken", "Missing bearer token.");
        }

        var headerValue = rawAuthorization.Trim();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid Authorization header format.");
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new WebRuntimeException(401, "MissingToken", "Missing bearer token.");
        }

        return AuthenticateJwtToken(token, secret);
    }

    private RuntimeValue AuthenticateCookieJwt(string cookieName, string jwtSecret, string cookieSecret)
    {
        if (string.IsNullOrWhiteSpace(cookieName))
        {
            throw new Exception("req.auth.authenticateCookieJwt() cookieName cannot be empty");
        }

        if (!TryGetDictionaryValue(_cookies, cookieName, out var rawCookie) || string.IsNullOrWhiteSpace(rawCookie))
        {
            throw new WebRuntimeException(401, "MissingToken", $"Missing auth cookie '{cookieName}'.");
        }

        var cookieToken = SafeUrlDecode(rawCookie.Trim());
        if (!string.IsNullOrWhiteSpace(cookieSecret))
        {
            if (!WebRuntimeHelpers.TryReadSecureCookieValue(cookieToken, cookieSecret, out cookieToken) ||
                string.IsNullOrWhiteSpace(cookieToken))
            {
                throw new WebRuntimeException(401, "InvalidToken", $"Invalid auth cookie '{cookieName}'.");
            }
        }

        return AuthenticateJwtToken(cookieToken, jwtSecret);
    }

    private RuntimeValue AuthenticateJwtToken(string token, string secret)
    {
        var claims = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.String(token),
                RuntimeValue.String(secret)
            },
            null!);

        if (claims.Type != ValueType.Object || claims.AsObject() is not JsonObject claimsObject)
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");
        }

        var subject = claimsObject.Get("sub", null);
        if (subject.Type != ValueType.String || string.IsNullOrWhiteSpace(subject.AsString()))
        {
            throw new WebRuntimeException(401, "InvalidToken", "JWT subject claim is required.");
        }

        SetVerifiedPrincipal(subject.AsString().Trim(), claims, token);
        return claims;
    }

    private void RequireVerified()
    {
        if (!HasVerifiedSubject)
        {
            throw new WebRuntimeException(401, "Unauthorized", "Authentication required.");
        }
    }

    private RuntimeValue GetClaimOrDefault(string claimName, RuntimeValue defaultValue)
    {
        return TryGetClaimValue(claimName, out var claimValue) && claimValue.Type != ValueType.Null
            ? claimValue
            : defaultValue;
    }

    private bool HasClaim(string claimName)
    {
        return TryGetClaimValue(claimName, out var claimValue) && claimValue.Type != ValueType.Null;
    }

    private bool HasRole(string role)
    {
        return ClaimListContains(role, "role", "roles");
    }

    private bool HasPermission(string permission)
    {
        return ClaimListContains(permission, "permission", "permissions", "scope", "scp");
    }

    private bool ClaimListContains(string candidate, params string[] claimNames)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var value in EnumerateClaimStringValues(claimNames))
        {
            if (value.Equals(candidate.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private ArrayInstance BuildClaimArray(params string[] claimNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<RuntimeValue>();
        foreach (var value in EnumerateClaimStringValues(claimNames))
        {
            if (seen.Add(value))
            {
                values.Add(RuntimeValue.String(value));
            }
        }

        return new ArrayInstance(values);
    }

    private IEnumerable<string> EnumerateClaimStringValues(params string[] claimNames)
    {
        foreach (var claimName in claimNames)
        {
            if (!TryGetClaimValue(claimName, out var claimValue) || claimValue.Type == ValueType.Null)
            {
                continue;
            }

            foreach (var value in ExpandClaimValues(claimValue))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> ExpandClaimValues(RuntimeValue claimValue)
    {
        if (claimValue.Type == ValueType.String)
        {
            foreach (var part in claimValue.AsString().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }

            yield break;
        }

        if (claimValue.Type != ValueType.Array)
        {
            yield break;
        }

        foreach (var item in claimValue.AsArray())
        {
            if (item.Type == ValueType.String && !string.IsNullOrWhiteSpace(item.AsString()))
            {
                yield return item.AsString();
            }
        }
    }

    private bool TryGetClaimValue(string claimName, out RuntimeValue claimValue)
    {
        claimValue = RuntimeValue.Null();
        if (string.IsNullOrWhiteSpace(claimName) ||
            Claims.Type != ValueType.Object ||
            Claims.AsObject() is not JsonObject claimsObject)
        {
            return false;
        }

        var direct = claimsObject.Get(claimName, null);
        if (direct.Type != ValueType.Null)
        {
            claimValue = direct;
            return true;
        }

        foreach (var kvp in claimsObject.GetProperties())
        {
            if (kvp.Key.Equals(claimName, StringComparison.OrdinalIgnoreCase))
            {
                claimValue = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDictionaryValue(Dictionary<string, string> source, string key, out string value)
    {
        if (source.TryGetValue(key, out value!))
        {
            return true;
        }

        foreach (var kvp in source)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string SafeUrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }

    private void SetVerifiedPrincipal(string subject, RuntimeValue claims, string bearerToken)
    {
        IsVerified = true;
        VerifiedSubject = subject;
        Claims = claims;
        BearerToken = bearerToken;
    }

    private void ClearPrincipal()
    {
        IsVerified = false;
        VerifiedSubject = string.Empty;
        Claims = RuntimeValue.Null();
        BearerToken = string.Empty;
    }
}
