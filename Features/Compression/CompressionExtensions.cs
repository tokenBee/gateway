namespace TokenBee.Features.Compression;

public static class CompressionExtensions
{
    public static IServiceCollection AddCompression(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("compressor");
        services.AddSingleton<ICompressionClient, CompressionClient>();
        return services;
    }
}
