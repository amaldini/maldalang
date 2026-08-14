// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// L6 capability tokens: mint unforgeable FileRead / FileWrite / DirList handles and
/// consume them. No flat <c>cap()</c> alias and no new keyword. <c>@effects("io")</c>
/// remains a name allow-list; pass a token into a tool so the model cannot invent a path.
/// </summary>
public static class CapStdLib
{
    public static RuntimeValue FileRead(List<RuntimeValue> args)
    {
        BuiltInArity.Require("fileRead", args, 1, 1, "path");
        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindFileRead, RequirePathString(args[0], "fileRead")));
    }

    public static RuntimeValue FileWrite(List<RuntimeValue> args)
    {
        BuiltInArity.Require("fileWrite", args, 1, 1, "path");
        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindFileWrite, RequirePathString(args[0], "fileWrite")));
    }

    public static RuntimeValue DirList(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dirList", args, 1, 1, "path");
        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindDirList, RequirePathString(args[0], "dirList")));
    }

    public static RuntimeValue Is(List<RuntimeValue> args)
    {
        BuiltInArity.Require("is", args, 1, 2, "value, kind?");
        if (!TryGetToken(args[0], out var token))
            return RuntimeValue.Boolean(false);

        if (args.Count < 2 || args[1].Type == ValueType.Null)
            return RuntimeValue.Boolean(true);

        if (args[1].Type != ValueType.String)
            throw new RuntimeException("is() expects 1-2 arguments: (value, kind?)");

        return RuntimeValue.Boolean(string.Equals(token.Kind, args[1].AsString(), StringComparison.Ordinal));
    }

    public static RuntimeValue Confine(List<RuntimeValue> args)
    {
        BuiltInArity.Require("confine", args, 2, 2, "token, relativePath");
        var parent = RequireToken(args[0], requiredKind: null, "confine");
        var relative = RequirePathString(args[1], "confine");
        var combined = CombineUnderParent(parent.Path, relative);
        if (!IsPathUnderRoot(parent.Path, combined))
            throw new RuntimeException($"confine() path '{relative}' is not under capability path '{parent.Path}'");

        return RuntimeValue.Object(CapabilityToken.Mint(parent.Kind, combined));
    }

    public static RuntimeValue Read(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("read", args, 1, 3, "token, startLine?, endLine?");
        var token = RequireToken(args[0], CapabilityToken.KindFileRead, "read");
        var forwarded = new List<RuntimeValue> { RuntimeValue.String(token.Path) };
        for (var i = 1; i < args.Count; i++)
            forwarded.Add(args[i]);
        return BuiltInFunctions.CallBuiltIn("readFile", forwarded, interpreter);
    }

    public static RuntimeValue Write(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("write", args, 2, 2, "token, content");
        var token = RequireToken(args[0], CapabilityToken.KindFileWrite, "write");
        return BuiltInFunctions.CallBuiltIn(
            "writeFile",
            new List<RuntimeValue> { RuntimeValue.String(token.Path), args[1] },
            interpreter);
    }

    public static RuntimeValue List(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("list", args, 1, 1, "token");
        var token = RequireToken(args[0], CapabilityToken.KindDirList, "list");
        return BuiltInFunctions.CallBuiltIn(
            "listDirectory",
            new List<RuntimeValue> { RuntimeValue.String(token.Path) },
            interpreter);
    }

    /// <summary>
    /// Map <c>cap.write</c> onto the WF1002 <c>writeFile</c> deny-list name. Mint / is /
    /// confine / read / list are not side-effecting for that list.
    /// </summary>
    public static string ResolveWorkflowBuiltInName(string methodName) =>
        methodName == "write" ? "writeFile" : methodName;

    public static bool TryGetToken(RuntimeValue value, out CapabilityToken token)
    {
        token = null!;
        if (value.Type != ValueType.Object)
            return false;
        if (value.AsObject() is not CapabilityToken cap)
            return false;
        token = cap;
        return true;
    }

    /// <summary>
    /// Path for <c>io.readFile</c> / <c>writeFile</c> / <c>listDirectory</c>: a string, or a
    /// matching capability token. Object literals that look like tokens are rejected.
    /// </summary>
    public static string ResolveIoPath(RuntimeValue value, string callee, string? requiredKind)
    {
        if (value.Type == ValueType.String)
            return value.AsString();

        if (TryGetToken(value, out var token))
        {
            if (requiredKind != null && !string.Equals(token.Kind, requiredKind, StringComparison.Ordinal))
                throw new RuntimeException($"{callee}() capability kind is '{token.Kind}', expected '{requiredKind}'");
            return token.Path;
        }

        throw new RuntimeException($"{callee}() expects a string path or a capability token, not a forged object");
    }

    internal static CapabilityToken RequireToken(RuntimeValue value, string? requiredKind, string callee)
    {
        if (!TryGetToken(value, out var token))
            throw new RuntimeException($"{callee}() expects an unforgeable capability token, not a string or object literal");

        if (requiredKind != null && !string.Equals(token.Kind, requiredKind, StringComparison.Ordinal))
            throw new RuntimeException($"{callee}() capability kind is '{token.Kind}', expected '{requiredKind}'");

        return token;
    }

    private static string RequirePathString(RuntimeValue value, string callee)
    {
        if (value.Type != ValueType.String)
            throw new RuntimeException($"{callee}() path must be a string");
        return value.AsString();
    }

    private static string CombineUnderParent(string parent, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return parent;

        if (System.IO.Path.IsPathRooted(relative) || EmbeddedFolderStore.IsEmbedPath(relative))
            return relative;

        if (EmbeddedFolderStore.IsEmbedPath(parent))
            return EmbeddedFolderStore.Join(parent, relative);

        return System.IO.Path.Combine(parent, relative);
    }

    private static bool IsPathUnderRoot(string root, string path)
    {
        return BuiltInFunctions.CallBuiltIn(
            "isPathUnder",
            new List<RuntimeValue> { RuntimeValue.String(root), RuntimeValue.String(path) },
            interpreter: null).AsBoolean();
    }
}
