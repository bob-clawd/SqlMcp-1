using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlMcp.Tools.Drivers;

namespace SqlMcp.Host;

internal sealed class DatabaseProbeService(IDatabaseDriver db, ILogger<DatabaseProbeService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await db.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("SqlMcp connected to {Dialect} successfully.", db.Dialect);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
