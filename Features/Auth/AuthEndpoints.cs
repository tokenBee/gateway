namespace TokenBee.Features.Auth;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/keys", CreateKey);
        group.MapGet("/keys/{userId}", GetKeys);
        group.MapDelete("/keys/{keyId}", RevokeKey);
        group.MapGet("/subscription/{userId}", GetSubscription);
        group.MapPost("/subscription/checkout", CreateCheckout);
        group.MapPost("/subscription/portal", CreatePortal);

        // Stripe webhook is mapped outside the /api/auth group
        app.MapPost("/api/stripe/webhook", HandleWebhook);

        return app;
    }

    // ──── POST /api/auth/keys ────

    private static async Task<IResult> CreateKey(
        CreateKeyRequest request,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
                return Results.BadRequest(new { error = "userId is required" });

            var result = await apiKeyService.CreateAsync(request.UserId, request.Name ?? "Unnamed Key");

            return Results.Ok(new
            {
                id = result.Id,
                key = result.PlainKey,
                prefix = result.Prefix,
                message = "Save this key — it won't be shown again"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create API key");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/auth/keys/{userId} ────

    private static async Task<IResult> GetKeys(
        string userId,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            var keys = await apiKeyService.GetByUserAsync(userId);
            return Results.Ok(keys);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get API keys");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── DELETE /api/auth/keys/{keyId}?userId=xxx ────

    private static async Task<IResult> RevokeKey(
        string keyId,
        string userId,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            var revoked = await apiKeyService.RevokeAsync(keyId, userId);

            return revoked
                ? Results.Ok(new { revoked = true })
                : Results.NotFound(new { error = "Key not found or already revoked" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to revoke API key");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/auth/subscription/{userId} ────

    private static async Task<IResult> GetSubscription(
        string userId,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            var status = await subscriptionService.GetOrCreateAsync(userId);
            return Results.Ok(status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get subscription status");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/auth/subscription/checkout ────

    private static async Task<IResult> CreateCheckout(
        CheckoutRequest request,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
                return Results.BadRequest(new { error = "userId is required" });

            var url = await subscriptionService.CreateCheckoutSessionAsync(
                request.UserId, request.Email ?? "", request.ReturnUrl ?? "/settings");

            return Results.Ok(new { url });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create checkout session");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/auth/subscription/portal ────

    private static async Task<IResult> CreatePortal(
        PortalRequest request,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
                return Results.BadRequest(new { error = "userId is required" });

            var url = await subscriptionService.CreatePortalSessionAsync(
                request.UserId, request.ReturnUrl ?? "/settings");

            return Results.Ok(new { url });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create portal session");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/stripe/webhook ────

    private static async Task<IResult> HandleWebhook(
        HttpContext ctx,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            // Read raw body — Stripe signature validation requires raw bytes
            ctx.Request.Body.Position = 0;
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();

            var signature = ctx.Request.Headers["Stripe-Signature"].FirstOrDefault() ?? "";

            await subscriptionService.HandleWebhookAsync(json, signature);

            return Results.Ok(new { received = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stripe webhook handling failed");
            return Results.Json(new { error = "Webhook processing failed" }, statusCode: 400);
        }
    }

    // ─── Request DTOs ───────────────────────────────────────────

    private record CreateKeyRequest(string UserId, string? Name);
    private record CheckoutRequest(string UserId, string? Email, string? ReturnUrl);
    private record PortalRequest(string UserId, string? ReturnUrl);
}
