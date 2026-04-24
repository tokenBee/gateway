using Dapper;
using Npgsql;

namespace TokenBee.Features.Observability;

public class TraceCleanupService(IServiceProvider services, ILogger<TraceCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);
    private readonly int _retentionDays = 30; // Delete traces older than 30 days

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Trace cleanup background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldTracesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during trace cleanup.");
            }

            // Wait for next scheduled check (24h)
            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation("Trace cleanup background service is stopping.");
    }

    private async Task CleanupOldTracesAsync(CancellationToken stoppingToken)
    {
        // Using a new scope to resolve scoped services (like IConfiguration) inside a singleton background service
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("Default");

        if (string.IsNullOrEmpty(connString)) return;

        var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);

        const string sql = """
            DELETE FROM traces
            WHERE timestamp < @CutoffDate
            """;

        await using var connection = new NpgsqlConnection(connString);
        
        logger.LogInformation("Running cleanup for traces older than {CutoffDate}", cutoffDate);
        var deletedRows = await connection.ExecuteAsync(sql, new { CutoffDate = cutoffDate });
        logger.LogInformation("Successfully deleted {DeletedCount} old traces.", deletedRows);
    }
}
