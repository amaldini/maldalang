using System.Text;
using MaldaLang.UIHost;
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

var authToken = System.Environment.GetEnvironmentVariable("MALDA_UI_AUTH_TOKEN");
var hub = new UiHostHub();

var heartbeatCts = new CancellationTokenSource();
_ = hub.RunHeartbeatAsync(heartbeatCts.Token);
app.Lifetime.ApplicationStopping.Register(() => heartbeatCts.Cancel());

app.MapGet("/health", () => Results.Ok(new { ok = true, protocolVersion = UiHostHub.ProtocolVersion }));

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
    await hub.AcceptAsync(socket, sessionId, context.RequestAborted);
});

app.MapPost("/ui/mount/{sessionId}", (string sessionId, HttpContext context) => PublishAsync(sessionId, "mount", context));
app.MapPost("/ui/patch/{sessionId}", (string sessionId, HttpContext context) => PublishAsync(sessionId, "patch", context));

app.Run();
return;

async Task<IResult> PublishAsync(string sessionId, string kind, HttpContext context)
{
    if (!AuthorizeRequest(context))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var error = hub.Publish(sessionId, kind, await reader.ReadToEndAsync());
    return error == null
        ? Results.Ok(new { delivered = true, protocolVersion = UiHostHub.ProtocolVersion })
        : Results.BadRequest(new { error });
}

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
