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
app.MapUiHost(new UiHostHub());
app.Run();
