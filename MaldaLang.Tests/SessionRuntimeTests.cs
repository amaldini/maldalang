// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class SessionRuntimeTests
{
    [Fact]
    public void Session_SetGet_PersistsAcrossCommitAndReload()
    {
        var store = new MemorySessionStore();
        var options = new SessionOptions
        {
            Secret = "test-session-secret",
            CookieName = "malda_session",
            Store = store,
            MaxAgeSeconds = 3600
        };

        var cookies1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var session1 = SessionRuntime.CreateSessionContext(cookies1, options);
        var req1 = new RequestContextInstance(
            "GET", "/", new Dictionary<string, string>(), new Dictionary<string, string>(),
            cookies1, RuntimeValue.Null(), sessionOptions: options);
        req1.AttachSession(session1);

        session1.CallMethod("set", new List<RuntimeValue>
        {
            RuntimeValue.String("userId"),
            RuntimeValue.String("alice")
        });

        var res1 = new ResponseContextInstance();
        SessionRuntime.CommitSession(req1, res1, secureConnection: false);

        var setCookie = res1.Get("headers").AsObject(); // may not expose Set-Cookie as headers
        // Reload via signed cookie extracted from AddSetCookieHeader path:
        var sessionId = session1.SessionId;
        var signed = WebRuntimeHelpers.CreateSecureCookieValue(sessionId, options.Secret, options.MaxAgeSeconds);
        var cookies2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [options.CookieName] = signed
        };

        var session2 = SessionRuntime.CreateSessionContext(cookies2, options);
        Assert.False(session2.IsNew);
        var value = session2.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String("userId") });
        Assert.Equal(ValueType.String, value.Type);
        Assert.Equal("alice", value.AsString());
    }

    [Fact]
    public void Session_Flash_IsAvailableOnlyOnNextRequest()
    {
        var store = new MemorySessionStore();
        var options = new SessionOptions
        {
            Secret = "flash-secret",
            Store = store
        };

        var session1 = SessionRuntime.CreateSessionContext(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), options);
        session1.CallMethod("flash", new List<RuntimeValue>
        {
            RuntimeValue.String("notice"),
            RuntimeValue.String("Signed in")
        });

        var req1 = new RequestContextInstance(
            "GET", "/", new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), RuntimeValue.Null(), sessionOptions: options);
        req1.AttachSession(session1);
        var res1 = new ResponseContextInstance();
        SessionRuntime.CommitSession(req1, res1, false);

        var signed = WebRuntimeHelpers.CreateSecureCookieValue(session1.SessionId, options.Secret, options.MaxAgeSeconds);
        var cookies2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [options.CookieName] = signed
        };
        var session2 = SessionRuntime.CreateSessionContext(cookies2, options);
        var flash = session2.CallMethod("getFlash", new List<RuntimeValue> { RuntimeValue.String("notice") });
        Assert.Equal("Signed in", flash.AsString());

        var req2 = new RequestContextInstance(
            "GET", "/", new Dictionary<string, string>(), new Dictionary<string, string>(),
            cookies2, RuntimeValue.Null(), sessionOptions: options);
        req2.AttachSession(session2);
        var res2 = new ResponseContextInstance();
        SessionRuntime.CommitSession(req2, res2, false);

        var session3 = SessionRuntime.CreateSessionContext(cookies2, options);
        var missing = session3.CallMethod("getFlash", new List<RuntimeValue> { RuntimeValue.String("notice") });
        Assert.Equal(ValueType.Null, missing.Type);
    }

    [Fact]
    public void Session_Disabled_DoesNotPersist()
    {
        var session = SessionRuntime.CreateSessionContext(
            new Dictionary<string, string>(), null);
        Assert.True(session.IsDisabled);
        session.CallMethod("set", new List<RuntimeValue>
        {
            RuntimeValue.String("x"),
            RuntimeValue.Integer(1)
        });
        var res = new ResponseContextInstance();
        var req = new RequestContextInstance(
            "GET", "/", new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), RuntimeValue.Null());
        req.AttachSession(session);
        SessionRuntime.CommitSession(req, res, false);
        // No cookie headers expected when disabled
        Assert.False(res.HasHeaders);
    }

    [Fact]
    public void EnableSession_ParsesSqliteStoreOption()
    {
        var path = Path.Combine(Path.GetTempPath(), "malda-session-test-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var optionsObj = new JsonObject();
            optionsObj.Set("store", RuntimeValue.String("sqlite"));
            optionsObj.Set("sqlitePath", RuntimeValue.String(path));
            var options = SessionRuntime.ParseEnableSessionArgs(new List<RuntimeValue>
            {
                RuntimeValue.String("sqlite-secret"),
                RuntimeValue.Object(optionsObj)
            });
            Assert.NotNull(options);
            Assert.IsType<SqliteSessionStore>(options!.Store);

            var session = SessionRuntime.CreateSessionContext(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), options);
            session.CallMethod("set", new List<RuntimeValue>
            {
                RuntimeValue.String("k"),
                RuntimeValue.String("v")
            });
            var req = new RequestContextInstance(
                "GET", "/", new Dictionary<string, string>(), new Dictionary<string, string>(),
                new Dictionary<string, string>(), RuntimeValue.Null(), sessionOptions: options);
            req.AttachSession(session);
            var res = new ResponseContextInstance();
            SessionRuntime.CommitSession(req, res, false);

            var signed = WebRuntimeHelpers.CreateSecureCookieValue(session.SessionId, options.Secret, options.MaxAgeSeconds);
            var reloaded = SessionRuntime.CreateSessionContext(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [options.CookieName] = signed
                },
                options);
            Assert.Equal("v", reloaded.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String("k") }).AsString());
            (options.Store as IDisposable)?.Dispose();
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
