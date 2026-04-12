using System.Security.Cryptography;
using Dapper;
using Npgsql;

namespace TokenBee.Features.Auth;

// ─── Records ────────────────────────────────────────────────────

public record ApiKeyResult(string PlainKey, string Prefix, string Id);
public record ValidatedKey(string UserId, string KeyId);
public record ApiKeyDto(string Id, string Prefix, string Name, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

// ─── Interface ──────────────────────────────────────────────────

public interface IApiKeyService
{
    Task<ApiKeyResult> CreateAsync(string userId, string name);
    Task<ValidatedKey?> ValidateAsync(string plainKey);
    Task<IEnumerable<ApiKeyDto>> GetByUserAsync(string userId);
    Task<bool> RevokeAsync(string keyId, string userId);
}

// ─── Implementation ─────────────────────────────────────────────

public class ApiKeyService(IConfiguration config, ILogger<ApiKeyService> logger) : IApiKeyService
{
    private readonly string _conn = config.GetConnectionString("Default")!;

    public async Task<ApiKeyResult> CreateAsync(string userId, string name)
    {
        try
        {
            // tb_live_ + 32 hex chars (16 bytes = exactly 32 hex chars, no trimming needed)
            var randomBytes = RandomNumberGenerator.GetBytes(16);
            var randomPart = Convert.ToHexString(randomBytes).ToLowerInvariant();
            var plainKey = $"tb_live_{randomPart}";   // e.g. tb_live_3f2a1b...

            // First 16 chars of full key used as a non-secret lookup prefix
            var prefix = plainKey[..16];               // "tb_live_3f2a1b.."

            // BCrypt with explicit work factor — tune to ~200-300ms on your hardware
            var hash = BCrypt.Net.BCrypt.HashPassword(plainKey, workFactor: 12);

            var id = Guid.NewGuid();

            const string sql = """
                INSERT INTO api_keys (id, user_id, key_hash, key_prefix, name, is_active, created_at)
                VALUES (@Id, @UserId, @KeyHash, @KeyPrefix, @Name, TRUE, NOW())
                """;

            await using var connection = new NpgsqlConnection(_conn);
            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                UserId = userId,
                KeyHash = hash,
                KeyPrefix = prefix,
                Name = name
            });

            return new ApiKeyResult(plainKey, prefix, id.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create API key for user {UserId}", userId);
            throw;
        }
    }

    public async Task<ValidatedKey?> ValidateAsync(string plainKey)
    {
        try
        {
            var prefix = plainKey[..16];

            const string sql = """
                SELECT id, user_id AS UserId, key_hash AS KeyHash
                FROM api_keys
                WHERE key_prefix = @Prefix AND is_active = TRUE
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var candidates = await connection.QueryAsync<KeyCandidate>(sql, new { Prefix = prefix });

            foreach (var candidate in candidates)
            {
                if (BCrypt.Net.BCrypt.Verify(plainKey, candidate.KeyHash))
                {
                    // Fire-and-forget: update last_used_at
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var conn = new NpgsqlConnection(_conn);
                            await conn.ExecuteAsync(
                                "UPDATE api_keys SET last_used_at = NOW() WHERE id = @Id",
                                new { candidate.Id });
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to update last_used_at for key {KeyId}", candidate.Id);
                        }
                    });

                    return new ValidatedKey(candidate.UserId, candidate.Id.ToString());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to validate API key");
            return null;
        }
    }

    public async Task<IEnumerable<ApiKeyDto>> GetByUserAsync(string userId)
    {
        try
        {
            const string sql = """
                SELECT id, key_prefix AS Prefix, name, is_active AS IsActive,
                       created_at AS CreatedAt, last_used_at AS LastUsedAt
                FROM api_keys
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                """;

            await using var connection = new NpgsqlConnection(_conn);
            return await connection.QueryAsync<ApiKeyDto>(sql, new { UserId = userId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get API keys for user {UserId}", userId);
            return Enumerable.Empty<ApiKeyDto>();
        }
    }

    public async Task<bool> RevokeAsync(string keyId, string userId)
    {
        try
        {
            const string sql = """
                UPDATE api_keys SET is_active = FALSE
                WHERE id = @KeyId AND user_id = @UserId
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var rows = await connection.ExecuteAsync(sql, new { KeyId = Guid.Parse(keyId), UserId = userId });
            return rows > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to revoke API key {KeyId}", keyId);
            return false;
        }
    }

    // Internal DTO for candidate lookup
    private record KeyCandidate(Guid Id, string UserId, string KeyHash);
}
