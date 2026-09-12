using System.Text.Json;
using Dapper;
using Npgsql;
using TokenBee.Shared.Auth;

namespace TokenBee.Features.Replay;

public static class ReplayEndpoints
{
    private static readonly HashSet<string> ValidSpanTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LlmCall", "ToolCall", "Decision", "Custom"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static WebApplication MapReplayEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/replay");

        group.MapGet("/sessions",              GetSessions);
        group.MapGet("/sessions/search",       SearchSessions);
        group.MapGet("/sessions/{id}",         GetSessionById);
        group.MapGet("/sessions/{id}/timeline", GetTimeline);
        group.MapPost("/sessions",             CreateSession);
        group.MapMethods("/sessions/{id}/end", ["PATCH"], EndSession);
        group.MapPost("/spans",                CreateSpan);
        group.MapGet("/spans/{id}/payload",    GetSpanPayload);

        return app;
    }

    // ──── GET /api/replay/sessions ────

    private static async Task<IResult> GetSessions(
        HttpContext ctx,
        IConfiguration configuration,
        int? limit,
        int? offset,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            var accountId = ctx.RequireAccountId();
            var effectiveLimit = Math.Clamp(limit ?? 20, 1, 100);
            var effectiveOffset = Math.Max(offset ?? 0, 0);

            var sql = """
                SELECT s.id, s.name, s.agent_type AS AgentType,
                       s.started_at AS StartedAt, s.ended_at AS EndedAt,
                       COUNT(sp.id)                         AS SpanCount,
                       COALESCE(SUM(sp.tokens), 0)          AS TotalTokens,
                       COALESCE(SUM(sp.duration_ms), 0)     AS DurationMs,
                       MAX(sp.timestamp)                    AS LastActivity
                FROM sessions s
                LEFT JOIN spans sp ON sp.session_id = s.id
                WHERE EXISTS (
                    SELECT 1 FROM traces t
                    WHERE t.session_id = s.id AND t.account_id = @AccountId
                )
                GROUP BY s.id
                ORDER BY s.started_at DESC
                LIMIT @Limit OFFSET @Offset
                """;

            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));
            var rows = await connection.QueryAsync<SessionSummaryRow>(
                sql, new { Limit = effectiveLimit, Offset = effectiveOffset, AccountId = accountId });

            var result = rows.Select(r => new SessionSummaryDto
            {
                Id           = r.Id,
                Name         = r.Name,
                AgentType    = r.AgentType,
                StartedAt    = r.StartedAt,
                EndedAt      = r.EndedAt,
                SpanCount    = r.SpanCount,
                TotalTokens  = r.TotalTokens,
                DurationMs   = r.EndedAt.HasValue
                    ? (int)(r.EndedAt.Value - r.StartedAt).TotalMilliseconds
                    : r.DurationMs,
                LastActivity = r.LastActivity
            });

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sessions");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/replay/sessions/search ────

    private static async Task<IResult> SearchSessions(
        HttpContext ctx,
        IConfiguration configuration,
        string? q,
        int? limit,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.Ok(Array.Empty<SessionSummaryDto>());

            var accountId = ctx.RequireAccountId();
            var effectiveLimit = Math.Clamp(limit ?? 20, 1, 100);

            var sql = """
                SELECT s.id, s.name, s.agent_type AS AgentType,
                       s.started_at AS StartedAt, s.ended_at AS EndedAt,
                       COUNT(sp.id)                         AS SpanCount,
                       COALESCE(SUM(sp.tokens), 0)          AS TotalTokens,
                       COALESCE(SUM(sp.duration_ms), 0)     AS DurationMs,
                       MAX(sp.timestamp)                    AS LastActivity
                FROM sessions s
                LEFT JOIN spans sp ON sp.session_id = s.id
                WHERE EXISTS (
                    SELECT 1 FROM traces t
                    WHERE t.session_id = s.id AND t.account_id = @AccountId
                )
                  AND (s.id ILIKE '%' || @Q || '%'
                   OR s.name ILIKE '%' || @Q || '%'
                   OR s.agent_type ILIKE '%' || @Q || '%')
                GROUP BY s.id
                ORDER BY s.started_at DESC
                LIMIT @Limit
                """;

            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));
            var rows = await connection.QueryAsync<SessionSummaryRow>(
                sql, new { Q = q, Limit = effectiveLimit, AccountId = accountId });

            var result = rows.Select(r => new SessionSummaryDto
            {
                Id           = r.Id,
                Name         = r.Name,
                AgentType    = r.AgentType,
                StartedAt    = r.StartedAt,
                EndedAt      = r.EndedAt,
                SpanCount    = r.SpanCount,
                TotalTokens  = r.TotalTokens,
                DurationMs   = r.EndedAt.HasValue
                    ? (int)(r.EndedAt.Value - r.StartedAt).TotalMilliseconds
                    : r.DurationMs,
                LastActivity = r.LastActivity
            });

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search sessions");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/replay/sessions/{id} ────

    private static async Task<IResult> GetSessionById(
        HttpContext ctx,
        string id,
        IConfiguration configuration,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));

            if (!await SessionVisibleAsync(connection, id, ctx.RequireAccountId()))
                return Results.NotFound(new { error = $"Session {id} not found" });

            var sessionSql = """
                SELECT id, name, agent_type AS AgentType,
                       started_at AS StartedAt, ended_at AS EndedAt
                FROM sessions WHERE id = @Id
                """;

            var session = await connection.QueryFirstOrDefaultAsync<Session>(
                sessionSql, new { Id = id });

            if (session is null)
                return Results.NotFound(new { error = $"Session {id} not found" });

            var spansSql = """
                SELECT id, type, timestamp, duration_ms AS DurationMs,
                       tokens, metadata_json AS MetadataJson,
                       parent_span_id AS ParentSpanId,
                       (input_payload IS NOT NULL)  AS HasInput,
                       (output_payload IS NOT NULL) AS HasOutput
                FROM spans
                WHERE session_id = @Id
                ORDER BY timestamp ASC
                """;

            var spanRows = (await connection.QueryAsync<SpanRow>(spansSql, new { Id = id })).ToList();

            var totalTokens = spanRows.Sum(s => s.Tokens);
            var sumDurationMs = spanRows.Sum(s => s.DurationMs);

            var durationMs = session.EndedAt.HasValue
                ? (int)(session.EndedAt.Value - session.StartedAt).TotalMilliseconds
                : sumDurationMs;

            var replaySpans = spanRows.Select((row, index) => new ReplaySpanDto
            {
                Id           = row.Id,
                Step         = index + 1,
                Type         = row.Type,
                Timestamp    = row.Timestamp,
                OffsetMs     = Math.Max(0, (int)(row.Timestamp - session.StartedAt).TotalMilliseconds),
                DurationMs   = row.DurationMs,
                Tokens       = row.Tokens,
                HasInput     = row.HasInput,
                HasOutput    = row.HasOutput,
                ParentSpanId = row.ParentSpanId,
                Metadata     = ParseMetadata(row.MetadataJson)
            }).ToList();

            var detail = new SessionReplayDto
            {
                Id          = session.Id,
                Name        = session.Name,
                AgentType   = session.AgentType,
                StartedAt   = session.StartedAt,
                EndedAt     = session.EndedAt,
                DurationMs  = durationMs,
                TotalTokens = totalTokens,
                SpanCount   = spanRows.Count,
                Spans       = replaySpans
            };

            return Results.Ok(detail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get session {SessionId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/replay/sessions/{id}/timeline ────

    private static async Task<IResult> GetTimeline(
        HttpContext ctx,
        string id,
        IConfiguration configuration,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));

            var exists = await SessionVisibleAsync(connection, id, ctx.RequireAccountId());

            if (!exists)
                return Results.NotFound(new { error = $"Session {id} not found" });

            var sessionSql = """
                SELECT started_at AS StartedAt, ended_at AS EndedAt
                FROM sessions WHERE id = @Id
                """;
            var session = await connection.QueryFirstAsync<Session>(sessionSql, new { Id = id });

            var spansSql = """
                SELECT id, type, timestamp, duration_ms AS DurationMs
                FROM spans
                WHERE session_id = @Id
                ORDER BY timestamp ASC
                """;

            var spanRows = (await connection.QueryAsync<SpanRow>(spansSql, new { Id = id })).ToList();

            var sumDurationMs = spanRows.Sum(s => s.DurationMs);
            var sessionDurationMs = session.EndedAt.HasValue
                ? (int)(session.EndedAt.Value - session.StartedAt).TotalMilliseconds
                : sumDurationMs;

            var timelineSpans = spanRows.Select((row, index) =>
            {
                var offsetMs = Math.Max(0, (int)(row.Timestamp - session.StartedAt).TotalMilliseconds);

                return new TimelineSpanDto
                {
                    Id         = row.Id,
                    Step       = index + 1,
                    Type       = row.Type,
                    OffsetMs   = offsetMs,
                    DurationMs = row.DurationMs,
                    WidthPct   = sessionDurationMs > 0
                        ? Math.Round((double)row.DurationMs / sessionDurationMs * 100, 2)
                        : 0,
                    OffsetPct  = sessionDurationMs > 0
                        ? Math.Round((double)offsetMs / sessionDurationMs * 100, 2)
                        : 0
                };
            }).ToList();

            return Results.Ok(new TimelineDto
            {
                SessionDurationMs = sessionDurationMs,
                Spans = timelineSpans
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get timeline for session {SessionId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── GET /api/replay/spans/{id}/payload ────

    private static async Task<IResult> GetSpanPayload(
        HttpContext ctx,
        Guid id,
        IConfiguration configuration,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            var sql = """
                SELECT input_payload AS InputPayload,
                       output_payload AS OutputPayload
                FROM spans WHERE id = @Id
                """;

            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));

            var owned = await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS (
                    SELECT 1
                    FROM spans sp
                    JOIN traces t ON t.session_id = sp.session_id
                    WHERE sp.id = @Id AND t.account_id = @AccountId
                )
                """, new { Id = id, AccountId = ctx.RequireAccountId() });
            if (!owned)
                return Results.NotFound(new { error = $"Span {id} not found" });

            var span = await connection.QueryFirstOrDefaultAsync<Span>(sql, new { Id = id });

            if (span is null)
                return Results.NotFound(new { error = $"Span {id} not found" });

            // Parse input payload
            object? parsedInput = null;
            try
            {
                if (span.InputPayload is not null)
                {
                    using var inputDoc = JsonDocument.Parse(span.InputPayload);
                    var root = inputDoc.RootElement;

                    var inputObj = new Dictionary<string, object?>();

                    if (root.TryGetProperty("messages", out var messages))
                        inputObj["messages"] = JsonSerializer.Deserialize<object>(messages.GetRawText(), JsonOpts);

                    if (root.TryGetProperty("model", out var model))
                        inputObj["model"] = model.GetString();

                    parsedInput = inputObj.Count > 0 ? inputObj : span.InputPayload;
                }
            }
            catch
            {
                parsedInput = span.InputPayload;
            }

            // Parse output payload
            object? parsedOutput = null;
            try
            {
                if (span.OutputPayload is not null)
                {
                    using var outputDoc = JsonDocument.Parse(span.OutputPayload);
                    var root = outputDoc.RootElement;

                    var outputObj = new Dictionary<string, object?>();

                    // Extract choices[0].message.content
                    if (root.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var content))
                        {
                            outputObj["content"] = content.GetString();
                        }
                    }

                    if (root.TryGetProperty("model", out var outModel))
                        outputObj["model"] = outModel.GetString();

                    if (root.TryGetProperty("usage", out var usage))
                        outputObj["usage"] = JsonSerializer.Deserialize<object>(usage.GetRawText(), JsonOpts);

                    parsedOutput = outputObj.Count > 0 ? outputObj : span.OutputPayload;
                }
            }
            catch
            {
                parsedOutput = span.OutputPayload;
            }

            return Results.Ok(new
            {
                input  = parsedInput,
                output = parsedOutput,
                raw = new
                {
                    input  = span.InputPayload,
                    output = span.OutputPayload
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get span payload {SpanId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/replay/sessions (unchanged) ────

    private static async Task<IResult> CreateSession(
        CreateSessionRequest request,
        IConfiguration configuration,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });

            var sql = """
                INSERT INTO sessions (id, name, agent_type, started_at)
                VALUES (@SessionId, @Name, @AgentType, NOW())
                ON CONFLICT (id) DO UPDATE
                SET name = COALESCE(@Name, sessions.name),
                    agent_type = COALESCE(@AgentType, sessions.agent_type)
                """;

            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));
            await connection.ExecuteAsync(sql, new
            {
                request.SessionId,
                request.Name,
                request.AgentType
            });

            return Results.Ok(new { sessionId = request.SessionId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create session");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── PATCH /api/replay/sessions/{id}/end (unchanged) ────

    private static async Task<IResult> EndSession(
        HttpContext ctx,
        string id,
        IConfiguration configuration,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));
            if (!await SessionWritableAsync(connection, id, ctx.RequireAccountId()))
                return Results.NotFound(new { error = $"Session {id} not found" });

            var sql = """
                UPDATE sessions SET ended_at = NOW()
                WHERE id = @Id AND ended_at IS NULL
                """;

            var rows = await connection.ExecuteAsync(sql, new { Id = id });

            if (rows == 0)
            {
                var exists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS(SELECT 1 FROM sessions WHERE id = @Id)",
                    new { Id = id });

                if (!exists)
                    return Results.NotFound(new { error = $"Session {id} not found" });
            }

            return Results.Ok(new { ended = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to end session {SessionId}", id);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── POST /api/replay/spans (unchanged) ────

    private static async Task<IResult> CreateSpan(
        HttpContext ctx,
        CreateSpanRequest request,
        IConfiguration configuration,
        ISpanRecorder spanRecorder,
        ILogger<SpanRecorder> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });

            if (!ValidSpanTypes.Contains(request.Type))
                return Results.BadRequest(new { error = $"Invalid type '{request.Type}'. Must be one of: LlmCall, ToolCall, Decision, Custom" });

            await using var connection = new NpgsqlConnection(
                configuration.GetConnectionString("Default"));
            if (!await SessionWritableAsync(connection, request.SessionId, ctx.RequireAccountId()))
                return Results.NotFound(new { error = $"Session {request.SessionId} not found" });

            var metadataJson = request.Metadata is not null
                ? JsonSerializer.Serialize(request.Metadata)
                : null;

            var spanId = Guid.NewGuid();

            await spanRecorder.RecordCustomSpanAsync(
                spanId:        spanId,
                sessionId:     request.SessionId,
                type:          request.Type,
                inputPayload:  request.Input,
                outputPayload: request.Output,
                durationMs:    request.DurationMs,
                tokens:        request.Tokens,
                parentSpanId:  request.ParentSpanId,
                metadataJson:  metadataJson);

            return Results.Ok(new { spanId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create span");
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    // ──── Helpers ────

    private static Task<bool> SessionVisibleAsync(NpgsqlConnection connection, string sessionId, string accountId) =>
        connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM traces
                WHERE session_id = @SessionId AND account_id = @AccountId
            )
            """, new { SessionId = sessionId, AccountId = accountId });

    /// <summary>
    /// Allow writes to a session the account owns, or to a session that has no traces yet
    /// (just created from the dashboard). Never write into another account's session.
    /// </summary>
    private static async Task<bool> SessionWritableAsync(
        NpgsqlConnection connection, string sessionId, string accountId)
    {
        var owned = await SessionVisibleAsync(connection, sessionId, accountId);
        if (owned) return true;

        var hasAnyTraces = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS (SELECT 1 FROM traces WHERE session_id = @SessionId)
            """, new { SessionId = sessionId });
        return !hasAnyTraces;
    }

    private static object ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson))
            return new { };

        try
        {
            return JsonSerializer.Deserialize<object>(metadataJson, JsonOpts) ?? new { };
        }
        catch
        {
            return new { };
        }
    }
}
