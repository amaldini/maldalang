// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInFormErrors(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("formErrors() expects 1 argument (array of strings or {field,message} objects)");

        var items = new List<string>();
        if (args[0].Type == ValueType.Array)
        {
            foreach (var item in args[0].AsArray())
            {
                if (item.Type == ValueType.String)
                {
                    items.Add(item.AsString());
                }
                else if (item.Type == ValueType.Object && item.AsObject() is JsonObject obj)
                {
                    var field = obj.Get("field", null);
                    var message = obj.Get("message", null);
                    if (message.Type == ValueType.String)
                    {
                        var fieldName = field.Type == ValueType.String ? field.AsString() : string.Empty;
                        items.Add(string.IsNullOrEmpty(fieldName)
                            ? message.AsString()
                            : $"{fieldName}: {message.AsString()}");
                    }
                    else
                    {
                        items.Add(item.ToString());
                    }
                }
                else
                {
                    items.Add(item.ToString());
                }
            }
        }
        else if (args[0].Type == ValueType.String)
        {
            items.Add(args[0].AsString());
        }
        else
        {
            throw new Exception("formErrors() expects an array or string");
        }

        if (items.Count == 0)
        {
            return RuntimeValue.String(string.Empty);
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<ul class=\"error-list\">");
        foreach (var item in items)
        {
            sb.Append("<li>");
            sb.Append(System.Net.WebUtility.HtmlEncode(item));
            sb.Append("</li>");
        }

        sb.Append("</ul>");
        return RuntimeValue.String(sb.ToString());
    }

    private static RuntimeValue BuiltInBindForm(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("bindForm() expects body and fields array");
        if (args[1].Type != ValueType.Array)
            throw new Exception("bindForm() fields must be an array");

        JsonObject? bodyObj = null;
        if (args[0].Type == ValueType.Object && args[0].AsObject() is JsonObject jsonBody)
        {
            bodyObj = jsonBody;
        }
        else if (args[0].Type == ValueType.Object && args[0].AsObject() is DictionaryInstance dictBody)
        {
            bodyObj = new JsonObject();
            foreach (var key in dictBody.GetAllKeys())
            {
                if (dictBody.TryGetEntry(key, out var value))
                {
                    bodyObj.Set(key, value);
                }
            }
        }
        else if (args[0].Type != ValueType.Null)
        {
            throw new Exception("bindForm() body must be an object or null");
        }

        var values = new JsonObject();
        var errors = new List<RuntimeValue>();
        var ok = true;

        foreach (var fieldSpec in args[1].AsArray())
        {
            if (fieldSpec.Type != ValueType.Object || fieldSpec.AsObject() is not JsonObject fieldObj)
            {
                throw new Exception("bindForm() each field must be an object with a name");
            }

            var nameValue = fieldObj.Get("name", null);
            if (nameValue.Type != ValueType.String || string.IsNullOrWhiteSpace(nameValue.AsString()))
            {
                throw new Exception("bindForm() field.name must be a non-empty string");
            }

            var name = nameValue.AsString();
            var required = fieldObj.Get("required", null).Type == ValueType.Boolean &&
                           fieldObj.Get("required", null).AsBoolean();
            var trim = fieldObj.Get("trim", null).Type != ValueType.Boolean ||
                       fieldObj.Get("trim", null).AsBoolean();

            RuntimeValue raw = RuntimeValue.Null();
            if (bodyObj != null)
            {
                raw = bodyObj.Get(name, null);
            }

            string text;
            if (raw.Type == ValueType.Null)
            {
                text = string.Empty;
            }
            else if (raw.Type == ValueType.String)
            {
                text = raw.AsString();
            }
            else
            {
                text = raw.ToString();
            }

            if (trim)
            {
                text = text.Trim();
            }

            values.Set(name, RuntimeValue.String(text));

            if (required && string.IsNullOrEmpty(text))
            {
                ok = false;
                errors.Add(CreateFormFieldError(name, $"{name} is required"));
                continue;
            }

            var minLength = fieldObj.Get("minLength", null);
            if (minLength.Type == ValueType.Integer && text.Length < minLength.AsInteger())
            {
                ok = false;
                errors.Add(CreateFormFieldError(name, $"{name} must be at least {minLength.AsInteger()} characters"));
            }

            var maxLength = fieldObj.Get("maxLength", null);
            if (maxLength.Type == ValueType.Integer && text.Length > maxLength.AsInteger())
            {
                ok = false;
                errors.Add(CreateFormFieldError(name, $"{name} must be at most {maxLength.AsInteger()} characters"));
            }

            var pattern = fieldObj.Get("pattern", null);
            if (pattern.Type == ValueType.String && !string.IsNullOrEmpty(text))
            {
                var patternName = pattern.AsString().Trim().ToLowerInvariant();
                if (patternName == "email")
                {
                    if (!text.Contains('@') || text.IndexOf('@') == 0 || text.IndexOf('@') == text.Length - 1)
                    {
                        ok = false;
                        errors.Add(CreateFormFieldError(name, $"{name} must be a valid email"));
                    }
                }
                else
                {
                    throw new Exception("bindForm() pattern must be \"email\" in this release");
                }
            }
        }

        var result = new JsonObject();
        result.Set("ok", RuntimeValue.Boolean(ok));
        result.Set("values", RuntimeValue.Object(values));
        result.Set("errors", RuntimeValue.Array(errors));
        return RuntimeValue.Object(result);
    }

    private static RuntimeValue CreateFormFieldError(string field, string message)
    {
        var obj = new JsonObject();
        obj.Set("field", RuntimeValue.String(field));
        obj.Set("message", RuntimeValue.String(message));
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue BuiltInPageLayout(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("pageLayout() expects title, bodyHtml, and optional options object");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("pageLayout() title and bodyHtml must be strings");

        var title = System.Net.WebUtility.HtmlEncode(args[0].AsString());
        var bodyHtml = args[1].AsString();
        var css = string.Empty;
        var script = string.Empty;

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Object)
            {
                throw new Exception("pageLayout() options must be an object");
            }

            var opts = args[2].AsObject();
            var cssValue = opts.Get("css", null);
            if (cssValue.Type == ValueType.String)
            {
                css = cssValue.AsString();
            }

            var scriptValue = opts.Get("script", null);
            if (scriptValue.Type == ValueType.String)
            {
                script = scriptValue.AsString();
            }
        }

        var styleTag = string.IsNullOrEmpty(css) ? string.Empty : $"<style>{css}</style>";
        var scriptTag = string.IsNullOrEmpty(script) ? string.Empty : $"<script>{script}</script>";
        return RuntimeValue.String(
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + title + "</title>" +
            styleTag + "</head><body>" + bodyHtml + scriptTag + "</body></html>");
    }
}
