using Microsoft.Extensions.Http;
using Polly;
using Serilog;
using TokenBee.Features.Auth;
using TokenBee.Features.Observability;
using TokenBee.Features.Compression;
using TokenBee.Features.Replay;
using TokenBee.Shared.Auth;
using TokenBee.Shared.Proxy;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // CORS for dashboard frontend
    builder.Services.AddCors(options => {
        options.AddDefaultPolicy(policy => {
            policy.WithOrigins("http://localhost:3000") // Dashboard origin
                  .AllowAnyMethod()
                  .WithHeaders("Content-Type",
                               "X-LLM-Key",
                               "X-TB-User-Id",
                               "X-TokenBee-Key",
                               "X-TB-Property-Feature",
                               "X-TB-Property-Environment"); // <--- CRITICAL
        });
    });

    // Named HttpClient "llm" with 30s timeout and Polly retry
    builder.Services.AddHttpClient("llm", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt)));

    // Observability (ITraceLogger + MetricsQueries)
    builder.Services.AddObservability(builder.Configuration);

    // Compression Sidecar
    builder.Services.AddCompression(builder.Configuration);

    // Replay (Session + Span Recording)
    builder.Services.AddReplay(builder.Configuration);

    // Auth (ApiKeyService + SubscriptionService + Stripe)
    builder.Services.AddAuth(builder.Configuration);

    var app = builder.Build();

    // Enable request body buffering for Stripe webhook raw body reading
    app.Use(async (context, next) =>
    {
        context.Request.EnableBuffering();
        await next();
    });

    app.UseCors();

    // API key middleware — validates /v1/* routes, skips dashboard/replay/auth
    app.UseMiddleware<ApiKeyMiddleware>();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapPost("/v1/{**path}", ProxyHandler.Handle);
    app.MapObservabilityEndpoints();
    app.MapReplayEndpoints();
    app.MapAuthEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
