using TokenBee.Features.Observability;
using TokenBee.Shared.Auth;

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
        group.MapGet("/capture-settings/{userId}", GetCaptureSettings);
        group.MapPatch("/capture-settings/{userId}", UpdateCaptureSettings);
        group.MapPost("/subscription/checkout", CreateCheckout);
        group.MapPost("/subscription/portal", CreatePortal);

        // Stripe webhook is mapped outside the /api/auth group
        app.MapPost("/api/stripe/webhook", HandleWebhook);

        return app;
    }

    // ──── POST /api/auth/keys ────

    private static async Task<IResult> CreateKey(
        HttpContext ctx,
        CreateKeyRequest request,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            var userId = ctx.RequireAccountId();
            var result = await apiKeyService.CreateAsync(userId, request.Name ?? "Unnamed Key");

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
        HttpContext ctx,
        string userId,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            var accountId = ctx.RequireAccountId();
            if (!string.Equals(userId, accountId, StringComparison.Ordinal))
                return Results.Json(new { error = "Forbidden" }, statusCode: 403);

            var keys = await apiKeyService.GetByUserAsync(accountId);
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
        HttpContext ctx,
        string keyId,
        IApiKeyService apiKeyService,
        ILogger<ApiKeyService> logger)
    {
        try
        {
            var revoked = await apiKeyService.RevokeAsync(keyId, ctx.RequireAccountId());

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
        HttpContext ctx,
        string userId,
        ISubscriptionService subscriptionService,
        MetricsQueries metricsQueries,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            var accountId = ctx.RequireAccountId();
            if (!string.Equals(userId, accountId, StringComparison.Ordinal))
                return Results.Json(new { error = "Forbidden" }, statusCode: 403);

            var status = await subscriptionService.GetOrCreateAsync(accountId);
            var plan = CaptureDecision.DisplayPlan(status.Status);
            var captured = await metricsQueries.CountCapturedThisMonthAsync(accountId);
            return Results.Ok(new
            {
                status.UserId,
                status.Status,
                plan,
                tokensThisMonth = status.TokensThisMonth,
                freeTokensUsed = status.FreeTokensUsed,
                isOverFreeLimit = status.IsOverFreeLimit,
                stripeCustomerId = status.StripeCustomerId,
                stripeSubscriptionId = status.StripeSubscriptionId,
                capturedInteractionsThisMonth = captured,
                capturedInteractionsLimit = CaptureDecision.MonthlyCaptureLimit(status.Status),
                maxRetentionDays = CaptureDecision.MaxRetentionDays(status.Status),
                allowedRetentionDays = CaptureDecision.AllowedRetentionDays(status.Status)
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get subscription status");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetCaptureSettings(
        HttpContext ctx,
        string userId,
        ICaptureSettingsService captureSettings,
        ISubscriptionService subscriptionService,
        ILogger<CaptureSettingsService> logger)
    {
        try
        {
            var accountId = ctx.RequireAccountId();
            if (!string.Equals(userId, accountId, StringComparison.Ordinal))
                return Results.Json(new { error = "Forbidden" }, statusCode: 403);

            var sub = await subscriptionService.GetOrCreateAsync(accountId);
            var settings = await captureSettings.GetOrCreateAsync(accountId, sub.Status);
            return Results.Ok(new
            {
                userId = settings.UserId,
                captureEnabled = settings.CaptureEnabled,
                retentionDays = settings.RetentionDays,
                captureMessages = settings.CaptureMessages,
                maxRetentionDays = captureSettings.MaxRetentionDays(sub.Status),
                allowedRetentionDays = CaptureDecision.AllowedRetentionDays(sub.Status),
                plan = CaptureDecision.DisplayPlan(sub.Status)
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get capture settings");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> UpdateCaptureSettings(
        HttpContext ctx,
        string userId,
        UpdateCaptureSettingsRequest request,
        ICaptureSettingsService captureSettings,
        ISubscriptionService subscriptionService,
        ILogger<CaptureSettingsService> logger)
    {
        try
        {
            var accountId = ctx.RequireAccountId();
            if (!string.Equals(userId, accountId, StringComparison.Ordinal))
                return Results.Json(new { error = "Forbidden" }, statusCode: 403);

            var sub = await subscriptionService.GetOrCreateAsync(accountId);
            var settings = await captureSettings.UpdateAsync(
                accountId, request.CaptureEnabled, request.RetentionDays, request.CaptureMessages, sub.Status);
            return Results.Ok(new
            {
                userId = settings.UserId,
                captureEnabled = settings.CaptureEnabled,
                retentionDays = settings.RetentionDays,
                captureMessages = settings.CaptureMessages,
                maxRetentionDays = captureSettings.MaxRetentionDays(sub.Status),
                allowedRetentionDays = CaptureDecision.AllowedRetentionDays(sub.Status),
                plan = CaptureDecision.DisplayPlan(sub.Status)
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update capture settings");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/auth/subscription/checkout ────

    private static async Task<IResult> CreateCheckout(
        HttpContext ctx,
        CheckoutRequest request,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            var userId = ctx.RequireAccountId();
            var url = await subscriptionService.CreateCheckoutSessionAsync(
                userId, request.Email ?? "", request.ReturnUrl ?? "/settings", request.Plan);

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
        HttpContext ctx,
        PortalRequest request,
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionService> logger)
    {
        try
        {
            var userId = ctx.RequireAccountId();
            var url = await subscriptionService.CreatePortalSessionAsync(
                userId, request.ReturnUrl ?? "/settings");

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
    private record CheckoutRequest(string UserId, string? Email, string? ReturnUrl, string? Plan);
    private record PortalRequest(string UserId, string? ReturnUrl);
    private record UpdateCaptureSettingsRequest(bool? CaptureEnabled, int? RetentionDays, bool? CaptureMessages);
}
