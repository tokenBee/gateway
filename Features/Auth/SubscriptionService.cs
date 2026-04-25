using Dapper;
using Npgsql;
using Stripe;
using Stripe.Checkout;

namespace TokenBee.Features.Auth;

// ─── Records ────────────────────────────────────────────────────

public record SubscriptionStatus(
    string UserId,
    string Status,
    long TokensThisMonth,
    long FreeTokensUsed,
    bool IsOverFreeLimit,
    string? StripeCustomerId,
    string? StripeSubscriptionId);

// ─── Interface ──────────────────────────────────────────────────

public interface ISubscriptionService
{
    Task<SubscriptionStatus> GetOrCreateAsync(string userId);
    Task IncrementUsageAsync(string userId, int tokenCount);
    Task<string> CreateCheckoutSessionAsync(string userId, string email, string returnUrl);
    Task<string> CreatePortalSessionAsync(string userId, string returnUrl);
    Task HandleWebhookAsync(string json, string signature);
}

// ─── Implementation ─────────────────────────────────────────────

public class SubscriptionService(IConfiguration config, ILogger<SubscriptionService> logger) : ISubscriptionService
{
    private readonly string _conn = config.GetConnectionString("Default")!;
    private readonly string _webhookSecret = config["Stripe:WebhookSecret"] ?? "";
    private readonly string _priceId = config["Stripe:PriceId"] ?? "";
    private const long FreeTokenLimit = 1_000_000;

    public async Task<SubscriptionStatus> GetOrCreateAsync(string userId)
    {
        try
        {
            const string selectSql = """
                SELECT user_id AS UserId, status,
                       COALESCE(tokens_this_month, 0) AS TokensThisMonth,
                       COALESCE(free_tokens_used, 0) AS FreeTokensUsed,
                       stripe_customer_id AS StripeCustomerId,
                       stripe_subscription_id AS StripeSubscriptionId
                FROM subscriptions
                WHERE user_id = @UserId
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var row = await connection.QueryFirstOrDefaultAsync<SubscriptionRow>(selectSql, new { UserId = userId });

            if (row is null)
            {
                const string insertSql = """
                    INSERT INTO subscriptions (id, user_id, status)
                    VALUES (@Id, @UserId, 'free')
                    ON CONFLICT (user_id) DO NOTHING
                    """;

                await connection.ExecuteAsync(insertSql, new { Id = Guid.NewGuid(), UserId = userId });

                return new SubscriptionStatus(userId, "free", 0, 0, false, null, null);
            }

            var isOverFreeLimit = row.FreeTokensUsed >= FreeTokenLimit && row.Status == "free";

            return new SubscriptionStatus(
                row.UserId,
                row.Status,
                row.TokensThisMonth,
                row.FreeTokensUsed,
                isOverFreeLimit,
                row.StripeCustomerId,
                row.StripeSubscriptionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get/create subscription for user {UserId}", userId);
            return new SubscriptionStatus(userId, "free", 0, 0, false, null, null);
        }
    }

    public async Task IncrementUsageAsync(string userId, int tokenCount)
    {
        try
        {
            const string upsertSql = """
                INSERT INTO subscriptions (id, user_id, status, tokens_this_month, free_tokens_used)
                VALUES (@Id, @UserId, 'free', @TokenCount, @TokenCount)
                ON CONFLICT (user_id) DO UPDATE SET
                    tokens_this_month = COALESCE(subscriptions.tokens_this_month, 0) + @TokenCount,
                    free_tokens_used = CASE
                        WHEN subscriptions.status = 'free'
                        THEN COALESCE(subscriptions.free_tokens_used, 0) + @TokenCount
                        ELSE COALESCE(subscriptions.free_tokens_used, 0)
                    END,
                    updated_at = NOW()
                RETURNING status, COALESCE(free_tokens_used, 0) AS FreeTokensUsed
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var result = await connection.QueryFirstAsync<UsageResult>(upsertSql, new { Id = Guid.NewGuid(), UserId = userId, TokenCount = tokenCount });

            // If user is past the free tier, insert a usage event for Stripe reporting
            if (result.Status != "free" || result.FreeTokensUsed > FreeTokenLimit)
            {
                const string eventSql = """
                    INSERT INTO usage_events (id, user_id, timestamp, request_count, reported_to_stripe)
                    VALUES (@Id, @UserId, NOW(), 1, FALSE)
                    """;

                await connection.ExecuteAsync(eventSql, new { Id = Guid.NewGuid(), UserId = userId });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to increment usage for user {UserId}", userId);
        }
    }

    public async Task<string> CreateCheckoutSessionAsync(string userId, string email, string returnUrl)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_conn);

            // Get or create subscription row
            var sub = await GetOrCreateAsync(userId);
            var customerId = sub.StripeCustomerId;

            // Create Stripe customer if needed
            if (string.IsNullOrEmpty(customerId))
            {
                var customerService = new CustomerService();
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = email,
                    Metadata = new Dictionary<string, string> { { "user_id", userId } }
                });
                customerId = customer.Id;

                await connection.ExecuteAsync(
                    "UPDATE subscriptions SET stripe_customer_id = @CustomerId WHERE user_id = @UserId",
                    new { CustomerId = customerId, UserId = userId });
            }

            // Create checkout session
            var sessionService = new SessionService();
            var session = await sessionService.CreateAsync(new SessionCreateOptions
            {
                Customer = customerId,
                Mode = "subscription",
                SuccessUrl = returnUrl + "?success=true",
                CancelUrl = returnUrl + "?canceled=true",
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = _priceId,
                        Quantity = 1
                    }
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { { "user_id", userId } }
                }
            });

            return session.Url;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create checkout session for user {UserId}", userId);
            throw;
        }
    }

    public async Task<string> CreatePortalSessionAsync(string userId, string returnUrl)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_conn);

            const string sql = "SELECT stripe_customer_id FROM subscriptions WHERE user_id = @UserId";
            var customerId = await connection.QueryFirstOrDefaultAsync<string?>(sql, new { UserId = userId });

            if (string.IsNullOrEmpty(customerId))
                throw new InvalidOperationException("No Stripe customer found for user");

            var portalService = new Stripe.BillingPortal.SessionService();
            var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl
            });

            return session.Url;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create portal session for user {UserId}", userId);
            throw;
        }
    }

    public async Task HandleWebhookAsync(string json, string signature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(json, signature, _webhookSecret);

            switch (stripeEvent.Type)
            {
                case "customer.subscription.created":
                case "customer.subscription.updated":
                {
                    var subscription = stripeEvent.Data.Object as Subscription;
                    if (subscription?.Metadata.TryGetValue("user_id", out var userId) == true)
                    {
                        var status = subscription.Status == "active" ? "paid" : subscription.Status;

                        const string sql = """
                            UPDATE subscriptions SET
                                stripe_subscription_id = @SubId,
                                status = @Status,
                                current_period_start = @PeriodStart,
                                current_period_end = @PeriodEnd,
                                updated_at = NOW()
                            WHERE user_id = @UserId
                            """;

                        // In Stripe SDK v51+, period dates live on subscription items
                        DateTimeOffset? periodStart = null;
                        DateTimeOffset? periodEnd = null;
                        if (subscription.Items?.Data?.Count > 0)
                        {
                            var firstItem = subscription.Items.Data[0];
                            periodStart = firstItem.CurrentPeriodStart;
                            periodEnd = firstItem.CurrentPeriodEnd;
                        }

                        await using var connection = new NpgsqlConnection(_conn);
                        await connection.ExecuteAsync(sql, new
                        {
                            SubId = subscription.Id,
                            Status = status,
                            PeriodStart = periodStart,
                            PeriodEnd = periodEnd,
                            UserId = userId
                        });
                    }
                    break;
                }
                case "customer.subscription.deleted":
                {
                    var subscription = stripeEvent.Data.Object as Subscription;
                    if (subscription?.Metadata.TryGetValue("user_id", out var userId) == true)
                    {
                        await using var connection = new NpgsqlConnection(_conn);
                        await connection.ExecuteAsync(
                            "UPDATE subscriptions SET status = 'free', updated_at = NOW() WHERE user_id = @UserId",
                            new { UserId = userId });
                    }
                    break;
                }
                case "invoice.payment_succeeded":
                {
                    var invoice = stripeEvent.Data.Object as Invoice;
                    if (invoice?.CustomerId is not null)
                    {
                        await using var connection = new NpgsqlConnection(_conn);
                        await connection.ExecuteAsync(
                            "UPDATE subscriptions SET requests_this_month = 0, updated_at = NOW() WHERE stripe_customer_id = @CustomerId",
                            new { CustomerId = invoice.CustomerId });

                        _ = ReportUsageToStripeAsync();
                    }
                    break;
                }
                case "invoice.payment_failed":
                {
                    var invoice = stripeEvent.Data.Object as Invoice;
                    if (invoice?.CustomerId is not null)
                    {
                        await using var connection = new NpgsqlConnection(_conn);
                        await connection.ExecuteAsync(
                            "UPDATE subscriptions SET status = 'past_due', updated_at = NOW() WHERE stripe_customer_id = @CustomerId",
                            new { CustomerId = invoice.CustomerId });
                    }
                    break;
                }
            }
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature validation failed");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to handle Stripe webhook");
            throw;
        }
    }

    // ─── Private Helpers ────────────────────────────────────────

    private async Task ReportUsageToStripeAsync()
    {
        try
        {
            const string sql = """
                SELECT id, user_id AS UserId, request_count AS RequestCount
                FROM usage_events
                WHERE reported_to_stripe = FALSE
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var events = (await connection.QueryAsync<UsageEvent>(sql)).ToList();

            foreach (var evt in events)
            {
                try
                {
                    var subIdSql = "SELECT stripe_subscription_id FROM subscriptions WHERE user_id = @UserId";
                    var subId = await connection.QueryFirstOrDefaultAsync<string?>(subIdSql, new { evt.UserId });

                    if (!string.IsNullOrEmpty(subId))
                    {
                        // Report usage via Billing Meter events (Stripe v51+)
                        var meterEventService = new Stripe.Billing.MeterEventService();
                        await meterEventService.CreateAsync(new Stripe.Billing.MeterEventCreateOptions
                        {
                            EventName = "api_requests",
                            Payload = new Dictionary<string, string>
                            {
                                { "stripe_customer_id", evt.UserId },
                                { "value", evt.RequestCount.ToString() }
                            }
                        });
                    }

                    await connection.ExecuteAsync(
                        "UPDATE usage_events SET reported_to_stripe = TRUE WHERE id = @Id",
                        new { evt.Id });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to report usage event {EventId} to Stripe", evt.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to report usage to Stripe");
        }
    }

    // ─── Internal DTOs ──────────────────────────────────────────

    private record SubscriptionRow(
        string UserId,
        string Status,
        long TokensThisMonth,
        long FreeTokensUsed,
        string? StripeCustomerId,
        string? StripeSubscriptionId);

    private record UsageResult(string Status, long FreeTokensUsed);

    private record UsageEvent(Guid Id, string UserId, int RequestCount);
}
