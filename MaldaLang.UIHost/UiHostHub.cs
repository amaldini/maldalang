// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace MaldaLang.UIHost;

/// <summary>
/// Host side of UI protocol 1.1, shared by the embedded and the standalone host.
/// <para>
/// Two counters, two jobs. <c>sequence</c> is per WebSocket: every message the host sends on a
/// socket carries the next number, starting with <c>connected</c> = 1, and the client numbers
/// its messages per socket the same way. <c>revision</c> / <c>baseRevision</c> come from the
/// published session envelopes (<c>UiSession</c>) and version the tree: the client applies a
/// patch only on top of the revision it holds.
/// </para>
/// <para>
/// The hub keeps each session's tree by applying the published patches, so <c>resync</c>
/// answers with the whole current tree, never with the last delta.
/// </para>
/// </summary>
public sealed class UiHostHub
{
    public const string ProtocolVersion = "1.1";
    public const int MaxInboundBytes = 256 * 1024;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    /// <summary>Raised for each client <c>event</c> message, with the session id and the message.</summary>
    public event Action<string, JsonObject>? ClientEvent;

    /// <summary>
    /// Publishes a <c>mount</c> or <c>patch</c> session envelope (the JSON of <c>ui.mount</c> /
    /// <c>ui.render</c>) to every socket of the session. Returns an error message when the
    /// envelope is rejected, null when it was published. In-process callers (<c>UiHostLink</c>)
    /// have no size limit here; the HTTP route caps posts at <see cref="MaxInboundBytes"/>.
    /// </summary>
    public string? Publish(string sessionId, string kind, string envelopeJson)
    {
        if (kind != "mount" && kind != "patch")
            return $"unknown envelope kind '{kind}'";

        JsonObject envelope;
        try
        {
            if (JsonNode.Parse(envelopeJson) is not JsonObject parsed)
                return "envelope must be a JSON object";
            envelope = parsed;
        }
        catch (JsonException)
        {
            return "envelope must be valid JSON";
        }

        if (envelope["patches"] is not JsonArray patches)
            return "envelope must have a 'patches' array";
        if (!TryGetLong(envelope, "revision", out var revision))
            return "envelope must have a numeric 'revision'";

        var payload = envelope.ToJsonString();
        var session = Session(sessionId);
        lock (session.Gate)
        {
            if (kind == "mount")
            {
                session.Tree = UiTreePatcher.Apply(null, patches);
                session.TreeKnown = true;
            }
            else if (session.TreeKnown && TryGetLong(envelope, "baseRevision", out var baseRevision) && baseRevision == session.Revision)
            {
                session.Tree = UiTreePatcher.Apply(session.Tree, patches);
            }
            else
            {
                // A patch on a tree the hub never saw: resync has nothing whole to send until the next mount.
                session.TreeKnown = false;
                session.Tree = null;
            }

            session.Revision = revision;

            // Enqueued under the session lock, so every socket sees publishes in revision order.
            foreach (var connection in session.Connections.Values)
                connection.Enqueue(sequence => Message(kind, sessionId, sequence, payload));
        }

        return null;
    }

    /// <summary>Runs one browser WebSocket until it closes.</summary>
    public async Task AcceptAsync(WebSocket socket, string sessionId, CancellationToken cancellationToken)
    {
        var session = Session(sessionId);
        var connection = new Connection(socket);
        var connectionId = Guid.NewGuid();
        session.Connections[connectionId] = connection;
        connection.Enqueue(sequence => Message("connected", sessionId, sequence));
        var pump = connection.PumpAsync();

        try
        {
            var buffer = new byte[8 * 1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(socket, buffer, cancellationToken);
                if (text == null)
                    break;

                connection.LastSeenUtc = DateTime.UtcNow;
                HandleClientMessage(session, sessionId, connection, text);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down or request aborted.
        }
        catch (WebSocketException)
        {
            // Client went away mid-message.
        }
        finally
        {
            session.Connections.TryRemove(connectionId, out _);
            connection.Complete();
            await pump;
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    // Already closing.
                }
            }
        }
    }

    /// <summary>Pings every socket each 15 s and aborts sockets silent for 45 s.</summary>
    public async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var (sessionId, session) in _sessions)
                {
                    foreach (var connection in session.Connections.Values)
                    {
                        if (DateTime.UtcNow - connection.LastSeenUtc > StaleAfter)
                            connection.Socket.Abort();
                        else
                            connection.Enqueue(sequence => Message("ping", sessionId, sequence));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }
    }

    private void HandleClientMessage(SessionState session, string sessionId, Connection connection, string text)
    {
        if (text.Length == 0)
        {
            connection.Enqueue(sequence => Error(sessionId, sequence, "PayloadTooLarge", $"Inbound payload exceeded {MaxInboundBytes} bytes."));
            return;
        }

        JsonObject message;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed)
                throw new JsonException();
            message = parsed;
        }
        catch (JsonException)
        {
            connection.Enqueue(sequence => Error(sessionId, sequence, "InvalidJson", "Inbound websocket payload must be a JSON object."));
            return;
        }

        if (TryGetLong(message, "sequence", out var inbound))
        {
            if (inbound < connection.ExpectedInbound)
            {
                connection.Enqueue(sequence => Nack(sessionId, sequence, inbound, "DuplicateSequence", $"Expected {connection.ExpectedInbound}."));
                return;
            }

            if (inbound > connection.ExpectedInbound)
            {
                // Report the gap but keep the message: refusing it would refuse every later one too.
                var expected = connection.ExpectedInbound;
                connection.Enqueue(sequence => Nack(sessionId, sequence, inbound, "SequenceGap", $"Expected {expected}."));
            }

            connection.ExpectedInbound = inbound + 1;
        }

        switch ((string?)message["type"])
        {
            case "pong":
            case "ack":
                break;
            case "resync":
                lock (session.Gate)
                {
                    var snapshot = session.TreeKnown && session.Tree != null ? Snapshot(sessionId, session) : "null";
                    connection.Enqueue(sequence => Message("resync", sessionId, sequence, snapshot));
                }
                break;
            case "event":
                connection.Enqueue(sequence => Ack(sessionId, sequence, inbound));
                ClientEvent?.Invoke(sessionId, message);
                break;
            default:
                connection.Enqueue(sequence => Error(sessionId, sequence, "UnknownType", $"Unknown message type '{(string?)message["type"]}'."));
                break;
        }
    }

    private SessionState Session(string sessionId) => _sessions.GetOrAdd(sessionId, _ => new SessionState());

    /// <summary>A resync session envelope: one root <c>ReplaceNode</c> at the current revision.</summary>
    private static string Snapshot(string sessionId, SessionState session)
    {
        var replace = new JsonObject
        {
            ["op"] = "ReplaceNode",
            ["path"] = "/",
            ["value"] = session.Tree!.DeepClone()
        };
        return new JsonObject
        {
            ["type"] = "resync",
            ["version"] = ProtocolVersion,
            ["sessionId"] = sessionId,
            ["revision"] = session.Revision,
            ["patches"] = new JsonArray(replace)
        }.ToJsonString();
    }

    private static string Message(string type, string sessionId, long sequence, string? payloadJson = null, Action<Utf8JsonWriter>? extra = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("version", ProtocolVersion);
            writer.WriteString("sessionId", sessionId);
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("envelopeId", Guid.NewGuid().ToString("N"));
            writer.WriteString("serverTimeUtc", DateTime.UtcNow.ToString("O"));
            extra?.Invoke(writer);
            if (payloadJson != null)
            {
                writer.WritePropertyName("payload");
                writer.WriteRawValue(payloadJson, skipInputValidation: true);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Ack(string sessionId, long sequence, long ackSequence) =>
        Message("ack", sessionId, sequence, extra: w => w.WriteNumber("ackSequence", ackSequence));

    private static string Nack(string sessionId, long sequence, long ackSequence, string code, string text) =>
        Message("nack", sessionId, sequence, extra: w =>
        {
            w.WriteNumber("ackSequence", ackSequence);
            WriteError(w, code, text);
        });

    private static string Error(string sessionId, long sequence, string code, string text) =>
        Message("error", sessionId, sequence, extra: w => WriteError(w, code, text));

    private static void WriteError(Utf8JsonWriter writer, string code, string text)
    {
        writer.WriteStartObject("error");
        writer.WriteString("code", code);
        writer.WriteString("message", text);
        writer.WriteEndObject();
    }

    private static bool TryGetLong(JsonObject obj, string name, out long value)
    {
        value = 0;
        return obj[name] is JsonValue node && node.TryGetValue(out value);
    }

    /// <summary>Text of one message; an empty string when it exceeds the limit, null on close.</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var tooLarge = false;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType != WebSocketMessageType.Text)
                return null;

            if (!tooLarge)
            {
                stream.Write(buffer, 0, result.Count);
                tooLarge = stream.Length > MaxInboundBytes;
            }

            if (result.EndOfMessage)
                break;
        }

        return tooLarge ? string.Empty : Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class SessionState
    {
        public readonly object Gate = new();
        public readonly ConcurrentDictionary<Guid, Connection> Connections = new();
        public JsonNode? Tree;
        public bool TreeKnown;
        public long Revision;
    }

    /// <summary>
    /// One socket. Messages go through a channel to a single pump, because a WebSocket allows one
    /// send at a time; the sequence number is taken when a message is enqueued, so it matches
    /// the send order.
    /// </summary>
    private sealed class Connection
    {
        private readonly Channel<string> _outbound = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly object _gate = new();
        private long _nextSequence = 1;

        public Connection(WebSocket socket) => Socket = socket;

        public WebSocket Socket { get; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public long ExpectedInbound { get; set; } = 1;

        public void Enqueue(Func<long, string> build)
        {
            lock (_gate)
                _outbound.Writer.TryWrite(build(_nextSequence++));
        }

        public void Complete() => _outbound.Writer.TryComplete();

        public async Task PumpAsync()
        {
            try
            {
                await foreach (var text in _outbound.Reader.ReadAllAsync())
                {
                    if (Socket.State != WebSocketState.Open)
                        continue;
                    await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // The receive loop sees the close and removes the connection.
            }
            catch (ObjectDisposedException)
            {
                // Socket disposed while sending.
            }
        }
    }
}
