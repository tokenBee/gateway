using Microsoft.Extensions.Http;
using Polly;
using Serilog;
using TokenScope.Features.Observability;
using TokenScope.Shared.Proxy;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Named HttpClient "llm" with 30s timeout and Polly retry
    builder.Services.AddHttpClient("llm", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt)));

    // Observability (ITraceLogger)
    builder.Services.AddObservability(builder.Configuration);

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapPost("/v1/{**path}", ProxyHandler.Handle);

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