namespace TokenBee.Features.Replay;

public class Session
{
    public string          Id        { get; set; } = string.Empty;
    public string?         Name      { get; set; }
    public string?         AgentType { get; set; }
    public DateTimeOffset  StartedAt { get; set; }
    public DateTimeOffset? EndedAt   { get; set; }
}

public class Span
{
    public Guid            Id            { get; set; }
    public string          SessionId     { get; set; } = string.Empty;
    public string          Type          { get; set; } = "LlmCall";
    public DateTimeOffset  Timestamp     { get; set; }
    public int             DurationMs    { get; set; }
    public string?         InputPayload  { get; set; }
    public string?         OutputPayload { get; set; }
    public int             Tokens        { get; set; }
    public string?         MetadataJson  { get; set; }
    public string?         ParentSpanId  { get; set; }
}

// ──── DTOs ────

public class SessionSummaryDto
{
    public string          Id           { get; set; } = string.Empty;
    public string?         Name         { get; set; }
    public string?         AgentType    { get; set; }
    public DateTimeOffset  StartedAt    { get; set; }
    public DateTimeOffset? EndedAt      { get; set; }
    public int             SpanCount    { get; set; }
    public int             TotalTokens  { get; set; }
    public int             DurationMs   { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
}

// Internal row for Dapper mapping (sessions list)
internal class SessionSummaryRow
{
    public string          Id           { get; set; } = string.Empty;
    public string?         Name         { get; set; }
    public string?         AgentType    { get; set; }
    public DateTimeOffset  StartedAt    { get; set; }
    public DateTimeOffset? EndedAt      { get; set; }
    public int             SpanCount    { get; set; }
    public int             TotalTokens  { get; set; }
    public int             DurationMs   { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
}

// Internal row for Dapper mapping (span in session detail)
internal class SpanRow
{
    public Guid            Id           { get; set; }
    public string          Type         { get; set; } = string.Empty;
    public DateTimeOffset  Timestamp    { get; set; }
    public int             DurationMs   { get; set; }
    public int             Tokens       { get; set; }
    public string?         MetadataJson { get; set; }
    public string?         ParentSpanId { get; set; }
    public bool            HasInput     { get; set; }
    public bool            HasOutput    { get; set; }
}

// Replay viewer span DTO (parsed metadata, step, offsetMs)
public class ReplaySpanDto
{
    public Guid            Id           { get; set; }
    public int             Step         { get; set; }
    public string          Type         { get; set; } = string.Empty;
    public DateTimeOffset  Timestamp    { get; set; }
    public int             OffsetMs     { get; set; }
    public int             DurationMs   { get; set; }
    public int             Tokens       { get; set; }
    public bool            HasInput     { get; set; }
    public bool            HasOutput    { get; set; }
    public string?         ParentSpanId { get; set; }
    public object          Metadata     { get; set; } = new { };
}

// Session detail for replay viewer
public class SessionReplayDto
{
    public string              Id          { get; set; } = string.Empty;
    public string?             Name        { get; set; }
    public string?             AgentType   { get; set; }
    public DateTimeOffset      StartedAt   { get; set; }
    public DateTimeOffset?     EndedAt     { get; set; }
    public int                 DurationMs  { get; set; }
    public int                 TotalTokens { get; set; }
    public int                 SpanCount   { get; set; }
    public List<ReplaySpanDto> Spans       { get; set; } = new();
}

// Timeline DTO
public class TimelineSpanDto
{
    public Guid   Id         { get; set; }
    public int    Step       { get; set; }
    public string Type       { get; set; } = string.Empty;
    public int    OffsetMs   { get; set; }
    public int    DurationMs { get; set; }
    public double WidthPct   { get; set; }
    public double OffsetPct  { get; set; }
}

public class TimelineDto
{
    public int                  SessionDurationMs { get; set; }
    public List<TimelineSpanDto> Spans            { get; set; } = new();
}

// ──── Request bodies ────

public class CreateSessionRequest
{
    public string  SessionId { get; set; } = string.Empty;
    public string? Name      { get; set; }
    public string? AgentType { get; set; }
}

public class CreateSpanRequest
{
    public string  SessionId    { get; set; } = string.Empty;
    public string  Type         { get; set; } = string.Empty;
    public int     DurationMs   { get; set; }
    public string? Input        { get; set; }
    public string? Output       { get; set; }
    public int     Tokens       { get; set; }
    public string? ParentSpanId { get; set; }
    public object? Metadata     { get; set; }
}
