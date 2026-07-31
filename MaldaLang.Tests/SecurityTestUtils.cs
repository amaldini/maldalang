namespace MaldaLang.Tests;

using System;
using System.Net.Http;
using System.Text.Json;

internal static class SecurityTestUtils
{
    public static void AssertStandardErrorPayload(JsonElement root, string expectedErrorCode, string expectedCorrelationId, int expectedStatus)
    {
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedErrorCode, root.GetProperty("error").GetString());
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }

    public static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return string.Empty;
        }

        foreach (var value in values)
        {
            var firstSegment = value.Split(';')[0];
            var parts = firstSegment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), cookieName, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return string.Empty;
    }
}
