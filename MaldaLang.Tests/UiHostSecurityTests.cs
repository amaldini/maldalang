// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using MaldaLang.UIHost;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// The UI host's security defaults (<see cref="UiHostSecurity"/>): Origin checks on the embedded
/// host, the token → ticket flow on a standalone host started with <c>MALDA_UI_AUTH_TOKEN</c>,
/// and the refusal to listen off loopback without a token.
/// </summary>
[Collection("Sequential")]
public class UiHostSecurityTests : IClassFixture<UiHostProtocolTests.Hosts>
{
    private const string Token = "test-token-0123456789";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly UiHostProtocolTests.Hosts _hosts;

    public UiHostSecurityTests(UiHostProtocolTests.Hosts hosts) => _hosts = hosts;

    [Theory]
    [InlineData("http://evil.example", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    [InlineData("http://localhost:3000", HttpStatusCode.SwitchingProtocols)]
    [InlineData("http://127.0.0.1:1", HttpStatusCode.SwitchingProtocols)]
    [InlineData(null, HttpStatusCode.SwitchingProtocols)]
    public async Task WebSocket_ChecksTheOrigin(string? origin, HttpStatusCode expected)
    {
        var baseUrl = await _hosts.BaseUrlAsync("embedded");
        Assert.Equal(expected, await ConnectAsync(baseUrl, "/ui/ws/origin-check", origin));
    }

    [Fact]
    public async Task WebSocket_AcceptsItsOwnOrigin()
    {
        var baseUrl = await _hosts.BaseUrlAsync("embedded");
        Assert.Equal(HttpStatusCode.SwitchingProtocols, await ConnectAsync(baseUrl, "/ui/ws/origin-self", baseUrl));
    }

    [Fact]
    public async Task WebSocket_AcceptsAListedOrigin()
    {
        var baseUrl = await _hosts.BaseUrlAsync("embedded");
        var previous = System.Environment.GetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable);
        System.Environment.SetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable, "https://app.example, https://other.example/");
        try
        {
            Assert.Equal(HttpStatusCode.SwitchingProtocols, await ConnectAsync(baseUrl, "/ui/ws/origin-listed", "https://other.example"));
            Assert.Equal(HttpStatusCode.Forbidden, await ConnectAsync(baseUrl, "/ui/ws/origin-listed", "https://third.example"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable, previous);
        }
    }

    [Fact]
    public async Task Publish_RefusesAForeignOrigin()
    {
        var baseUrl = await _hosts.BaseUrlAsync("embedded");
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/ui/patch/origin-post")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("Origin", "http://evil.example");

        using var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_IsNeededForTicketsAndPublishing_AndNeverTakenFromTheQuery()
    {
        await using var host = await TokenHost.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host.Url + "/ui/ticket", token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host.Url + "/ui/ticket?token=" + Token, token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host.Url + "/ui/ticket", token: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(host.Url + "/ui/mount/s", token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ConnectAsync(host.Url, "/ui/ws/s?token=" + Token, origin: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await ConnectAsync(host.Url, "/ui/ws/s", origin: null));

        using var ticketResponse = await PostAsync(host.Url + "/ui/ticket", Token);
        Assert.Equal(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticket = (string)JsonNode.Parse(await ticketResponse.Content.ReadAsStringAsync())!["ticket"]!;

        Assert.Equal(HttpStatusCode.SwitchingProtocols, await ConnectAsync(host.Url, "/ui/ws/s?ticket=" + ticket, origin: null));
        // Single use.
        Assert.Equal(HttpStatusCode.Unauthorized, await ConnectAsync(host.Url, "/ui/ws/s?ticket=" + ticket, origin: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await ConnectAsync(host.Url, "/ui/ws/s?ticket=made-up", origin: null));
    }

    [Fact]
    public void Ticket_ExpiresAndIsSingleUse()
    {
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var store = new UiTicketStore(TimeSpan.FromSeconds(30), () => now);

        var used = store.Issue();
        Assert.True(store.TryRedeem(used));
        Assert.False(store.TryRedeem(used));

        var late = store.Issue();
        now = now.AddSeconds(31);
        Assert.False(store.TryRedeem(late));
        Assert.False(store.TryRedeem("never-issued"));
    }

    [Theory]
    [InlineData("http://localhost:50114", null, true)]
    [InlineData("http://127.0.0.1:5000", null, true)]
    [InlineData("http://[::1]:5000", null, true)]
    [InlineData("http://localhost:1;http://127.0.0.1:2", null, true)]
    [InlineData("http://0.0.0.0:5000", null, false)]
    [InlineData("http://*:5000", null, false)]
    [InlineData("http://+:5000", null, false)]
    [InlineData("http://[::]:5000", null, false)]
    [InlineData("http://192.168.1.10:5000", null, false)]
    [InlineData("http://myhost:5000", null, false)]
    [InlineData("http://localhost:1;http://0.0.0.0:2", null, false)]
    [InlineData("http://0.0.0.0:5000", "secret", true)]
    public void ListenUrls_OffLoopbackNeedAToken(string urls, string? token, bool allowed)
    {
        Assert.Equal(allowed, UiHostSecurity.CheckListenUrls(urls, token) == null);
    }

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://127.0.0.1", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("http://evil.example", false)]
    [InlineData("http://localhost.evil.example", false)]
    [InlineData("file://", false)]
    [InlineData("null", false)]
    public void CrossOrigin_LoopbackOnlyByDefault(string origin, bool allowed)
    {
        var previous = System.Environment.GetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable);
        System.Environment.SetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable, null);
        try
        {
            Assert.Equal(allowed, UiHostSecurity.IsCrossOriginAllowed(origin));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(UiHostSecurity.AllowedOriginVariable, previous);
        }
    }

    [Fact]
    public async Task StandaloneHost_RefusesToListenOffLoopbackWithoutAToken()
    {
        var startInfo = StandaloneStartInfo($"http://0.0.0.0:{UiHostProtocolTests.Hosts.FreePort()}");
        startInfo.Environment.Remove(UiHostSecurity.TokenVariable);
        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(30_000);
        if (!exited)
            process.Kill(entireProcessTree: true);

        Assert.True(exited, "the host kept running off loopback without a token");
        Assert.Equal(1, process.ExitCode);
        Assert.Contains(UiHostSecurity.TokenVariable, await stderr);
    }

    private static async Task<HttpResponseMessage> PostAsync(string url, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (token != null)
            request.Headers.Add(UiHostSecurity.TokenHeader, token);
        return await Http.SendAsync(request);
    }

    /// <summary>The handshake's status: 101 when the socket opened.</summary>
    private static async Task<HttpStatusCode> ConnectAsync(string baseUrl, string pathAndQuery, string? origin)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        if (origin != null)
            socket.Options.SetRequestHeader("Origin", origin);
        var uri = new Uri(baseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + pathAndQuery);
        try
        {
            await socket.ConnectAsync(uri, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // HttpStatusCode carries the refusal.
        }
        return socket.HttpStatusCode;
    }

    private static ProcessStartInfo StandaloneStartInfo(string url)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "MaldaLang.UIHost.dll"));
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(url);
        return startInfo;
    }

    /// <summary>A standalone host with <see cref="Token"/> set, on a free loopback port.</summary>
    private sealed class TokenHost : IAsyncDisposable
    {
        private readonly Process _process;

        private TokenHost(Process process, string url)
        {
            _process = process;
            Url = url;
        }

        public string Url { get; }

        public static async Task<TokenHost> StartAsync()
        {
            var url = $"http://127.0.0.1:{UiHostProtocolTests.Hosts.FreePort()}";
            var startInfo = StandaloneStartInfo(url);
            startInfo.Environment[UiHostSecurity.TokenVariable] = Token;
            var process = Process.Start(startInfo)!;
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var host = new TokenHost(process, url);

            for (var i = 0; i < 100; i++)
            {
                try
                {
                    if ((await Http.GetAsync(url + "/health")).IsSuccessStatusCode)
                        return host;
                }
                catch (HttpRequestException)
                {
                    // Still starting.
                }
                await Task.Delay(100);
            }
            await host.DisposeAsync();
            throw new Xunit.Sdk.XunitException("the standalone host with a token did not answer /health");
        }

        public ValueTask DisposeAsync()
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
