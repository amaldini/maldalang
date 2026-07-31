namespace MaldaLang.Tests;

using System.Net.Http;
using System.Text.Json;

public class SecurityTestUtilsTests
{
    [Fact]
    public void ExtractCookieValue_ReturnsRequestedCookie()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", "csrf_token=abc123; Path=/; HttpOnly");
        response.Headers.Add("Set-Cookie", "session=xyz; Path=/; HttpOnly");

        var token = SecurityTestUtils.ExtractCookieValue(response, "csrf_token");
        Assert.Equal("abc123", token);
    }

    [Fact]
    public void AssertStandardErrorPayload_ValidPayload_DoesNotThrow()
    {
        var json = """
        {
          "status": 429,
          "error": "RateLimitExceeded",
          "message": "Too many requests.",
          "correlationId": "corr-1"
        }
        """;
        using var doc = JsonDocument.Parse(json);

        SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "RateLimitExceeded", "corr-1", 429);
    }
}
