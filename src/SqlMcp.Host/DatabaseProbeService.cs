using Microsoft.Extensions.Hosting;
using SqlMcp.Tools.Drivers;

namespace SqlMcp.Host;

internal sealed class DatabaseProbeService(IDatabaseDriver db) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await db.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
