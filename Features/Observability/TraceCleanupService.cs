using Dapper;
using Npgsql;

namespace TokenBee.Features.Observability;

public class TraceCleanupService(IServiceProvider services, ILogger<TraceCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

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

            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation("Trace cleanup background service is stopping.");
    }

    private async Task CleanupOldTracesAsync(CancellationToken stoppingToken)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("Default");
        if (string.IsNullOrEmpty(connString)) return;

        const string sql = """
            DELETE FROM traces
            WHERE expires_at IS NOT NULL AND expires_at < NOW()
               OR (expires_at IS NULL AND timestamp < NOW() - INTERVAL '30 days')
            """;

        await using var connection = new NpgsqlConnection(connString);
        var deletedRows = await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: stoppingToken));
        logger.LogInformation("Deleted {DeletedCount} expired interaction traces.", deletedRows);
    }
}
