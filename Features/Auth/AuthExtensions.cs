using Stripe;

namespace TokenBee.Features.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Stripe
        StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"];

        // Register services
        services.AddSingleton<IApiKeyService, ApiKeyService>();
        services.AddSingleton<ISubscriptionService, SubscriptionService>();
        services.AddSingleton<ICaptureSettingsService, CaptureSettingsService>();
        services.AddSingleton<TokenBee.Shared.Auth.SupabaseUserService>();
        services.AddHttpClient("supabase-auth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        return services;
    }
}
