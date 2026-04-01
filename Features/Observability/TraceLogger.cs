using Dapper;
using Npgsql;

namespace TokenScope.Features.Observability;

public interface ITraceLogger
{
    Task LogAsync(TraceRecord trace);
}

public class TraceLogger : ITraceLogger
{
    private readonly string _connectionString;
    private readonly ILogger<TraceLogger> _logger;

    private const string InsertSql = """
        INSERT INTO traces (
            id, timestamp, path, model, provider,
            input_tokens, output_tokens, original_tokens, compressed_tokens,
            input_cost_usd, output_cost_usd, total_cost_usd, saved_cost_usd,
            latency_ms, status_code, was_compressed, is_streaming,
            user_id, session_id, properties_json
        ) VALUES (
            @Id, @Timestamp, @Path, @Model, @Provider,
            @InputTokens, @OutputTokens, @OriginalTokens, @CompressedTokens,
            @InputCostUsd, @OutputCostUsd, @TotalCostUsd, @SavedCostUsd,
            @LatencyMs, @StatusCode, @WasCompressed, @IsStreaming,
            @UserId, @SessionId, @PropertiesJson
        );
        """;

    public TraceLogger(IConfiguration configuration, ILogger<TraceLogger> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");
        _logger = logger;
    }

    public async Task LogAsync(TraceRecord trace)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(InsertSql, trace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log trace {TraceId} for model {Model}",
                trace.Id, trace.Model);
        }
    }
}
