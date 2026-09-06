// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// L6 capability tokens: mint unforgeable handles (files, HTTP GET, MCP call, shell
/// prefix) and consume them. No flat <c>cap()</c> alias and no new keyword.
/// <c>@effects("io")</c> remains a name allow-list; pass a token into a tool so the
/// model cannot invent a path, URL, tool name, or argv.
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

    public static RuntimeValue HttpGet(List<RuntimeValue> args)
    {
        // Do not Require("httpGet"): that name is the HTTP client builtin in the TSV.
        if (args.Count != 1)
            throw new RuntimeException("httpGet() expects 1 argument: (origin)");
        var origin = NormalizeHttpPrefix(RequirePathString(args[0], "httpGet"), "httpGet");
        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindHttpGet, origin));
    }

    public static RuntimeValue McpCall(List<RuntimeValue> args)
    {
        BuiltInArity.Require("mcpCall", args, 1, 2, "server, tool?");
        var server = RequirePathString(args[0], "mcpCall");
        var tool = "";
        if (args.Count > 1 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type != ValueType.String)
                throw new RuntimeException("mcpCall() tool must be a string");
            tool = args[1].AsString();
        }

        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindMcpCall, server, tool));
    }

    public static RuntimeValue Shell(List<RuntimeValue> args)
    {
        BuiltInArity.Require("shell", args, 1, 1, "prefix");
        var prefix = ParseArgv(args[0], "shell");
        if (prefix.Count == 0)
            throw new RuntimeException("shell() prefix cannot be empty");
        foreach (var part in prefix)
            RejectUnsafeShellArg(part, "shell");
        return RuntimeValue.Object(CapabilityToken.Mint(CapabilityToken.KindShell, JoinArgv(prefix)));
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
        BuiltInArity.Require("confine", args, 2, 2, "token, relative");
        var parent = RequireToken(args[0], requiredKind: null, "confine");
        return parent.Kind switch
        {
            CapabilityToken.KindHttpGet => ConfineHttp(parent, RequirePathString(args[1], "confine")),
            CapabilityToken.KindMcpCall => ConfineMcp(parent, RequirePathString(args[1], "confine")),
            CapabilityToken.KindShell => ConfineShell(parent, args[1]),
            _ => ConfineFile(parent, RequirePathString(args[1], "confine"))
        };
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

    public static RuntimeValue Fetch(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("fetch", args, 1, 4, "token, urlOrPath?, maxBytes?, timeoutMs?");
        var token = RequireToken(args[0], CapabilityToken.KindHttpGet, "fetch");
        string? pathOrUrl = null;
        var rest = 1;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            pathOrUrl = args[1].AsString();
            rest = 2;
        }
        else if (args.Count > 1 && args[1].Type == ValueType.Null)
        {
            rest = 2;
        }

        var url = ResolveHttpUrl(token, pathOrUrl);
        var forwarded = new List<RuntimeValue> { RuntimeValue.String(url) };
        for (var i = rest; i < args.Count; i++)
            forwarded.Add(args[i]);
        return BuiltInFunctions.CallBuiltIn("webFetch", forwarded, interpreter);
    }

    public static RuntimeValue Invoke(List<RuntimeValue> args)
    {
        BuiltInArity.Require("invoke", args, 2, 3, "token, server, args?");
        var token = RequireToken(args[0], CapabilityToken.KindMcpCall, "invoke");
        if (string.IsNullOrEmpty(token.Name))
            throw new RuntimeException("invoke() token has no tool; mint mcpCall(server, tool) or cap.confine(token, tool)");

        if (args[1].Type != ValueType.Object)
            throw new RuntimeException("invoke() expects an MCPServer or MCPClient");

        var host = args[1].AsObject();
        var toolArgs = args.Count > 2 ? args[2] : ToolSchemaResolver.EmptyArgsObject();
        var callArgs = new List<RuntimeValue> { RuntimeValue.String(token.Name), toolArgs };

        if (host is MCPServerInstance server)
            return server.CallMethod("callTool", callArgs);

        if (host is MCPClientInstance client)
        {
            RequireMcpServerMatch(token, client, "invoke");
            return client.CallMethod("callTool", callArgs);
        }

        throw new RuntimeException("invoke() expects an MCPServer or MCPClient");
    }

    public static RuntimeValue Run(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("run", args, 1, 4, "token, extraArgs?, workingDir?, timeout?");
        var token = RequireToken(args[0], CapabilityToken.KindShell, "run");
        var extras = new List<string>();
        var rest = 1;
        if (args.Count > 1 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type == ValueType.Array || args[1].Type == ValueType.String)
            {
                extras = ParseArgv(args[1], "run");
                rest = 2;
            }
        }
        else if (args.Count > 1 && args[1].Type == ValueType.Null)
        {
            rest = 2;
        }

        foreach (var extra in extras)
            RejectUnsafeShellArg(extra, "run");

        var argv = ParsePrefix(token.Path);
        argv.AddRange(extras);
        if (argv.Count == 0)
            throw new RuntimeException("run() capability prefix cannot be empty");

        var forwarded = new List<RuntimeValue>
        {
            RuntimeValue.String(argv[0]),
            RuntimeValue.Array(argv.Skip(1).Select(RuntimeValue.String).ToList())
        };
        for (var i = rest; i < args.Count; i++)
            forwarded.Add(args[i]);
        return BuiltInFunctions.CallBuiltIn("runCommand", forwarded, interpreter);
    }

    /// <summary>
    /// Map consume helpers onto the WF1002 deny-list names. Mint / is / confine
    /// and read / list / invoke are not side-effecting for that list.
    /// </summary>
    public static string ResolveWorkflowBuiltInName(string methodName) =>
        methodName switch
        {
            "write" => "writeFile",
            "fetch" => "httpGet",
            "run" => "runCommand",
            _ => methodName
        };

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

    /// <summary>
    /// URL for <c>webFetch</c>: a string, or an <c>httpGet</c> token (optional relative
    /// path or absolute URL that must stay under the token prefix).
    /// </summary>
    public static string ResolveWebFetchUrl(RuntimeValue value, string? pathOrUrl, string callee)
    {
        if (value.Type == ValueType.String)
        {
            if (!string.IsNullOrWhiteSpace(pathOrUrl))
                throw new RuntimeException($"{callee}() path is only valid with an httpGet capability token");
            return value.AsString();
        }

        var token = RequireToken(value, CapabilityToken.KindHttpGet, callee);
        return ResolveHttpUrl(token, pathOrUrl);
    }

    public static RuntimeValue ApplyHttpTokenToWebFetchArgs(CapabilityToken token, RuntimeValue arguments)
    {
        if (token.Kind != CapabilityToken.KindHttpGet)
            throw new RuntimeException("web_fetch capability kind is '" + token.Kind + "', expected 'httpGet'");

        if (arguments.Type != ValueType.Object)
            return arguments;

        var argsObj = arguments.AsObject();
        string? raw = null;
        try
        {
            var urlVal = argsObj.Get("url", null);
            if (urlVal != null && urlVal.Type == ValueType.String)
                raw = urlVal.AsString();
        }
        catch
        {
            raw = null;
        }

        var resolved = ResolveHttpUrl(token, string.IsNullOrWhiteSpace(raw) ? null : raw);
        argsObj.Set("url", RuntimeValue.String(resolved));
        return arguments;
    }

    public static void RequireCommandUnderShell(CapabilityToken token, string command, IReadOnlyList<string>? args)
    {
        if (token.Kind != CapabilityToken.KindShell)
            throw new RuntimeException("run_command capability kind is '" + token.Kind + "', expected 'shell'");

        var prefix = ParsePrefix(token.Path);
        if (prefix.Count == 0)
            throw new RuntimeException("run() capability prefix cannot be empty");

        if (!CommandMatchesPrefix(prefix[0], command))
            throw new RuntimeException($"run() command '{command}' is not under capability prefix '{token.Path}'");

        var supplied = args ?? Array.Empty<string>();
        if (prefix.Count > 1)
        {
            if (supplied.Count < prefix.Count - 1)
                throw new RuntimeException($"run() args do not start with capability prefix '{token.Path}'");

            for (var i = 1; i < prefix.Count; i++)
            {
                if (!string.Equals(supplied[i - 1], prefix[i], StringComparison.Ordinal))
                    throw new RuntimeException($"run() args do not start with capability prefix '{token.Path}'");
            }
        }

        var extraStart = Math.Max(0, prefix.Count - 1);
        for (var i = extraStart; i < supplied.Count; i++)
            RejectUnsafeShellArg(supplied[i], "run");
    }

    public static void RequireMcpTool(CapabilityToken token, string toolName, string? serverName, string callee)
    {
        if (token.Kind != CapabilityToken.KindMcpCall)
            throw new RuntimeException($"{callee}() capability kind is '{token.Kind}', expected '{CapabilityToken.KindMcpCall}'");

        if (string.IsNullOrEmpty(token.Name))
            throw new RuntimeException($"{callee}() token has no tool; mint mcpCall(server, tool) or cap.confine(token, tool)");

        if (!string.Equals(token.Name, toolName, StringComparison.Ordinal))
            throw new RuntimeException($"{callee}() tool '{toolName}' is not under capability tool '{token.Name}'");

        if (!string.IsNullOrEmpty(token.Path) &&
            !string.IsNullOrEmpty(serverName) &&
            !string.Equals(token.Path, serverName, StringComparison.Ordinal))
        {
            throw new RuntimeException($"{callee}() server '{serverName}' is not under capability server '{token.Path}'");
        }
    }

    internal static CapabilityToken RequireToken(RuntimeValue value, string? requiredKind, string callee)
    {
        if (!TryGetToken(value, out var token))
            throw new RuntimeException($"{callee}() expects an unforgeable capability token, not a string or object literal");

        if (requiredKind != null && !string.Equals(token.Kind, requiredKind, StringComparison.Ordinal))
            throw new RuntimeException($"{callee}() capability kind is '{token.Kind}', expected '{requiredKind}'");

        return token;
    }

    internal static string ResolveHttpUrl(CapabilityToken token, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return token.Path;

        string prefix;
        string query = "";
        var raw = pathOrUrl.Trim();
        if (TryParseHttpUri(raw, out var abs))
        {
            prefix = NormalizeHttpPrefix(raw, "fetch");
            query = abs.Query ?? "";
        }
        else
        {
            if (HasDotDotSegment(raw))
                throw new RuntimeException($"fetch() URL '{raw}' is not under capability origin '{token.Path}'");

            SplitQuery(raw, out var pathPart, out query);
            prefix = string.IsNullOrWhiteSpace(pathPart)
                ? token.Path
                : JoinHttpPrefix(token.Path, pathPart);
        }

        if (!IsHttpUnder(token.Path, prefix))
            throw new RuntimeException($"fetch() URL is not under capability origin '{token.Path}'");

        return prefix + query;
    }

    private static RuntimeValue ConfineFile(CapabilityToken parent, string relative)
    {
        var combined = CombineUnderParent(parent.Path, relative);
        if (!IsPathUnderRoot(parent.Path, combined))
            throw new RuntimeException($"confine() path '{relative}' is not under capability path '{parent.Path}'");

        return RuntimeValue.Object(CapabilityToken.Mint(parent.Kind, combined, parent.Name));
    }

    private static RuntimeValue ConfineHttp(CapabilityToken parent, string relative)
    {
        if (HasDotDotSegment(relative))
            throw new RuntimeException($"confine() path '{relative}' is not under capability origin '{parent.Path}'");

        SplitQuery(relative.Trim(), out var pathPart, out _);
        var combined = string.IsNullOrWhiteSpace(pathPart)
            ? parent.Path
            : (TryParseHttpUri(pathPart, out _)
                ? NormalizeHttpPrefix(pathPart, "confine")
                : JoinHttpPrefix(parent.Path, pathPart));

        if (!IsHttpUnder(parent.Path, combined))
            throw new RuntimeException($"confine() path '{relative}' is not under capability origin '{parent.Path}'");

        return RuntimeValue.Object(CapabilityToken.Mint(parent.Kind, combined));
    }

    private static RuntimeValue ConfineMcp(CapabilityToken parent, string tool)
    {
        if (string.IsNullOrWhiteSpace(tool))
            throw new RuntimeException("confine() tool name must be a non-empty string");
        if (tool.Contains('/') || tool.Contains('\\') || HasDotDotSegment(tool))
        {
            throw new RuntimeException($"confine() tool '{tool}' is not under capability server '{parent.Path}'");
        }

        if (!string.IsNullOrEmpty(parent.Name) &&
            !string.Equals(parent.Name, tool, StringComparison.Ordinal))
        {
            throw new RuntimeException($"confine() tool '{tool}' is not under capability tool '{parent.Name}'");
        }

        return RuntimeValue.Object(CapabilityToken.Mint(parent.Kind, parent.Path, tool));
    }

    private static RuntimeValue ConfineShell(CapabilityToken parent, RuntimeValue relative)
    {
        var extra = ParseArgv(relative, "confine");
        foreach (var part in extra)
            RejectUnsafeShellArg(part, "confine");

        var combined = ParsePrefix(parent.Path);
        combined.AddRange(extra);
        if (combined.Count == 0)
            throw new RuntimeException("confine() shell prefix cannot be empty");

        return RuntimeValue.Object(CapabilityToken.Mint(parent.Kind, JoinArgv(combined)));
    }

    private static void RequireMcpServerMatch(CapabilityToken token, MCPClientInstance client, string callee)
    {
        var serverName = "";
        try
        {
            var nameVal = client.Get("serverName", null);
            if (nameVal.Type == ValueType.String)
                serverName = nameVal.AsString();
        }
        catch
        {
            serverName = "";
        }

        if (!string.IsNullOrEmpty(token.Path) &&
            !string.IsNullOrEmpty(serverName) &&
            !string.Equals(token.Path, serverName, StringComparison.Ordinal))
        {
            throw new RuntimeException($"{callee}() server '{serverName}' is not under capability server '{token.Path}'");
        }
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

    internal static string NormalizeHttpPrefix(string raw, string callee)
    {
        if (!TryParseHttpUri(raw, out var uri))
            throw new RuntimeException($"{callee}() expects an absolute http or https URL");

        var path = uri.AbsolutePath;
        if (path == "/")
            path = "";
        else
            path = path.TrimEnd('/');

        var origin = $"{uri.Scheme}://{uri.Host}";
        if (!uri.IsDefaultPort)
            origin += $":{uri.Port}";
        return origin + path;
    }

    private static string JoinHttpPrefix(string parent, string relative)
    {
        var rel = relative.Replace('\\', '/').Trim();
        if (rel.StartsWith("//", StringComparison.Ordinal))
            throw new RuntimeException($"confine() path '{relative}' is not under capability origin '{parent}'");

        if (rel.StartsWith('/'))
        {
            var path = rel.Split('?', 2)[0].Split('#', 2)[0].TrimEnd('/');
            return GetHttpOrigin(parent) + path;
        }

        var relPath = rel.Split('?', 2)[0].Split('#', 2)[0].Trim('/');
        return string.IsNullOrEmpty(relPath) ? parent : parent.TrimEnd('/') + "/" + relPath;
    }

    private static bool IsHttpUnder(string parent, string child)
    {
        if (!TryParseHttpUri(parent, out var parentUri) || !TryParseHttpUri(child, out var childUri))
            return false;

        if (!string.Equals(parentUri.Scheme, childUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(parentUri.Host, childUri.Host, StringComparison.OrdinalIgnoreCase))
            return false;
        if (parentUri.Port != childUri.Port)
            return false;

        var parentPath = NormalizeHttpPath(parentUri.AbsolutePath);
        var childPath = NormalizeHttpPath(childUri.AbsolutePath);
        if (parentPath.Length == 0)
            return true;
        if (string.Equals(parentPath, childPath, StringComparison.Ordinal))
            return true;
        return childPath.StartsWith(parentPath + "/", StringComparison.Ordinal);
    }

    private static string GetHttpOrigin(string prefix)
    {
        if (!TryParseHttpUri(prefix, out var uri))
            return prefix;
        var origin = $"{uri.Scheme}://{uri.Host}";
        if (!uri.IsDefaultPort)
            origin += $":{uri.Port}";
        return origin;
    }

    private static string NormalizeHttpPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "";
        return path.TrimEnd('/');
    }

    private static bool TryParseHttpUri(string raw, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        uri = parsed;
        return true;
    }

    private static void SplitQuery(string raw, out string path, out string query)
    {
        var cut = raw.IndexOf('#');
        var withoutFrag = cut >= 0 ? raw.Substring(0, cut) : raw;
        var q = withoutFrag.IndexOf('?');
        if (q < 0)
        {
            path = withoutFrag;
            query = "";
            return;
        }

        path = withoutFrag.Substring(0, q);
        query = withoutFrag.Substring(q);
    }

    private static bool HasDotDotSegment(string value)
    {
        var path = value.Replace('\\', '/');
        var q = path.IndexOfAny(['?', '#']);
        if (q >= 0)
            path = path.Substring(0, q);

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
                return true;
        }

        return false;
    }

    private static List<string> ParseArgv(RuntimeValue value, string callee)
    {
        if (value.Type == ValueType.String)
            return ParsePrefix(value.AsString());

        if (value.Type == ValueType.Array)
        {
            var parts = new List<string>();
            foreach (var item in value.AsArray())
            {
                if (item.Type != ValueType.String)
                    throw new RuntimeException($"{callee}() prefix entries must be strings");
                var text = item.AsString();
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }

            return parts;
        }

        throw new RuntimeException($"{callee}() prefix must be a string or an array of strings");
    }

    private static List<string> ParsePrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new List<string>();
        return path.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string JoinArgv(IReadOnlyList<string> parts) => string.Join(" ", parts);

    private static void RejectUnsafeShellArg(string arg, string callee)
    {
        if (string.IsNullOrWhiteSpace(arg))
            throw new RuntimeException($"{callee}() prefix cannot contain an empty argument");
        if (HasDotDotSegment(arg))
            throw new RuntimeException($"{callee}() argument '{arg}' is not allowed under a shell capability");
        if (System.IO.Path.IsPathRooted(arg) ||
            arg.StartsWith('/') ||
            arg.StartsWith('\\'))
        {
            throw new RuntimeException($"{callee}() argument '{arg}' is not allowed under a shell capability");
        }
    }

    private static bool CommandMatchesPrefix(string prefixCmd, string supplied)
    {
        if (string.Equals(prefixCmd, supplied, StringComparison.OrdinalIgnoreCase))
            return true;
        if (System.IO.Path.IsPathRooted(supplied))
            return false;
        if (prefixCmd.IndexOfAny(['/', '\\']) >= 0)
            return false;
        return string.Equals(
            System.IO.Path.GetFileNameWithoutExtension(prefixCmd),
            System.IO.Path.GetFileNameWithoutExtension(supplied),
            StringComparison.OrdinalIgnoreCase);
    }
}
