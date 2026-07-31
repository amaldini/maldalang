using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("MaldaUiCors", policy =>
    {
        var allowedOrigin = System.Environment.GetEnvironmentVariable("MALDA_UI_ALLOWED_ORIGIN");
        if (string.IsNullOrWhiteSpace(allowedOrigin) || allowedOrigin == "*")
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigin).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();
app.UseCors("MaldaUiCors");
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

const string protocolVersion = "1.0";
const int maxInboundMessageBytes = 256 * 1024;
var authToken = System.Environment.GetEnvironmentVariable("MALDA_UI_AUTH_TOKEN");

var socketsBySession = new ConcurrentDictionary<string, ConcurrentDictionary<Guid, SocketState>>(StringComparer.Ordinal);
var lastEnvelopeBySession = new ConcurrentDictionary<string, JsonElement>(StringComparer.Ordinal);

var heartbeatCts = new CancellationTokenSource();
_ = Task.Run(() => HeartbeatLoopAsync(heartbeatCts.Token));
app.Lifetime.ApplicationStopping.Register(() => heartbeatCts.Cancel());

app.MapGet("/health", () => Results.Ok(new { ok = true, protocolVersion }));

app.Map("/ui/ws/{sessionId}", async (HttpContext context, string sessionId) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    if (!AuthorizeRequest(context))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var socketId = Guid.NewGuid();
    var state = new SocketState(socket);
    var sessionSockets = socketsBySession.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, SocketState>());
    sessionSockets[socketId] = state;

    await SendJsonAsync(socket, new
    {
        type = "connected",
        sessionId,
        version = protocolVersion,
        envelopeId = Guid.NewGuid().ToString("N"),
        sequence = 1,
        serverTimeUtc = DateTime.UtcNow.ToString("O")
    });

    var receiveBuffer = new byte[8 * 1024];
    while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
    {
        var message = await ReceiveTextMessageAsync(socket, receiveBuffer, context.RequestAborted);
        if (message == null)
        {
            break;
        }

        if (message.Length > maxInboundMessageBytes)
        {
            await SendProtocolErrorAsync(socket, sessionId, "PayloadTooLarge", $"Inbound payload exceeded {maxInboundMessageBytes} bytes.");
            continue;
        }

        JsonDocument? parsed;
        try
        {
            parsed = JsonDocument.Parse(message);
        }
        catch
        {
            await SendProtocolErrorAsync(socket, sessionId, "InvalidJson", "Inbound websocket payload must be valid JSON.");
            continue;
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            var inboundType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "event" : "event";
            var inboundSequence = root.TryGetProperty("sequence", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number ? seqProp.GetInt32() : state.ExpectedInboundSequence;
            var inboundEnvelopeId = root.TryGetProperty("envelopeId", out var envProp) ? envProp.GetString() : null;

            if (inboundSequence < state.ExpectedInboundSequence)
            {
                await SendJsonAsync(socket, BuildNackEnvelope(sessionId, inboundSequence, inboundEnvelopeId, "DuplicateSequence", $"Expected {state.ExpectedInboundSequence}."));
                continue;
            }

            if (inboundSequence > state.ExpectedInboundSequence)
            {
                await SendJsonAsync(socket, BuildNackEnvelope(sessionId, inboundSequence, inboundEnvelopeId, "SequenceGap", $"Expected {state.ExpectedInboundSequence}."));
                await SendResyncIfAvailableAsync(socket, sessionId);
                continue;
            }

            state.ExpectedInboundSequence++;
            state.LastSeenUtc = DateTime.UtcNow;

            if (inboundType == "pong")
            {
                await SendJsonAsync(socket, BuildAckEnvelope(sessionId, inboundSequence, inboundEnvelopeId));
                continue;
            }

            if (inboundType == "resync")
            {
                await SendResyncIfAvailableAsync(socket, sessionId);
                await SendJsonAsync(socket, BuildAckEnvelope(sessionId, inboundSequence, inboundEnvelopeId));
                continue;
            }

            var payload = root.TryGetProperty("payload", out var payloadProp) ? ToLooseObject(payloadProp) : new { };
            await BroadcastAsync(sessionId, new
            {
                type = "event",
                version = protocolVersion,
                sessionId,
                sequence = state.NextOutboundSequence++,
                envelopeId = Guid.NewGuid().ToString("N"),
                serverTimeUtc = DateTime.UtcNow.ToString("O"),
                payload
            });
            await SendJsonAsync(socket, BuildAckEnvelope(sessionId, inboundSequence, inboundEnvelopeId));
        }
    }

    sessionSockets.TryRemove(socketId, out _);
    if (sessionSockets.IsEmpty)
    {
        socketsBySession.TryRemove(sessionId, out _);
    }

    if (socket.State == WebSocketState.Open)
    {
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", context.RequestAborted);
    }
});

app.MapPost("/ui/mount/{sessionId}", async (string sessionId, HttpContext context) =>
{
    if (!AuthorizeRequest(context))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var payloadText = await reader.ReadToEndAsync();
    if (payloadText.Length > maxInboundMessageBytes)
    {
        return Results.BadRequest(new { error = "payload too large" });
    }

    var payload = ParseOrRaw(payloadText);
    var envelope = new
    {
        type = "mount",
        version = protocolVersion,
        sessionId,
        sequence = NextServerSequence(sessionId),
        envelopeId = Guid.NewGuid().ToString("N"),
        serverTimeUtc = DateTime.UtcNow.ToString("O"),
        payload
    };
    CacheEnvelope(sessionId, envelope);
    await BroadcastAsync(sessionId, envelope);
    return Results.Ok(new { delivered = true, protocolVersion });
});

app.MapPost("/ui/patch/{sessionId}", async (string sessionId, HttpContext context) =>
{
    if (!AuthorizeRequest(context))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var payloadText = await reader.ReadToEndAsync();
    if (payloadText.Length > maxInboundMessageBytes)
    {
        return Results.BadRequest(new { error = "payload too large" });
    }

    var payload = ParseOrRaw(payloadText);
    var envelope = new
    {
        type = "patch",
        version = protocolVersion,
        sessionId,
        sequence = NextServerSequence(sessionId),
        envelopeId = Guid.NewGuid().ToString("N"),
        serverTimeUtc = DateTime.UtcNow.ToString("O"),
        payload
    };
    CacheEnvelope(sessionId, envelope);
    await BroadcastAsync(sessionId, envelope);
    return Results.Ok(new { delivered = true, protocolVersion });
});

app.Run();
return;

bool AuthorizeRequest(HttpContext context)
{
    if (string.IsNullOrWhiteSpace(authToken))
    {
        return true;
    }

    if (!context.Request.Headers.TryGetValue("X-Malda-UI-Auth", out var token))
    {
        token = context.Request.Query["token"];
    }

    return string.Equals(token.ToString(), authToken, StringComparison.Ordinal);
}

object ParseOrRaw(string text)
{
    try
    {
        return JsonSerializer.Deserialize<object>(text) ?? new { raw = text };
    }
    catch
    {
        return new { raw = text };
    }
}

void CacheEnvelope(string sessionId, object envelope)
{
    var json = JsonSerializer.Serialize(envelope);
    using var doc = JsonDocument.Parse(json);
    lastEnvelopeBySession[sessionId] = doc.RootElement.Clone();
}

async Task SendResyncIfAvailableAsync(WebSocket socket, string sessionId)
{
    if (lastEnvelopeBySession.TryGetValue(sessionId, out var cached))
    {
        await SendJsonAsync(socket, new
        {
            type = "resync",
            version = protocolVersion,
            sessionId,
            sequence = 0,
            envelopeId = Guid.NewGuid().ToString("N"),
            serverTimeUtc = DateTime.UtcNow.ToString("O"),
            payload = ToLooseObject(cached)
        });
    }
}

object BuildAckEnvelope(string sessionId, int inboundSequence, string? inboundEnvelopeId) => new
{
    type = "ack",
    version = protocolVersion,
    sessionId,
    sequence = 0,
    ackSequence = inboundSequence,
    envelopeId = inboundEnvelopeId ?? Guid.NewGuid().ToString("N"),
    serverTimeUtc = DateTime.UtcNow.ToString("O")
};

object BuildNackEnvelope(string sessionId, int inboundSequence, string? inboundEnvelopeId, string code, string message) => new
{
    type = "nack",
    version = protocolVersion,
    sessionId,
    sequence = 0,
    ackSequence = inboundSequence,
    envelopeId = inboundEnvelopeId ?? Guid.NewGuid().ToString("N"),
    serverTimeUtc = DateTime.UtcNow.ToString("O"),
    error = new { code, message }
};

int NextServerSequence(string sessionId)
{
    var sockets = socketsBySession.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, SocketState>());
    var max = 1;
    foreach (var state in sockets.Values)
    {
        if (state.NextOutboundSequence > max)
        {
            max = state.NextOutboundSequence;
        }
    }

    foreach (var state in sockets.Values)
    {
        state.NextOutboundSequence = max + 1;
    }

    return max;
}

async Task<string?> ReceiveTextMessageAsync(WebSocket socket, byte[] receiveBuffer, CancellationToken cancellationToken)
{
    var stream = new MemoryStream();
    while (true)
    {
        var result = await socket.ReceiveAsync(receiveBuffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        if (result.MessageType != WebSocketMessageType.Text)
        {
            return null;
        }

        stream.Write(receiveBuffer, 0, result.Count);
        if (stream.Length > maxInboundMessageBytes)
        {
            return string.Empty;
        }

        if (result.EndOfMessage)
        {
            break;
        }
    }

    return Encoding.UTF8.GetString(stream.ToArray());
}

async Task BroadcastAsync(string sessionId, object message)
{
    if (!socketsBySession.TryGetValue(sessionId, out var sockets) || sockets.IsEmpty)
    {
        return;
    }

    var dead = new List<Guid>();
    foreach (var kvp in sockets)
    {
        var state = kvp.Value;
        var socket = state.Socket;
        if (socket.State != WebSocketState.Open)
        {
            dead.Add(kvp.Key);
            continue;
        }

        try
        {
            await SendJsonAsync(socket, message);
        }
        catch
        {
            dead.Add(kvp.Key);
        }
    }

    foreach (var deadSocket in dead)
    {
        sockets.TryRemove(deadSocket, out _);
    }
}

async Task SendProtocolErrorAsync(WebSocket socket, string sessionId, string code, string message)
{
    await SendJsonAsync(socket, new
    {
        type = "error",
        version = protocolVersion,
        sessionId,
        envelopeId = Guid.NewGuid().ToString("N"),
        serverTimeUtc = DateTime.UtcNow.ToString("O"),
        error = new { code, message }
    });
}

async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
    while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
    {
        foreach (var sessionSockets in socketsBySession.Values)
        {
            foreach (var kvp in sessionSockets)
            {
                var state = kvp.Value;
                if (state.Socket.State != WebSocketState.Open)
                {
                    continue;
                }

                if (DateTime.UtcNow - state.LastSeenUtc > TimeSpan.FromSeconds(45))
                {
                    try
                    {
                        await state.Socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "heartbeat timeout", cancellationToken);
                    }
                    catch
                    {
                        // Ignore close errors.
                    }
                    continue;
                }

                await SendJsonAsync(state.Socket, new
                {
                    type = "ping",
                    version = protocolVersion,
                    envelopeId = Guid.NewGuid().ToString("N"),
                    serverTimeUtc = DateTime.UtcNow.ToString("O")
                });
            }
        }
    }
}

static async Task SendJsonAsync(WebSocket socket, object message)
{
    var json = JsonSerializer.Serialize(message);
    var bytes = Encoding.UTF8.GetBytes(json);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
}

static object ToLooseObject(JsonElement element)
{
    return JsonSerializer.Deserialize<object>(element.GetRawText()) ?? new { };
}

sealed class SocketState
{
    public WebSocket Socket { get; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public int ExpectedInboundSequence { get; set; } = 1;
    public int NextOutboundSequence { get; set; } = 2;

    public SocketState(WebSocket socket)
    {
        Socket = socket;
    }
}
