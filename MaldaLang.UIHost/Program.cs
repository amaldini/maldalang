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
    // Loopback origins and MALDA_UI_ALLOWED_ORIGIN, the same rule as the Origin check.
    options.AddPolicy("MaldaUiCors", policy =>
        policy.SetIsOriginAllowed(UiHostSecurity.IsCrossOriginAllowed).AllowAnyHeader().AllowAnyMethod());
});

// Refuse before listening when the configured URLs are off loopback without a token...
if (UiHostSecurity.CheckListenUrls(builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey) ?? "http://localhost:5000") is { } urlError)
{
    Console.Error.WriteLine(urlError);
    return 1;
}

var app = builder.Build();
app.UseCors("MaldaUiCors");
app.UseRateLimiter();
app.MapUiHost(new UiHostHub());
await app.StartAsync();

// ...and after, on the addresses actually bound (Kestrel endpoints, ASPNETCORE_HTTP_PORTS).
if (UiHostSecurity.CheckListenUrls(string.Join(';', app.Urls)) is { } boundError)
{
    Console.Error.WriteLine(boundError);
    await app.StopAsync();
    return 1;
}

await app.WaitForShutdownAsync();
return 0;
