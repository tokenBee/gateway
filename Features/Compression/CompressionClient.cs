using System.Text.Json.Serialization;
using System.Text.Json;

namespace TokenBee.Features.Compression;

public class CompressionOptions
{
    public string SidecarUrl { get; set; } = string.Empty;
    public int ThresholdTokens { get; set; } = 1000;
    public float DefaultRate { get; set; } = 0.5f;
    public int TimeoutMs { get; set; } = 100;
}

public record CompressionResult(
    string CompressedBody,
    int OriginalTokens,
    int CompressedTokens,
    int SavedTokens,
    double CompressionRate,
    bool WasCompressed,
    string ModeUsed = "agnostic",
    string? QueryUsed = null,
    bool AutoQuery = false
);

public interface ICompressionClient
{
    Task<CompressionResult> CompressAsync(string requestBody, float rate = 0.5f, string mode = "auto", CancellationToken ct = default);
}

public class CompressionClient : ICompressionClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompressionClient> _logger;
    private readonly CompressionOptions _options;

    public CompressionClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CompressionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _options = configuration.GetSection("Compression").Get<CompressionOptions>() ?? new CompressionOptions();
    }
    
    private static string? ExtractLastUserMessage(string requestBody)
    {
        try
        {
            var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("messages", out var messages))
                return null;

            var msgArray = messages.EnumerateArray().ToList();
            for (int i = msgArray.Count - 1; i >= 0; i--)
            {
                var msg = msgArray[i];
                if (msg.TryGetProperty("role", out var role)
                    && role.GetString() == "user"
                    && msg.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                        return text.Length > 500 ? text[..500] : text;
                }
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<CompressionResult> CompressAsync(string requestBody, float rate = 0.5f, string mode = "auto", CancellationToken ct = default)
    {
        int estimatedTokens = requestBody.Length / 4;

        if (estimatedTokens < _options.ThresholdTokens)
        {
            return new CompressionResult(requestBody, estimatedTokens, estimatedTokens, 0, 1.0, false);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(_options.TimeoutMs));

            using var client = _httpClientFactory.CreateClient("compressor");
            client.BaseAddress = new Uri(_options.SidecarUrl);

            var query = ExtractLastUserMessage(requestBody);
            
            var reqBody = new { 
                prompt = requestBody, 
                rate = rate,
                query = query,
                mode = mode,
                coarse = false
            };
            
            var response = await client.PostAsJsonAsync("/compress", reqBody, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CompressionResponse>(cancellationToken: cts.Token);
                if (result != null)
                {
                    return new CompressionResult(
                        result.Compressed, 
                        result.OriginalTokens, 
                        result.CompressedTokens, 
                        result.SavedTokens, 
                        result.CompressionRate, 
                        true,
                        result.ModeUsed ?? "agnostic",
                        result.QueryUsed,
                        result.AutoQuery ?? false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Compression sidecar timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compression sidecar request failed.");
        }

        // Fallback
        return new CompressionResult(requestBody, estimatedTokens, estimatedTokens, 0, 1.0, false);
    }

    private record CompressionResponse(
        [property: JsonPropertyName("compressed")] string Compressed,
        [property: JsonPropertyName("original_tokens")] int OriginalTokens,
        [property: JsonPropertyName("compressed_tokens")] int CompressedTokens,
        [property: JsonPropertyName("saved_tokens")] int SavedTokens,
        [property: JsonPropertyName("compression_rate")] double CompressionRate,
        [property: JsonPropertyName("mode_used")] string? ModeUsed,
        [property: JsonPropertyName("query_used")] string? QueryUsed,
        [property: JsonPropertyName("auto_query")] bool? AutoQuery
    );
}
