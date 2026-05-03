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
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetSummaryAsync(
                days ?? 30, accountId, property, propertyValue);
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
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetDailyAsync(days ?? 30, accountId);
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
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetByModelAsync(days ?? 30, accountId);
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
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var result = await queries.GetByUserAsync(days ?? 30, limit ?? 20, accountId);
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
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var effectiveLimit = Math.Min(limit ?? 50, 100);
            var result = await queries.GetTracesAsync(
                effectiveLimit, offset ?? 0,
                accountId, userId, model,
                property, propertyValue,
                onlyErrors ?? false, onlyCompressed ?? false);
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
        MetricsQueries queries,
        ILogger<MetricsQueries> logger)
    {
        try
        {
            var trace = await queries.GetTraceByIdAsync(id);
            return trace is not null
                ? Results.Ok(trace)
                : Results.NotFound(new { error = $"Trace {id} not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get trace {TraceId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }
}
