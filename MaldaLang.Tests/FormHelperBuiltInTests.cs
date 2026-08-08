// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class FormHelperBuiltInTests
{
    [Fact]
    public void CsrfField_ProducesHiddenInputWithValidToken()
    {
        var field = BuiltInFunctions.CallBuiltIn(
            "csrfField",
            new List<RuntimeValue> { RuntimeValue.String("csrf-secret") },
            null!);
        Assert.Equal(ValueType.String, field.Type);
        var html = field.AsString();
        Assert.Contains("name=\"_csrf\"", html);
        Assert.Contains("type=\"hidden\"", html);

        var start = html.IndexOf("value=\"", StringComparison.Ordinal) + "value=\"".Length;
        var end = html.IndexOf('"', start);
        var token = System.Net.WebUtility.HtmlDecode(html[start..end]);
        Assert.True(WebRuntimeHelpers.VerifyCsrfToken(token, "csrf-secret"));
    }

    [Fact]
    public void BindForm_ValidatesRequiredAndEmail()
    {
        var body = new JsonObject();
        body.Set("title", RuntimeValue.String("  hello  "));
        body.Set("email", RuntimeValue.String("not-an-email"));

        var fields = RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(Field("title", required: true, trim: true)),
            RuntimeValue.Object(Field("email", required: true, pattern: "email"))
        });

        var result = BuiltInFunctions.CallBuiltIn(
            "bindForm",
            new List<RuntimeValue> { RuntimeValue.Object(body), fields },
            null!).AsObject() as JsonObject;

        Assert.NotNull(result);
        Assert.False(result!.Get("ok", null).AsBoolean());
        Assert.Equal("hello", result.Get("values", null).AsObject().Get("title", null).AsString());
        Assert.True(result.Get("errors", null).AsArray().Count >= 1);
    }

    [Fact]
    public void FormErrors_And_PageLayout_RenderHtml()
    {
        var errors = BuiltInFunctions.CallBuiltIn(
            "formErrors",
            new List<RuntimeValue>
            {
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("Bad <input>") })
            },
            null!).AsString();
        Assert.Contains("error-list", errors);
        Assert.Contains("&lt;input&gt;", errors);

        var page = BuiltInFunctions.CallBuiltIn(
            "pageLayout",
            new List<RuntimeValue>
            {
                RuntimeValue.String("Title"),
                RuntimeValue.String("<p>Hi</p>")
            },
            null!).AsString();
        Assert.Contains("<title>Title</title>", page);
        Assert.Contains("<p>Hi</p>", page);
    }

    private static JsonObject Field(string name, bool required = false, bool trim = true, string? pattern = null)
    {
        var obj = new JsonObject();
        obj.Set("name", RuntimeValue.String(name));
        obj.Set("required", RuntimeValue.Boolean(required));
        obj.Set("trim", RuntimeValue.Boolean(trim));
        if (pattern != null)
        {
            obj.Set("pattern", RuntimeValue.String(pattern));
        }

        return obj;
    }
}
