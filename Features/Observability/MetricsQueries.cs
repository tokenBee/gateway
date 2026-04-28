using Dapper;
using Npgsql;

namespace TokenBee.Features.Observability;

// ──────────────────────────────── DTOs ────────────────────────────────

public sealed record SummaryDto
{
    public int TotalRequests { get; init; }
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalOriginalTokens { get; init; }
    public decimal TotalCostUsd { get; init; }
    public decimal TotalSavedUsd { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P50LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public int ErrorRequests { get; init; }
    public int CompressedRequests { get; init; }
    public double CompressionRate { get; init; }
}

public sealed record DailyDto
{
    public DateOnly Date { get; init; }
    public int Requests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal TotalCostUsd { get; init; }
    public decimal SavedCostUsd { get; init; }
    public double AvgLatencyMs { get; init; }
    public int ErrorCount { get; init; }
}

public sealed record ModelDto
{
    public string Model { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int Requests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal TotalCostUsd { get; init; }
    public decimal SavedCostUsd { get; init; }
    public double AvgLatencyMs { get; init; }
    public int ErrorCount { get; init; }
}

public sealed record UserDto
{
    public string UserId { get; init; } = string.Empty;
    public int Requests { get; init; }
    public decimal TotalCostUsd { get; init; }
    public decimal SavedCostUsd { get; init; }
    public double AvgLatencyMs { get; init; }
    public int ErrorCount { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}

public sealed record TraceDto
{
    public Guid Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int OriginalTokens { get; init; }
    public int CompressedTokens { get; init; }
    public decimal InputCostUsd { get; init; }
    public decimal OutputCostUsd { get; init; }
    public decimal TotalCostUsd { get; init; }
    public decimal SavedCostUsd { get; init; }
    public int LatencyMs { get; init; }
    public int StatusCode { get; init; }
    public bool WasCompressed { get; init; }
    public bool IsStreaming { get; init; }
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public string? PropertiesJson { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
}

// ──────────────────────────────── Queries ────────────────────────────────

public class MetricsQueries
{
    private readonly string _connectionString;
    private readonly ILogger<MetricsQueries> _logger;

    public MetricsQueries(IConfiguration configuration, ILogger<MetricsQueries> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");
        _logger = logger;
    }

    // ──── 1. Summary ────

    public async Task<SummaryDto> GetSummaryAsync(int days, string? userId, string? property, string? propertyValue)
    {
        var filters = BuildFilters(days, userId, property, propertyValue);

        var sql = $"""
            SELECT
                COUNT(*)::int                                        AS TotalRequests,
                COALESCE(SUM(input_tokens), 0)                       AS TotalInputTokens,
                COALESCE(SUM(output_tokens), 0)                      AS TotalOutputTokens,
                COALESCE(SUM(original_tokens), 0)                    AS TotalOriginalTokens,
                COALESCE(SUM(total_cost_usd), 0)                     AS TotalCostUsd,
                COALESCE(SUM(saved_cost_usd), 0)                     AS TotalSavedUsd,
                COALESCE(AVG(latency_ms), 0)                         AS AvgLatencyMs,
                COALESCE(PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY latency_ms), 0) AS P50LatencyMs,
                COALESCE(PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY latency_ms), 0) AS P95LatencyMs,
                COALESCE(PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY latency_ms), 0) AS P99LatencyMs,
                COALESCE(SUM(CASE WHEN status_code >= 400 THEN 1 ELSE 0 END), 0)::int AS ErrorRequests,
                COALESCE(SUM(CASE WHEN was_compressed THEN 1 ELSE 0 END), 0)::int     AS CompressedRequests
            FROM traces
            {filters.WhereClause}
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QuerySingleAsync<SummaryDto>(sql, filters.Parameters);

        // Calculate compression rate in C#
        return row with
        {
            CompressionRate = row.TotalRequests > 0
                ? Math.Round((double)row.CompressedRequests / row.TotalRequests * 100, 2)
                : 0
        };
    }

    // ──── 2. Daily ────

    public async Task<IEnumerable<DailyDto>> GetDailyAsync(int days, string? userId)
    {
        var filters = BuildFilters(days, userId, null, null);

        var sql = $"""
            SELECT
                DATE(timestamp)                                      AS Date,
                COUNT(*)::int                                        AS Requests,
                COALESCE(SUM(input_tokens), 0)                       AS InputTokens,
                COALESCE(SUM(output_tokens), 0)                      AS OutputTokens,
                COALESCE(SUM(total_cost_usd), 0)                     AS TotalCostUsd,
                COALESCE(SUM(saved_cost_usd), 0)                     AS SavedCostUsd,
                COALESCE(AVG(latency_ms), 0)                         AS AvgLatencyMs,
                COALESCE(SUM(CASE WHEN status_code >= 400 THEN 1 ELSE 0 END), 0)::int AS ErrorCount
            FROM traces
            {filters.WhereClause}
            GROUP BY DATE(timestamp)
            ORDER BY DATE(timestamp) ASC
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<DailyDto>(sql, filters.Parameters);
    }

    // ──── 3. By Model ────

    public async Task<IEnumerable<ModelDto>> GetByModelAsync(int days, string? userId)
    {
        var filters = BuildFilters(days, userId, null, null);

        var sql = $"""
            SELECT
                model                                                AS Model,
                provider                                             AS Provider,
                COUNT(*)::int                                        AS Requests,
                COALESCE(SUM(input_tokens), 0)                       AS InputTokens,
                COALESCE(SUM(output_tokens), 0)                      AS OutputTokens,
                COALESCE(SUM(total_cost_usd), 0)                     AS TotalCostUsd,
                COALESCE(SUM(saved_cost_usd), 0)                     AS SavedCostUsd,
                COALESCE(AVG(latency_ms), 0)                         AS AvgLatencyMs,
                COALESCE(SUM(CASE WHEN status_code >= 400 THEN 1 ELSE 0 END), 0)::int AS ErrorCount
            FROM traces
            {filters.WhereClause}
            GROUP BY model, provider
            ORDER BY SUM(total_cost_usd) DESC
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<ModelDto>(sql, filters.Parameters);
    }

    // ──── 4. By User ────

    public async Task<IEnumerable<UserDto>> GetByUserAsync(int days, int limit)
    {
        var sql = """
            SELECT
                user_id                                              AS UserId,
                COUNT(*)::int                                        AS Requests,
                COALESCE(SUM(total_cost_usd), 0)                     AS TotalCostUsd,
                COALESCE(SUM(saved_cost_usd), 0)                     AS SavedCostUsd,
                COALESCE(AVG(latency_ms), 0)                         AS AvgLatencyMs,
                COALESCE(SUM(input_tokens + output_tokens), 0)       AS TotalTokens,
                COALESCE(SUM(CASE WHEN status_code >= 400 THEN 1 ELSE 0 END), 0)::int AS ErrorCount,
                MAX(timestamp)                                       AS LastSeenAt
            FROM traces
            WHERE timestamp >= NOW() - MAKE_INTERVAL(days => @Days)
              AND user_id IS NOT NULL
            GROUP BY user_id
            ORDER BY SUM(total_cost_usd) DESC
            LIMIT @Limit
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<UserDto>(sql, new { Days = days, Limit = limit });
    }

    // ──── 5. Traces (list) ────

    public async Task<IEnumerable<TraceDto>> GetTracesAsync(
        int limit, int offset, string? userId, string? model,
        string? property, string? propertyValue,
        bool onlyErrors, bool onlyCompressed)
    {
        var dp = new DynamicParameters();
        var clauses = new List<string>();

        if (!string.IsNullOrEmpty(userId))
        {
            clauses.Add("user_id = @UserId");
            dp.Add("UserId", userId);
        }

        if (!string.IsNullOrEmpty(model))
        {
            clauses.Add("model = @Model");
            dp.Add("Model", model);
        }

        if (!string.IsNullOrEmpty(property) && !string.IsNullOrEmpty(propertyValue))
        {
            clauses.Add("properties_json::jsonb ->> @Property = @PropertyValue");
            dp.Add("Property", property);
            dp.Add("PropertyValue", propertyValue);
        }

        if (onlyErrors)
            clauses.Add("status_code >= 400");

        if (onlyCompressed)
            clauses.Add("was_compressed = true");

        var where = clauses.Count > 0
            ? "WHERE " + string.Join(" AND ", clauses)
            : string.Empty;

        var sql = $"""
            SELECT
                id, timestamp, path, model, provider,
                input_tokens   AS InputTokens,
                output_tokens  AS OutputTokens,
                original_tokens AS OriginalTokens,
                compressed_tokens AS CompressedTokens,
                input_cost_usd AS InputCostUsd,
                output_cost_usd AS OutputCostUsd,
                total_cost_usd AS TotalCostUsd,
                saved_cost_usd AS SavedCostUsd,
                latency_ms     AS LatencyMs,
                status_code    AS StatusCode,
                was_compressed AS WasCompressed,
                is_streaming   AS IsStreaming,
                user_id        AS UserId,
                session_id     AS SessionId,
                properties_json AS PropertiesJson,
                request_body   AS RequestBody,
                response_body  AS ResponseBody
            FROM traces
            {where}
            ORDER BY timestamp DESC
            LIMIT @Limit OFFSET @Offset
            """;

        dp.Add("Limit", limit);
        dp.Add("Offset", offset);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryAsync<TraceDto>(sql, dp);
    }

    // ──── 6. Trace by Id ────

    public async Task<TraceDto?> GetTraceByIdAsync(Guid id)
    {
        const string sql = """
            SELECT
                id, timestamp, path, model, provider,
                input_tokens   AS InputTokens,
                output_tokens  AS OutputTokens,
                original_tokens AS OriginalTokens,
                compressed_tokens AS CompressedTokens,
                input_cost_usd AS InputCostUsd,
                output_cost_usd AS OutputCostUsd,
                total_cost_usd AS TotalCostUsd,
                saved_cost_usd AS SavedCostUsd,
                latency_ms     AS LatencyMs,
                status_code    AS StatusCode,
                was_compressed AS WasCompressed,
                is_streaming   AS IsStreaming,
                user_id        AS UserId,
                session_id     AS SessionId,
                properties_json AS PropertiesJson,
                request_body   AS RequestBody,
                response_body  AS ResponseBody
            FROM traces
            WHERE id = @Id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<TraceDto>(sql, new { Id = id });
    }

    // ──────────────────── Private helpers ────────────────────

    private static (string WhereClause, DynamicParameters Parameters) BuildFilters(
        int days, string? userId, string? property, string? propertyValue)
    {
        var dp = new DynamicParameters();
        var clauses = new List<string> { "timestamp >= NOW() - MAKE_INTERVAL(days => @Days)" };
        dp.Add("Days", days);

        if (!string.IsNullOrEmpty(userId))
        {
            clauses.Add("user_id = @UserId");
            dp.Add("UserId", userId);
        }

        if (!string.IsNullOrEmpty(property) && !string.IsNullOrEmpty(propertyValue))
        {
            clauses.Add("properties_json::jsonb ->> @Property = @PropertyValue");
            dp.Add("Property", property);
            dp.Add("PropertyValue", propertyValue);
        }

        return ("WHERE " + string.Join(" AND ", clauses), dp);
    }
}
