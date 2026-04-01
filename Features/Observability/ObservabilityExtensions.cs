namespace TokenScope.Features.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ITraceLogger, TraceLogger>();
        return services;
    }
}
