namespace TokenBee.Features.Observability;

public static class ObservabilityEndpoints
{
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .RequireCors("DashboardCors");

        group.MapGet("/summary", GetSummary);
        group.MapGet("/daily", GetDaily);
        group.MapGet("/by-model", GetByModel);
        group.MapGet("/by-user", GetByUser);
        group.MapGet("/traces", GetTraces);
        group.MapGet("/traces/{id:guid}", GetTraceById);
        group.MapDelete("/traces/{id:guid}", DeleteTrace);
        group.MapGet("/interactions", GetTraces);
        group.MapGet("/interactions/{id:guid}", GetTraceById);
        group.MapDelete("/interactions/{id:guid}", DeleteTrace);
        group.MapGet("/savings", GetSavings);

        return app;
    }

    // ──── 1. GET /api/dashboard/summary ────

    private static async Task<IResult> GetSummary(
        MetricsQueries queries,
        int? days,
        string? accountId,
        string? userId,
        string? property,
        string? propertyValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetSummaryAsync(
                days ?? 30, accountId, property, propertyValue, from, to);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get summary");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── 2. GET /api/dashboard/daily ────

    private static async Task<IResult> GetDaily(
        MetricsQueries queries,
        int? days,
        string? accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetDailyAsync(days ?? 30, accountId, from, to);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get daily metrics");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── 3. GET /api/dashboard/by-model ────

    private static async Task<IResult> GetByModel(
        MetricsQueries queries,
        int? days,
        string? accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetByModelAsync(days ?? 30, accountId, from, to);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get by-model metrics");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── 4. GET /api/dashboard/by-user ────

    private static async Task<IResult> GetByUser(
        MetricsQueries queries,
        int? days,
        int? limit,
        string? accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetByUserAsync(days ?? 30, limit ?? 20, accountId, from, to);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get by-user metrics");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── 5. GET /api/dashboard/traces ────

    private static async Task<IResult> GetTraces(
        MetricsQueries queries,
        int? limit,
        int? offset,
        string? accountId,
        string? userId,
        string? model,
        string? property,
        string? propertyValue,
        bool? onlyErrors,
        bool? onlyCompressed,
        string? provider,
        string? sessionId,
        string? q,
        ILogger<MetricsQueries> logger)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return Results.BadRequest(new { error = "accountId is required" });

        try
        {
            var effectiveLimit = Math.Min(limit ?? 50, 100);
            var result = await queries.GetTracesAsync(
                effectiveLimit, offset ?? 0,
                accountId, userId, model,
                property, propertyValue,
                onlyErrors ?? false, onlyCompressed ?? false,
                provider, sessionId, q);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get traces");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── 6. GET /api/dashboard/traces/{id} ────

    private static async Task<IResult> GetTraceById(
        Guid id,
        string? accountId,
        MetricsQueries queries,
        ILogger<MetricsQueries> logger)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return Results.BadRequest(new { error = "accountId is required" });

        try
        {
            var trace = await queries.GetTraceByIdAsync(id, accountId);
            return trace is not null
                ? Results.Ok(trace)
                : Results.NotFound(new { error = $"Interaction {id} not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get interaction {TraceId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> DeleteTrace(
        Guid id,
        string? accountId,
        MetricsQueries queries,
        ILogger<MetricsQueries> logger)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return Results.BadRequest(new { error = "accountId is required" });

        try
        {
            var deleted = await queries.DeleteTraceAsync(id, accountId);
            return deleted
                ? Results.Ok(new { deleted = true })
                : Results.NotFound(new { error = "Interaction not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete interaction {TraceId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetSavings(
        MetricsQueries queries,
        int? days,
        string? accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return Results.BadRequest(new { error = "accountId is required" });

        try
        {
            var result = await queries.GetSavingsAsync(days ?? 30, accountId, from, to);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get savings");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }
}
