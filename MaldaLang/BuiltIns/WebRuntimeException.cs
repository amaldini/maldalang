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

/// <summary>
/// HTTP/auth failures thrown from web helpers (e.g. missing JWT cookie).
/// Extends <see cref="RuntimeException"/> so Malda <c>try/catch</c> can redirect
/// (ASK <c>askRequireAuth</c>) instead of always surfacing a raw 401.
/// </summary>
public class WebRuntimeException : RuntimeException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public List<RouteValidationError>? Details { get; }

    public WebRuntimeException(
        int statusCode,
        string errorCode,
        string message,
        List<RouteValidationError>? details = null,
        Exception? innerException = null) : base(message, null, null, null, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }
}
