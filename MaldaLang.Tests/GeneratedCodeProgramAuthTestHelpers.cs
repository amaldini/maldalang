using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

namespace GeneratedCode;

public static class Program
{
    public const string JwtSecret = "sprint3-test-secret";

    public static Task<object> AuthGuardMiddleware(object requestObj, object responseObj, object nextObj)
    {
        var request = requestObj as RequestContextInstance
            ?? throw new WebRuntimeException(401, "InvalidToken", "Invalid request context.");
        _ = responseObj as ResponseContextInstance
            ?? throw new WebRuntimeException(401, "InvalidToken", "Invalid response context.");

        var headers = request.Get("headers", null).AsObject() as JsonObject
            ?? throw new WebRuntimeException(401, "MissingToken", "Missing Authorization header.");
        var authorization = headers.Get("Authorization", null);
        if (authorization.Type != MaldaLang.Interpreter.ValueType.String)
            throw new WebRuntimeException(401, "MissingToken", "Missing bearer token.");

        var headerValue = authorization.AsString().Trim();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new WebRuntimeException(401, "InvalidToken", "Invalid Authorization header format.");

        var token = headerValue["Bearer ".Length..].Trim();
        var verifiedClaims = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.String(token),
                RuntimeValue.String(JwtSecret)
            },
            null!);

        if (verifiedClaims.Type == MaldaLang.Interpreter.ValueType.Object &&
            verifiedClaims.AsObject() is JsonObject claims)
        {
            var sub = claims.Get("sub", null);
            if (sub.Type == MaldaLang.Interpreter.ValueType.String &&
                request.Get("auth", null).AsObject() is RequestAuthContextInstance authContext)
            {
                authContext.CallMethod(
                    "setVerifiedSub",
                    new List<RuntimeValue> { RuntimeValue.String(sub.AsString()) });
            }
        }

        if (nextObj is FunctionValue nextFn && nextFn.BuiltInInstance is MiddlewareNextCallbackInstance callback)
        {
            callback.CallMethod("invoke", new List<RuntimeValue>());
        }

        return Task.FromResult<object>(null!);
    }

    public static Task<object> ProtectedRouteHandler()
    {
        var payload = new JsonObject();
        payload.Set("ok", RuntimeValue.Boolean(true));
        return Task.FromResult<object>(payload);
    }

    public static Task<object> ProtectedMutationHandler(object body)
    {
        var payload = new JsonObject();
        payload.Set("ok", RuntimeValue.Boolean(true));
        payload.Set("type", RuntimeValue.String("mutation"));
        payload.Set("hasBody", RuntimeValue.Boolean(body != null));
        return Task.FromResult<object>(payload);
    }

    public static Task<object> ReturnStandardErrorPayload()
    {
        var payload = new JsonObject();
        payload.Set("status", RuntimeValue.Integer(422));
        payload.Set("error", RuntimeValue.String("BusinessRuleViolation"));
        payload.Set("message", RuntimeValue.String("The request violates a business rule."));
        payload.Set("correlationId", RuntimeValue.String("handler-correlation"));
        return Task.FromResult<object>(payload);
    }

    public static Task<object> ThrowBadRequestWebError()
    {
        throw new WebRuntimeException(400, "ValidationError", "Page validation failed.");
    }
}
