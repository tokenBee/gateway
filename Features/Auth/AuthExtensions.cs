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

        return services;
    }
}
