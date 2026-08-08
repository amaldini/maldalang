// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using Microsoft.Data.Sqlite;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// In-memory or SQLite-backed HTTP session data (values + flash for the next request).
/// </summary>
public sealed class SessionRecord
{
    public Dictionary<string, RuntimeValue> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RuntimeValue> PendingFlash { get; } = new(StringComparer.Ordinal);
}

public interface ISessionStore
{
    bool TryLoad(string sessionId, out SessionRecord record);
    void Save(string sessionId, SessionRecord record);
    void Delete(string sessionId);
}

public sealed class MemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions =
        new(StringComparer.Ordinal);

    public bool TryLoad(string sessionId, out SessionRecord record)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            record = CloneRecord(existing);
            return true;
        }

        record = new SessionRecord();
        return false;
    }

    public void Save(string sessionId, SessionRecord record)
    {
        _sessions[sessionId] = CloneRecord(record);
    }

    public void Delete(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    private static SessionRecord CloneRecord(SessionRecord source)
    {
        var clone = new SessionRecord();
        foreach (var kvp in source.Values)
        {
            clone.Values[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in source.PendingFlash)
        {
            clone.PendingFlash[kvp.Key] = kvp.Value;
        }

        return clone;
    }
}

public sealed class SqliteSessionStore : ISessionStore, IDisposable
{
    private static readonly object SqliteInitLock = new();
    private static bool _sqliteProviderInitialized;
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public const string DefaultPath = "./.malda/sessions.db";

    public SqliteSessionStore(string? databasePath = null)
    {
        EnsureSqliteProviderInitialized();
        var path = string.IsNullOrWhiteSpace(databasePath) ? DefaultPath : databasePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        EnsureSchema();
    }

    public bool TryLoad(string sessionId, out SessionRecord record)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT data_json FROM sessions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", sessionId);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(json))
            {
                record = new SessionRecord();
                return false;
            }

            record = DeserializeRecord(json);
            return true;
        }
    }

    public void Save(string sessionId, SessionRecord record)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sessions(id, data_json, updated_at)
                VALUES ($id, $data, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    data_json = excluded.data_json,
                    updated_at = excluded.updated_at;";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$data", SerializeRecord(record));
            cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(string sessionId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM sessions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                data_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureSqliteProviderInitialized()
    {
        if (_sqliteProviderInitialized)
        {
            return;
        }

        lock (SqliteInitLock)
        {
            if (_sqliteProviderInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _sqliteProviderInitialized = true;
        }
    }

    private static string SerializeRecord(SessionRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("values");
            WriteDictionary(writer, record.Values);
            writer.WritePropertyName("flash");
            WriteDictionary(writer, record.PendingFlash);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDictionary(Utf8JsonWriter writer, Dictionary<string, RuntimeValue> source)
    {
        writer.WriteStartObject();
        foreach (var kvp in source)
        {
            writer.WritePropertyName(kvp.Key);
            WriteRuntimeValue(writer, kvp.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteRuntimeValue(Utf8JsonWriter writer, RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.Null:
                writer.WriteNullValue();
                break;
            case ValueType.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            case ValueType.Integer:
                writer.WriteNumberValue(value.AsInteger());
                break;
            case ValueType.Float:
                writer.WriteNumberValue(value.AsFloat());
                break;
            case ValueType.String:
                writer.WriteStringValue(value.AsString());
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static SessionRecord DeserializeRecord(string json)
    {
        var record = new SessionRecord();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in values.EnumerateObject())
            {
                record.Values[prop.Name] = ReadRuntimeValue(prop.Value);
            }
        }

        if (root.TryGetProperty("flash", out var flash) && flash.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in flash.EnumerateObject())
            {
                record.PendingFlash[prop.Name] = ReadRuntimeValue(prop.Value);
            }
        }

        return record;
    }

    private static RuntimeValue ReadRuntimeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => RuntimeValue.Null(),
            JsonValueKind.True => RuntimeValue.Boolean(true),
            JsonValueKind.False => RuntimeValue.Boolean(false),
            JsonValueKind.String => RuntimeValue.String(element.GetString() ?? string.Empty),
            JsonValueKind.Number when element.TryGetInt64(out var i) => RuntimeValue.Integer((int)i),
            JsonValueKind.Number => RuntimeValue.Float(element.GetDouble()),
            _ => RuntimeValue.String(element.ToString())
        };
    }
}

public sealed class SessionOptions
{
    public string Secret { get; set; } = string.Empty;
    public string CookieName { get; set; } = WebRuntimeHelpers.DefaultSessionCookieName;
    public ISessionStore Store { get; set; } = new MemorySessionStore();
    public int? MaxAgeSeconds { get; set; } = 60 * 60 * 24 * 14;
}

/// <summary>
/// Request-scoped session API exposed as <c>req.session</c>.
/// </summary>
public class RequestSessionContextInstance : ObjectInstance
{
    private readonly SessionOptions _options;
    private readonly Dictionary<string, RuntimeValue> _values;
    private readonly Dictionary<string, RuntimeValue> _flashIncoming;
    private readonly Dictionary<string, RuntimeValue> _flashOutgoing = new(StringComparer.Ordinal);
    private bool _dirty;
    private bool _cleared;
    private bool _flashConsumedAll;

    public string SessionId { get; private set; }
    public bool IsNew { get; private set; }
    public bool IsDisabled { get; private set; }

    public RequestSessionContextInstance(
        string sessionId,
        bool isNew,
        SessionRecord record,
        SessionOptions options,
        bool disabled = false) : base(null)
    {
        SessionId = sessionId;
        IsNew = isNew;
        IsDisabled = disabled;
        _options = options;
        _values = new Dictionary<string, RuntimeValue>(record.Values, StringComparer.Ordinal);
        _flashIncoming = new Dictionary<string, RuntimeValue>(record.PendingFlash, StringComparer.Ordinal);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        return name switch
        {
            "id" => RuntimeValue.String(SessionId),
            "get" or "set" or "delete" or "clear" or "flash" or "getFlash" or "getFlashes" =>
                RuntimeValue.Function(new FunctionValue(null, null, false, null)
                {
                    BuiltInInstance = this,
                    BuiltInMethod = name
                }),
            _ => throw new Exception($"Undefined property '{name}' on Session context.")
        };
    }

    public override IEnumerable<string> GetAllKeys()
    {
        yield return "id";
        yield return "get";
        yield return "set";
        yield return "delete";
        yield return "clear";
        yield return "flash";
        yield return "getFlash";
        yield return "getFlashes";
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        return methodName switch
        {
            "get" => GetValue(args),
            "set" => SetValue(args),
            "delete" => DeleteValue(args),
            "clear" => Clear(args),
            "flash" => Flash(args),
            "getFlash" => GetFlash(args),
            "getFlashes" => GetFlashes(args),
            _ => throw new Exception($"Unknown method: {methodName}")
        };
    }

    public void CommitTo(ResponseContextInstance response, bool secureConnection)
    {
        if (IsDisabled)
        {
            return;
        }

        if (_cleared)
        {
            _options.Store.Delete(SessionId);
            var clearOptions = new JsonObject();
            clearOptions.Set("maxAge", RuntimeValue.Integer(0));
            clearOptions.Set("httpOnly", RuntimeValue.Boolean(true));
            clearOptions.Set("secure", RuntimeValue.Boolean(secureConnection));
            clearOptions.Set("sameSite", RuntimeValue.String("Lax"));
            clearOptions.Set("path", RuntimeValue.String("/"));
            response.AddSetCookieHeader(WebRuntimeHelpers.CreateCookieHeader(
                _options.CookieName,
                string.Empty,
                RuntimeValue.Object(clearOptions),
                useSecureDefaults: false));
            return;
        }

        var needsPersist = _dirty || IsNew || _flashOutgoing.Count > 0 || _flashIncoming.Count > 0 || _flashConsumedAll;
        if (!needsPersist)
        {
            return;
        }

        var record = new SessionRecord();
        foreach (var kvp in _values)
        {
            record.Values[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in _flashOutgoing)
        {
            record.PendingFlash[kvp.Key] = kvp.Value;
        }

        // Unread incoming flash is discarded (one-request lifetime).
        _options.Store.Save(SessionId, record);
        IsNew = false;
        _dirty = false;

        var signedId = WebRuntimeHelpers.CreateSecureCookieValue(
            SessionId,
            _options.Secret,
            _options.MaxAgeSeconds);
        var cookieOptions = new JsonObject();
        cookieOptions.Set("httpOnly", RuntimeValue.Boolean(true));
        cookieOptions.Set("secure", RuntimeValue.Boolean(secureConnection));
        cookieOptions.Set("sameSite", RuntimeValue.String("Lax"));
        cookieOptions.Set("path", RuntimeValue.String("/"));
        if (_options.MaxAgeSeconds.HasValue)
        {
            cookieOptions.Set("maxAge", RuntimeValue.Integer(_options.MaxAgeSeconds.Value));
        }

        response.AddSetCookieHeader(WebRuntimeHelpers.CreateCookieHeader(
            _options.CookieName,
            signedId,
            RuntimeValue.Object(cookieOptions),
            useSecureDefaults: false));
    }

    private RuntimeValue GetValue(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
        {
            throw new Exception("session.get() expects key string and optional default");
        }

        var key = args[0].AsString();
        if (_values.TryGetValue(key, out var value))
        {
            return value;
        }

        return args.Count == 2 ? args[1] : RuntimeValue.Null();
    }

    private RuntimeValue SetValue(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String)
        {
            throw new Exception("session.set() expects key string and value");
        }

        _values[args[0].AsString()] = args[1];
        _dirty = true;
        _cleared = false;
        return RuntimeValue.Null();
    }

    private RuntimeValue DeleteValue(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
        {
            throw new Exception("session.delete() expects 1 string key");
        }

        if (_values.Remove(args[0].AsString()))
        {
            _dirty = true;
        }

        return RuntimeValue.Null();
    }

    private RuntimeValue Clear(List<RuntimeValue> args)
    {
        if (args.Count != 0)
        {
            throw new Exception("session.clear() expects 0 arguments");
        }

        _values.Clear();
        _flashOutgoing.Clear();
        _flashIncoming.Clear();
        _cleared = true;
        _dirty = true;
        return RuntimeValue.Null();
    }

    private RuntimeValue Flash(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String)
        {
            throw new Exception("session.flash() expects key string and value");
        }

        _flashOutgoing[args[0].AsString()] = args[1];
        _dirty = true;
        _cleared = false;
        return RuntimeValue.Null();
    }

    private RuntimeValue GetFlash(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
        {
            throw new Exception("session.getFlash() expects key string and optional default");
        }

        var key = args[0].AsString();
        if (_flashIncoming.TryGetValue(key, out var value))
        {
            _flashIncoming.Remove(key);
            _dirty = true;
            return value;
        }

        return args.Count == 2 ? args[1] : RuntimeValue.Null();
    }

    private RuntimeValue GetFlashes(List<RuntimeValue> args)
    {
        if (args.Count != 0)
        {
            throw new Exception("session.getFlashes() expects 0 arguments");
        }

        var obj = new JsonObject();
        foreach (var kvp in _flashIncoming)
        {
            obj.Set(kvp.Key, kvp.Value);
        }

        _flashIncoming.Clear();
        _flashConsumedAll = true;
        _dirty = true;
        return RuntimeValue.Object(obj);
    }
}

public static class SessionRuntime
{
    public static SessionOptions? ParseEnableSessionArgs(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
        {
            throw new Exception("enableSession() expects secret string and optional options object");
        }

        if (args[0].Type != ValueType.String)
        {
            throw new Exception("enableSession() secret must be a string");
        }

        var secret = args[0].AsString();
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new Exception("enableSession() secret cannot be empty");
        }

        var options = new SessionOptions
        {
            Secret = secret,
            CookieName = WebRuntimeHelpers.DefaultSessionCookieName,
            Store = new MemorySessionStore(),
            MaxAgeSeconds = 60 * 60 * 24 * 14
        };

        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Object)
            {
                throw new Exception("enableSession() options must be an object");
            }

            var obj = args[1].AsObject();
            if (obj.TryGet("cookieName", out var cookieName) && cookieName.Type == ValueType.String)
            {
                options.CookieName = cookieName.AsString();
            }

            if (obj.TryGet("maxAge", out var maxAge) && maxAge.Type == ValueType.Integer)
            {
                options.MaxAgeSeconds = maxAge.AsInteger();
            }

            var storeKind = "memory";
            if (obj.TryGet("store", out var store) && store.Type == ValueType.String)
            {
                storeKind = store.AsString().Trim().ToLowerInvariant();
            }

            string? sqlitePath = null;
            if (obj.TryGet("sqlitePath", out var path) && path.Type == ValueType.String)
            {
                sqlitePath = path.AsString();
            }

            options.Store = storeKind switch
            {
                "memory" => new MemorySessionStore(),
                "sqlite" => new SqliteSessionStore(sqlitePath),
                _ => throw new Exception("enableSession() store must be \"memory\" or \"sqlite\"")
            };
        }

        return options;
    }

    public static RequestSessionContextInstance CreateSessionContext(
        IReadOnlyDictionary<string, string> cookies,
        SessionOptions? options)
    {
        if (options == null)
        {
            return new RequestSessionContextInstance(
                Guid.NewGuid().ToString("N"),
                isNew: true,
                new SessionRecord(),
                new SessionOptions { Secret = "disabled", Store = new MemorySessionStore() },
                disabled: true);
        }

        cookies.TryGetValue(options.CookieName, out var rawCookie);
        string sessionId;
        var isNew = true;
        var record = new SessionRecord();
        if (!string.IsNullOrEmpty(rawCookie) &&
            WebRuntimeHelpers.TryReadSecureCookieValue(rawCookie, options.Secret, out var plainId) &&
            !string.IsNullOrWhiteSpace(plainId))
        {
            sessionId = plainId;
            isNew = false;
            if (!options.Store.TryLoad(sessionId, out record))
            {
                record = new SessionRecord();
            }
        }
        else
        {
            sessionId = Guid.NewGuid().ToString("N");
        }

        return new RequestSessionContextInstance(sessionId, isNew, record, options);
    }

    public static void CommitSession(
        RequestContextInstance request,
        ResponseContextInstance response,
        bool secureConnection)
    {
        request.Session?.CommitTo(response, secureConnection);
    }
}
