using System.Text.Json;

namespace TokenBee.Features.Observability;

public class TraceRecord
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int OriginalTokens { get; set; }
    public int CompressedTokens { get; set; }
    public decimal InputCostUsd { get; set; }
    public decimal OutputCostUsd { get; set; }
    public decimal TotalCostUsd { get; set; }
    public decimal SavedCostUsd { get; set; }
    public int LatencyMs { get; set; }
    public int StatusCode { get; set; }
    public bool WasCompressed { get; set; }
    public bool IsStreaming { get; set; }
    public string? UserId { get; set; }
    public string? AccountId { get; set; }
    public string? SessionId { get; set; }
    public string? PropertiesJson { get; set; }
    public string? RequestBody { get; set; }
    public string? OriginalRequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? CompressionMetadataJson { get; set; }

    public void SetProperties(Dictionary<string, string> properties)
    {
        PropertiesJson = properties.Count > 0
            ? JsonSerializer.Serialize(properties)
            : null;
    }
}
