using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class AuthBuiltInsTests : TestBase
{
    [Fact]
    public void HashPassword_And_VerifyPassword_RoundTrip()
    {
        var hash = BuiltInFunctions.CallBuiltIn(
            "hashPassword",
            new List<RuntimeValue> { RuntimeValue.String("s3cret!") },
            null!);

        Assert.Equal(ValueType.String, hash.Type);

        var valid = BuiltInFunctions.CallBuiltIn(
            "verifyPassword",
            new List<RuntimeValue> { RuntimeValue.String("s3cret!"), hash },
            null!);
        var invalid = BuiltInFunctions.CallBuiltIn(
            "verifyPassword",
            new List<RuntimeValue> { RuntimeValue.String("wrong"), hash },
            null!);

        Assert.True(valid.AsBoolean());
        Assert.False(invalid.AsBoolean());
    }

    [Fact]
    public void CreateJwt_And_VerifyJwt_RoundTrip()
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-123"));
        payload.Set("role", RuntimeValue.String("admin"));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Integer(120)
            },
            null!);

        var verifiedPayload = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue> { token, RuntimeValue.String("jwt-secret") },
            null!);

        Assert.Equal(ValueType.Object, verifiedPayload.Type);
        var obj = (JsonObject)verifiedPayload.AsObject();
        Assert.Equal("user-123", obj.Get("sub", null).AsString());
        Assert.Equal("admin", obj.Get("role", null).AsString());
        Assert.NotEqual(ValueType.Null, obj.Get("iat", null).Type);
    }

    [Fact]
    public void VerifyJwt_ExpiredToken_ThrowsWebRuntimeException()
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-expired"));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Integer(-1)
            },
            null!);

        var ex = Assert.Throws<WebRuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn(
                "verifyJwt",
                new List<RuntimeValue> { token, RuntimeValue.String("jwt-secret") },
                null!));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("TokenExpired", ex.ErrorCode);
    }

    [Fact]
    public void CreateJwt_Options_SetsIssuerAudienceAndNotBefore()
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-options"));
        var options = new JsonObject();
        options.Set("expiresInSeconds", RuntimeValue.Integer(120));
        options.Set("issuer", RuntimeValue.String("malda-core"));
        options.Set("audience", RuntimeValue.String("malda-apps"));
        options.Set("notBeforeSeconds", RuntimeValue.Integer(0));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Object(options)
            },
            null!);

        var verifyOptions = new JsonObject();
        verifyOptions.Set("issuer", RuntimeValue.String("malda-core"));
        verifyOptions.Set("audience", RuntimeValue.String("malda-apps"));
        var claims = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue>
            {
                token,
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Object(verifyOptions)
            },
            null!);

        var obj = (JsonObject)claims.AsObject();
        Assert.Equal("user-options", obj.Get("sub", null).AsString());
        Assert.Equal("malda-core", obj.Get("iss", null).AsString());
        Assert.Equal("malda-apps", obj.Get("aud", null).AsString());
        Assert.NotEqual(ValueType.Null, obj.Get("nbf", null).Type);
    }

    [Fact]
    public void VerifyJwt_WrongIssuer_ThrowsInvalidToken()
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-iss"));
        var createOptions = new JsonObject();
        createOptions.Set("expiresInSeconds", RuntimeValue.Integer(120));
        createOptions.Set("issuer", RuntimeValue.String("expected-iss"));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Object(createOptions)
            },
            null!);

        var verifyOptions = new JsonObject();
        verifyOptions.Set("issuer", RuntimeValue.String("other-iss"));
        var ex = Assert.Throws<WebRuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn(
                "verifyJwt",
                new List<RuntimeValue>
                {
                    token,
                    RuntimeValue.String("jwt-secret"),
                    RuntimeValue.Object(verifyOptions)
                },
                null!));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("InvalidToken", ex.ErrorCode);
    }

    [Fact]
    public void VerifyJwt_NotBeforeInFuture_ThrowsInvalidToken()
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-nbf"));
        var createOptions = new JsonObject();
        createOptions.Set("expiresInSeconds", RuntimeValue.Integer(120));
        createOptions.Set("notBeforeSeconds", RuntimeValue.Integer(3600));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String("jwt-secret"),
                RuntimeValue.Object(createOptions)
            },
            null!);

        var ex = Assert.Throws<WebRuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn(
                "verifyJwt",
                new List<RuntimeValue> { token, RuntimeValue.String("jwt-secret") },
                null!));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("InvalidToken", ex.ErrorCode);
        Assert.Contains("not yet valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsrfBuiltIns_GenerateAndVerify_RoundTrip()
    {
        var token = BuiltInFunctions.CallBuiltIn(
            "generateCsrfToken",
            new List<RuntimeValue>
            {
                RuntimeValue.String("csrf-secret"),
                RuntimeValue.Integer(120)
            },
            null!);

        var valid = BuiltInFunctions.CallBuiltIn(
            "verifyCsrfToken",
            new List<RuntimeValue>
            {
                token,
                RuntimeValue.String("csrf-secret")
            },
            null!);
        var invalid = BuiltInFunctions.CallBuiltIn(
            "verifyCsrfToken",
            new List<RuntimeValue>
            {
                token,
                RuntimeValue.String("wrong-secret")
            },
            null!);

        Assert.True(valid.AsBoolean());
        Assert.False(invalid.AsBoolean());
    }

    [Fact]
    public void SecureCookieBuiltIns_CreateAndRead_RoundTrip()
    {
        var options = new JsonObject();
        options.Set("maxAge", RuntimeValue.Integer(60));
        options.Set("sameSite", RuntimeValue.String("Strict"));

        var cookieHeader = BuiltInFunctions.CallBuiltIn(
            "createSecureCookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String("session"),
                RuntimeValue.String("user-123"),
                RuntimeValue.String("cookie-secret"),
                RuntimeValue.Object(options)
            },
            null!);

        var headerValue = cookieHeader.AsString();
        Assert.Contains("HttpOnly", headerValue);
        Assert.Contains("Secure", headerValue);
        Assert.Contains("SameSite=Strict", headerValue);
        Assert.Contains("Max-Age=60", headerValue);

        var cookieValueSegment = headerValue.Split(';')[0];
        var secureValue = Uri.UnescapeDataString(cookieValueSegment.Split('=', 2)[1]);
        var parsed = BuiltInFunctions.CallBuiltIn(
            "readSecureCookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String(secureValue),
                RuntimeValue.String("cookie-secret")
            },
            null!);

        Assert.Equal(ValueType.String, parsed.Type);
        Assert.Equal("user-123", parsed.AsString());
    }

    [Fact]
    public void AuthBuiltIns_InterpretedAndTranspiled_Parity()
    {
        var interpretedPayload = new JsonObject();
        interpretedPayload.Set("sub", RuntimeValue.String("parity-user"));
        var interpretedToken = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(interpretedPayload),
                RuntimeValue.String("parity-secret"),
                RuntimeValue.Integer(120)
            },
            null!);
        var interpretedClaims = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue> { interpretedToken, RuntimeValue.String("parity-secret") },
            null!);
        var interpretedHash = BuiltInFunctions.CallBuiltIn(
            "hashPassword",
            new List<RuntimeValue> { RuntimeValue.String("pw") },
            null!);
        var interpretedValid = BuiltInFunctions.CallBuiltIn(
            "verifyPassword",
            new List<RuntimeValue> { RuntimeValue.String("pw"), interpretedHash },
            null!);
        var interpretedInvalid = BuiltInFunctions.CallBuiltIn(
            "verifyPassword",
            new List<RuntimeValue> { RuntimeValue.String("wrong"), interpretedHash },
            null!);

        Assert.Equal(ValueType.Object, interpretedClaims.Type);
        Assert.True(interpretedValid.AsBoolean());
        Assert.False(interpretedInvalid.AsBoolean());
        var interpretedCsrfToken = BuiltInFunctions.CallBuiltIn(
            "generateCsrfToken",
            new List<RuntimeValue>
            {
                RuntimeValue.String("parity-secret"),
                RuntimeValue.Integer(120)
            },
            null!);
        var interpretedCsrfValid = BuiltInFunctions.CallBuiltIn(
            "verifyCsrfToken",
            new List<RuntimeValue>
            {
                interpretedCsrfToken,
                RuntimeValue.String("parity-secret")
            },
            null!);
        var interpretedSecureCookie = BuiltInFunctions.CallBuiltIn(
            "createSecureCookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String("session"),
                RuntimeValue.String("cookie-parity"),
                RuntimeValue.String("parity-secret")
            },
            null!);
        var secureCookieValue = Uri.UnescapeDataString(interpretedSecureCookie.AsString().Split(';')[0].Split('=', 2)[1]);
        var interpretedSecureCookieRead = BuiltInFunctions.CallBuiltIn(
            "readSecureCookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String(secureCookieValue),
                RuntimeValue.String("parity-secret")
            },
            null!);
        Assert.True(interpretedCsrfValid.AsBoolean());
        Assert.Equal("cookie-parity", interpretedSecureCookieRead.AsString());

        const string source = """
var payload = {"sub": "parity-user"};
var token = createJwt(payload, "parity-secret", 120);
var claims = verifyJwt(token, "parity-secret");
var hash = hashPassword("pw");
var csrfToken = generateCsrfToken("parity-secret", 120);
var csrfOk = verifyCsrfToken(csrfToken, "parity-secret");
var secureCookie = createSecureCookie("session", "cookie-parity", "parity-secret");
var secureCookieValue = split(split(secureCookie, ";")[0], "=")[1];
var secureCookieRead = readSecureCookie(urlDecode(secureCookieValue), "parity-secret");
print(typeOf(claims));
print(verifyPassword("pw", hash));
print(verifyPassword("wrong", hash));
print(csrfOk);
print(secureCookieRead);
""";

        var transpiledResult = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, transpiledResult.ExitCode);
        Assert.Equal("object\ntrue\nfalse\ntrue\ncookie-parity", transpiledResult.StdOut);
    }
}
