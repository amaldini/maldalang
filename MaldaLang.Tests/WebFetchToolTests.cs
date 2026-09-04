// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class WebFetchToolTests
{
    [Fact]
    public void TextBody_200_OkTrueContentMatches()
    {
        using var server = LocalHttpServer.Start(_ => (200, "text/plain", "hello fetch"));
        var result = BuiltInFunctions.ExecuteWebFetch(Args(("url", server.Url("/text"))));
        AssertFetchShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        Assert.Equal(200, obj.Get("status", null)!.AsInteger());
        Assert.Equal("hello fetch", obj.Get("content", null)!.AsString());
        Assert.False(obj.Get("truncated", null)!.AsBoolean());
        Assert.Contains(server.BaseUrl, obj.Get("url", null)!.AsString());
    }

    [Fact]
    public void JsonBody_ContentIsSerializedString()
    {
        using var server = LocalHttpServer.Start(_ => (200, "application/json", "{\"hello\":\"world\",\"n\":1}"));
        var result = BuiltInFunctions.ExecuteWebFetch(Args(("url", server.Url("/json"))));
        AssertFetchShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        var contentVal = obj.Get("content", null)!;
        Assert.Equal(ValueType.String, contentVal.Type);
        var content = contentVal.AsString();
        Assert.Contains("hello", content, StringComparison.Ordinal);
        Assert.Contains("world", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Tool execution validated", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Status404_OkFalseContentStillPresent()
    {
        using var server = LocalHttpServer.Start(_ => (404, "text/plain", "missing page"));
        var result = BuiltInFunctions.ExecuteWebFetch(Args(("url", server.Url("/missing"))));
        AssertFetchShape(result);
        var obj = result.AsObject();
        Assert.False(obj.Get("ok", null)!.AsBoolean());
        Assert.Equal(404, obj.Get("status", null)!.AsInteger());
        Assert.Equal("missing page", obj.Get("content", null)!.AsString());
    }

    [Fact]
    public void MaxBytesSmall_TruncatesContent()
    {
        using var server = LocalHttpServer.Start(_ => (200, "text/plain", "abcdefghijKLMNOP"));
        var args = new JsonObject();
        args.Set("url", RuntimeValue.String(server.Url("/long")));
        args.Set("maxBytes", RuntimeValue.Integer(8));
        var result = BuiltInFunctions.ExecuteWebFetch(RuntimeValue.Object(args));
        AssertFetchShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        Assert.True(obj.Get("truncated", null)!.AsBoolean());
        var content = obj.Get("content", null)!.AsString();
        Assert.True(content.Length <= 8, content);
        Assert.Equal("abcdefgh", content);
    }

    [Fact]
    public void RejectsFileSchemeAndEmptyUrl()
    {
        var fileResult = BuiltInFunctions.ExecuteWebFetch(Args(("url", "file:///tmp/secret.txt")));
        AssertFetchShape(fileResult);
        Assert.False(fileResult.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Contains("scheme", fileResult.AsObject().Get("error", null)!.AsString(), StringComparison.OrdinalIgnoreCase);

        var emptyResult = BuiltInFunctions.ExecuteWebFetch(Args(("url", "")));
        AssertFetchShape(emptyResult);
        Assert.False(emptyResult.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Contains("empty", emptyResult.AsObject().Get("error", null)!.AsString(), StringComparison.OrdinalIgnoreCase);

        var missingHost = BuiltInFunctions.ExecuteWebFetch(Args(("url", "http://")));
        AssertFetchShape(missingHost);
        Assert.False(missingHost.AsObject().Get("ok", null)!.AsBoolean());
    }

    [Fact]
    public void ToolExecute_FetchesLocalServer()
    {
        using var server = LocalHttpServer.Start(_ => (200, "text/plain", "via-execute"));
        var toolVal = BuiltInTools.CreateWebFetchTool();
        var tool = Assert.IsType<ToolInstance>(toolVal.AsObject());
        Assert.Equal("web_fetch", tool.Name);
        var result = tool.Execute(Args(("url", server.Url("/exec"))));
        AssertFetchShape(result);
        Assert.True(result.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Equal("via-execute", result.AsObject().Get("content", null)!.AsString());
        Assert.DoesNotContain("Tool execution validated", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Builtin_CallBuiltIn_MatchesHelper()
    {
        using var server = LocalHttpServer.Start(_ => (200, "text/plain", "via-builtin"));
        var viaBuiltin = BuiltInFunctions.CallBuiltIn(
            "webFetch",
            new List<RuntimeValue> { RuntimeValue.String(server.Url("/builtin")) },
            null);
        AssertFetchShape(viaBuiltin);
        Assert.True(viaBuiltin.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Equal("via-builtin", viaBuiltin.AsObject().Get("content", null)!.AsString());
    }

    private static RuntimeValue Args(params (string Name, string Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj.Set(name, RuntimeValue.String(value));
        return RuntimeValue.Object(obj);
    }

    private static void AssertFetchShape(RuntimeValue result)
    {
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        Assert.NotNull(obj.Get("ok", null));
        Assert.NotNull(obj.Get("status", null));
        Assert.NotNull(obj.Get("url", null));
        Assert.NotNull(obj.Get("content", null));
        Assert.NotNull(obj.Get("truncated", null));
        Assert.Equal(ValueType.Boolean, obj.Get("ok", null)!.Type);
        Assert.Equal(ValueType.Integer, obj.Get("status", null)!.Type);
        Assert.Equal(ValueType.String, obj.Get("url", null)!.Type);
        Assert.Equal(ValueType.String, obj.Get("content", null)!.Type);
        Assert.Equal(ValueType.Boolean, obj.Get("truncated", null)!.Type);
    }

    private sealed class LocalHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Func<HttpListenerRequest, (int Status, string ContentType, string Body)> _handler;

        public string BaseUrl { get; }

        private LocalHttpServer(
            HttpListener listener,
            string baseUrl,
            Func<HttpListenerRequest, (int Status, string ContentType, string Body)> handler)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _handler = handler;
            _loop = Task.Run(ListenLoop);
        }

        public static LocalHttpServer Start(
            Func<HttpListenerRequest, (int Status, string ContentType, string Body)> handler)
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var port = GetAvailablePort();
                var baseUrl = $"http://127.0.0.1:{port}";
                var listener = new HttpListener();
                listener.Prefixes.Add(baseUrl + "/");
                try
                {
                    listener.Start();
                    return new LocalHttpServer(listener, baseUrl, handler);
                }
                catch (Exception ex)
                {
                    last = ex;
                    try { listener.Close(); } catch { /* retry */ }
                }
            }

            throw new InvalidOperationException("Could not bind a local HttpListener for web_fetch tests.", last);
        }

        public string Url(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
                return BaseUrl + "/";
            return path.StartsWith('/') ? BaseUrl + path : BaseUrl + "/" + path;
        }

        private void ListenLoop()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    var (status, contentType, body) = _handler(ctx.Request);
                    var bytes = Encoding.UTF8.GetBytes(body ?? "");
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = contentType;
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { /* ignore */ }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _cts.Dispose();
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
