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

        if (request.Get("auth", null).AsObject() is not RequestAuthContextInstance authContext)
        {
            throw new WebRuntimeException(401, "InvalidToken", "Missing request auth context.");
        }

        authContext.CallMethod(
            "authenticateBearerJwt",
            new List<RuntimeValue> { RuntimeValue.String(JwtSecret) });

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

    public static Task<object> PublicHealthHandler()
    {
        var payload = new JsonObject();
        payload.Set("status", RuntimeValue.String("ok"));
        return Task.FromResult<object>(payload);
    }

    /// <summary>
    /// Mirrors C# transpile of a MALDA object literal (G9 <c>/api/health</c>).
    /// </summary>
    public static Task<object> ReturnDictionaryHealthPayload()
    {
        return Task.FromResult<object>(new Dictionary<string, object?>
        {
            ["app"] = "tapscore",
            ["status"] = "ok"
        });
    }

    /// <summary>
    /// Mirrors C# transpile of G9 <c>/api/scores</c> (object + nested list of objects).
    /// </summary>
    public static Task<object> ReturnDictionaryScoresPayload()
    {
        return Task.FromResult<object>(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["scores"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "Ada",
                    ["points"] = 12
                }
            }
        });
    }
}
