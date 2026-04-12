namespace TokenBee.Features.Replay;

public static class ReplayExtensions
{
    public static IServiceCollection AddReplay(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ISpanRecorder, SpanRecorder>();
        return services;
    }
}
