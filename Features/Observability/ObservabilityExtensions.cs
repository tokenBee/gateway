namespace TokenBee.Features.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<TraceLogger>();
        services.AddSingleton<ITraceLogger>(sp => sp.GetRequiredService<TraceLogger>());
        services.AddHostedService(sp => sp.GetRequiredService<TraceLogger>());
        
        services.AddScoped<MetricsQueries>();
        return services;
    }
}
