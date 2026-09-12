using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenBee.Shared.Auth;

public class SupabaseUserService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _anonKey;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_anonKey);

    public SupabaseUserService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = (configuration["Supabase:Url"]
                    ?? configuration["SUPABASE_URL"]
                    ?? "").TrimEnd('/');
        _anonKey = configuration["Supabase:AnonKey"]
                   ?? configuration["SUPABASE_ANON_KEY"]
                   ?? "";
    }

    public async Task<string?> GetUserIdAsync(string accessToken, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(accessToken))
            return null;

        if (_cache.TryGetValue(accessToken, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.UserId;

        try
        {
            var client = _httpClientFactory.CreateClient("supabase-auth");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/auth/v1/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("apikey", _anonKey);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("id", out var idProp))
                return null;

            var userId = idProp.GetString();
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            _cache[accessToken] = new CacheEntry(userId, DateTime.UtcNow.AddMinutes(2));
            return userId;
        }
        catch
        {
            return null;
        }
    }

    private sealed record CacheEntry(string UserId, DateTime ExpiresAt);
}
