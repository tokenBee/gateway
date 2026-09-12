using TokenBee.Shared.Auth;

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

    private static async Task<IResult> GetSummary(
        HttpContext ctx,
        MetricsQueries queries,
        int? days,
        string? userId,
        string? property,
        string? propertyValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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

    private static async Task<IResult> GetDaily(
        HttpContext ctx,
        MetricsQueries queries,
        int? days,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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

    private static async Task<IResult> GetByModel(
        HttpContext ctx,
        MetricsQueries queries,
        int? days,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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

    private static async Task<IResult> GetByUser(
        HttpContext ctx,
        MetricsQueries queries,
        int? days,
        int? limit,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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

    private static async Task<IResult> GetTraces(
        HttpContext ctx,
        MetricsQueries queries,
        int? limit,
        int? offset,
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
        var accountId = ctx.RequireAccountId();
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

    private static async Task<IResult> GetTraceById(
        HttpContext ctx,
        Guid id,
        MetricsQueries queries,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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
        HttpContext ctx,
        Guid id,
        MetricsQueries queries,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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
        HttpContext ctx,
        MetricsQueries queries,
        int? days,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ILogger<MetricsQueries> logger)
    {
        var accountId = ctx.RequireAccountId();
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
