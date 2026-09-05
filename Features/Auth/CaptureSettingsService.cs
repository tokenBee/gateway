using Dapper;
using Npgsql;
using TokenBee.Features.Observability;

namespace TokenBee.Features.Auth;

public record CaptureSettings(
    string UserId,
    bool CaptureEnabled,
    int RetentionDays,
    bool CaptureMessages);

public interface ICaptureSettingsService
{
    Task<CaptureSettings> GetOrCreateAsync(string userId, string planStatus);
    Task<CaptureSettings> UpdateAsync(string userId, bool? captureEnabled, int? retentionDays, bool? captureMessages, string planStatus);
    int MaxRetentionDays(string planStatus);
}

public class CaptureSettingsService(IConfiguration config, ILogger<CaptureSettingsService> logger)
    : ICaptureSettingsService
{
    private readonly string _conn = config.GetConnectionString("Default")!;

    public int MaxRetentionDays(string planStatus) => CaptureDecision.MaxRetentionDays(planStatus);

    public async Task<CaptureSettings> GetOrCreateAsync(string userId, string planStatus)
    {
        var maxDays = MaxRetentionDays(planStatus);
        try
        {
            const string sql = """
                SELECT user_id AS UserId,
                       capture_enabled AS CaptureEnabled,
                       retention_days AS RetentionDays,
                       capture_messages AS CaptureMessages
                FROM capture_settings
                WHERE user_id = @UserId
                """;

            await using var connection = new NpgsqlConnection(_conn);
            var row = await connection.QueryFirstOrDefaultAsync<CaptureSettings>(sql, new { UserId = userId });
            if (row is not null)
            {
                var days = Math.Min(row.RetentionDays, maxDays);
                return row with { RetentionDays = days };
            }

            const string insert = """
                INSERT INTO capture_settings (user_id, capture_enabled, retention_days, capture_messages)
                VALUES (@UserId, TRUE, @RetentionDays, TRUE)
                ON CONFLICT (user_id) DO NOTHING
                """;
            await connection.ExecuteAsync(insert, new { UserId = userId, RetentionDays = maxDays });
            return new CaptureSettings(userId, true, maxDays, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load capture settings for {UserId}", userId);
            return new CaptureSettings(userId, true, maxDays, true);
        }
    }

    public async Task<CaptureSettings> UpdateAsync(
        string userId, bool? captureEnabled, int? retentionDays, bool? captureMessages, string planStatus)
    {
        var current = await GetOrCreateAsync(userId, planStatus);
        var maxDays = MaxRetentionDays(planStatus);
        var nextEnabled = captureEnabled ?? current.CaptureEnabled;
        var nextDays = Math.Clamp(retentionDays ?? current.RetentionDays, 1, maxDays);
        var nextMessages = captureMessages ?? current.CaptureMessages;

        const string sql = """
            INSERT INTO capture_settings (user_id, capture_enabled, retention_days, capture_messages, updated_at)
            VALUES (@UserId, @CaptureEnabled, @RetentionDays, @CaptureMessages, NOW())
            ON CONFLICT (user_id) DO UPDATE SET
                capture_enabled = EXCLUDED.capture_enabled,
                retention_days = EXCLUDED.retention_days,
                capture_messages = EXCLUDED.capture_messages,
                updated_at = NOW()
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_conn);
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                CaptureEnabled = nextEnabled,
                RetentionDays = nextDays,
                CaptureMessages = nextMessages
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update capture settings for {UserId}", userId);
        }

        return new CaptureSettings(userId, nextEnabled, nextDays, nextMessages);
    }
}
