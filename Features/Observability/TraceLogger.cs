using System.Threading.Channels;
using Dapper;
using Npgsql;

namespace TokenBee.Features.Observability;

public interface ITraceLogger
{
    Task LogAsync(TraceRecord trace);
}

public class TraceLogger : BackgroundService, ITraceLogger
{
    private readonly string _connectionString;
    private readonly ILogger<TraceLogger> _logger;
    private readonly Channel<TraceRecord> _channel;

    private const string InsertSql = """
        INSERT INTO traces (
            id, timestamp, path, model, provider,
            input_tokens, output_tokens, original_tokens, compressed_tokens,
            input_cost_usd, output_cost_usd, total_cost_usd, saved_cost_usd,
            latency_ms, status_code, was_compressed, is_streaming,
            user_id, session_id, properties_json,
            request_body, response_body, compression_metadata_json
        ) VALUES (
            @Id, @Timestamp, @Path, @Model, @Provider,
            @InputTokens, @OutputTokens, @OriginalTokens, @CompressedTokens,
            @InputCostUsd, @OutputCostUsd, @TotalCostUsd, @SavedCostUsd,
            @LatencyMs, @StatusCode, @WasCompressed, @IsStreaming,
            @UserId, @SessionId, CAST(@PropertiesJson AS jsonb),
            @RequestBody, @ResponseBody, @CompressionMetadataJson
        );
        """;

    public TraceLogger(IConfiguration configuration, ILogger<TraceLogger> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");
        _logger = logger;
        _channel = Channel.CreateUnbounded<TraceRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task LogAsync(TraceRecord trace)
    {
        if (!_channel.Writer.TryWrite(trace))
            _logger.LogWarning("Failed to enqueue trace {TraceId} for model {Model}", trace.Id, trace.Model);
        
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Async Trace Writer started.");
        
        await foreach (var trace in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.ExecuteAsync(InsertSql, trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log trace {TraceId} for model {Model} to DB",
                    trace.Id, trace.Model);
            }
        }
    }
}
