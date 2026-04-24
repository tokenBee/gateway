using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using TokenBee.Features.Auth;

namespace TokenBee.Shared.Auth;

public class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
{
    // Paths that skip API key authentication
    private static readonly string[] SkipPaths =
    [
        "/health",
        "/api/dashboard",
        "/api/replay",
        "/api/auth",
        "/api/stripe"
    ];

    // Per-user rate limiters
    private static readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new();
    
    private FixedWindowRateLimiter GetLimiter(string userId) =>
        _limiters.GetOrAdd(userId, _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    public async Task InvokeAsync(
        HttpContext ctx,
        IApiKeyService apiKeyService,
        ISubscriptionService subscriptionService)
    {
        var path = ctx.Request.Path.Value ?? "";

        // Skip auth for non-proxy paths
        if (ShouldSkip(path))
        {
            await next(ctx);
            return;
        }

        // Extract API key from Authorization header or X-TB-Key
        string? key = null;
        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            key = authHeader["Bearer ".Length..].Trim();
        }
        else
        {
            key = ctx.Request.Headers["X-TB-Key"].FirstOrDefault() 
                  ?? ctx.Request.Headers["X-TokenBee-Key"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(key))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Missing API key. Provide either 'Authorization: Bearer <key>' or 'X-TB-Key' header." });
            return;
        }

        // Validate API key
        var validatedKey = await apiKeyService.ValidateAsync(key);

        if (validatedKey is null)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        // Check subscription / free tier limit
        var subscription = await subscriptionService.GetOrCreateAsync(validatedKey.UserId);
        
        // 1. Rate Limiting (Shield against attacks)
        var limiter = GetLimiter(validatedKey.UserId);
        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            ctx.Response.StatusCode = 429;
            await ctx.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded. Max 10 requests every 10 seconds." });
            return;
        }

        // 2. Usage Quota (Business limit)
        if (subscription.IsOverFreeLimit)
        {
            ctx.Response.StatusCode = 429;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "Free tier limit reached (10,000 requests)",
                upgrade = "https://tokenbee.io/settings"
            });
            return;
        }

        // Set context items for downstream handlers
        ctx.Items["UserId"] = validatedKey.UserId;
        ctx.Items["KeyId"] = validatedKey.KeyId;
        ctx.Items["Subscription"] = subscription; // Attached for feature gating in ProxyHandler

        await next(ctx);

        // After the request completes, increment usage for /v1/* routes
        if (path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await subscriptionService.IncrementUsageAsync(validatedKey.UserId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background usage increment failed for {UserId}", validatedKey.UserId);
                }
            });
        }
    }

    private static bool ShouldSkip(string path)
    {
        foreach (var skip in SkipPaths)
        {
            if (path.Equals(skip, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(skip + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
