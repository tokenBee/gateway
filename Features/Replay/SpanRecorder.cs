using System.Text.Json;
using Dapper;
using Npgsql;

namespace TokenBee.Features.Replay;

public interface ISpanRecorder
{
    Task RecordLlmCallAsync(
        string sessionId,
        string inputPayload,
        string outputPayload,
        int    durationMs,
        int    tokens,
        string model,
        bool   wasCompressed,
        int    savedTokens);

    Task<Guid> RecordCustomSpanAsync(
        Guid    spanId,
        string  sessionId,
        string  type,
        string? inputPayload,
        string? outputPayload,
        int     durationMs,
        int     tokens,
        string? parentSpanId,
        string? metadataJson);
}

public class SpanRecorder : ISpanRecorder
{
    private readonly string _connectionString;
    private readonly ILogger<SpanRecorder> _logger;

    private const int MaxPayloadLength = 10_000;

    private const string UpsertSessionSql = """
        INSERT INTO sessions (id, started_at)
        VALUES (@SessionId, NOW())
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string InsertSpanSql = """
        INSERT INTO spans (id, session_id, type, timestamp, duration_ms,
                           input_payload, output_payload, tokens,
                           metadata_json, parent_span_id)
        VALUES (@Id, @SessionId, @Type, NOW(), @DurationMs,
                @InputPayload, @OutputPayload, @Tokens,
                @MetadataJson, @ParentSpanId);
        """;

    public SpanRecorder(IConfiguration configuration, ILogger<SpanRecorder> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");
        _logger = logger;
    }

    public async Task RecordLlmCallAsync(
        string sessionId,
        string inputPayload,
        string outputPayload,
        int    durationMs,
        int    tokens,
        string model,
        bool   wasCompressed,
        int    savedTokens)
    {
        try
        {
            var metadataJson = JsonSerializer.Serialize(new
            {
                model,
                wasCompressed,
                savedTokens
            });

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(UpsertSessionSql, new { SessionId = sessionId });
            await connection.ExecuteAsync(InsertSpanSql, new
            {
                Id            = Guid.NewGuid(),
                SessionId     = sessionId,
                Type          = "LlmCall",
                DurationMs    = durationMs,
                InputPayload  = Truncate(inputPayload),
                OutputPayload = Truncate(outputPayload),
                Tokens        = tokens,
                MetadataJson  = metadataJson,
                ParentSpanId  = (string?)null
            });

            _logger.LogDebug("Recorded LlmCall span for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record LlmCall span for session {SessionId}", sessionId);
        }
    }

    public async Task<Guid> RecordCustomSpanAsync(
        Guid    spanId,
        string  sessionId,
        string  type,
        string? inputPayload,
        string? outputPayload,
        int     durationMs,
        int     tokens,
        string? parentSpanId,
        string? metadataJson)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(UpsertSessionSql, new { SessionId = sessionId });
            await connection.ExecuteAsync(InsertSpanSql, new
            {
                Id            = spanId,
                SessionId     = sessionId,
                Type          = type,
                DurationMs    = durationMs,
                InputPayload  = Truncate(inputPayload),
                OutputPayload = Truncate(outputPayload),
                Tokens        = tokens,
                MetadataJson  = metadataJson,
                ParentSpanId  = parentSpanId
            });

            _logger.LogDebug("Recorded {Type} span for session {SessionId}", type, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record {Type} span for session {SessionId}", type, sessionId);
        }

        return spanId;
    }

    private static string? Truncate(string? value) =>
        value is not null && value.Length > MaxPayloadLength
            ? value[..MaxPayloadLength]
            : value;
}
