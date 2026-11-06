using Microsoft.Extensions.Logging;
using NaylorNocker.Core.Admission;
using NaylorNocker.Core.Networking;
using NaylorNocker.Service.Diagnostics;

namespace NaylorNocker.Service;

internal sealed class GrantMaintenanceWorker(TimeProvider clock, GrantCoordinator grants,
    UdpKnockReceiver receiver, ServiceDiagnostics diagnostics) : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(100), clock);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        long reportedExpirations = 0, reportedDrops = 0;
        var lastDropReport = clock.GetTimestamp();
        while (await _timer.WaitForNextTickAsync(cancellationToken))
        {
            await grants.ExpireDueAsync(cancellationToken);
            var expired = grants.TotalExpirations;
            if (expired != reportedExpirations)
            {
                diagnostics.Write(1101, LogLevel.Information, $"Closed {expired - reportedExpirations} admission window(s).");
                reportedExpirations = expired;
            }
            var drops = receiver.DroppedKnocks;
            if (drops != reportedDrops && clock.GetElapsedTime(lastDropReport) >= TimeSpan.FromSeconds(1))
            {
                diagnostics.Write(1201, LogLevel.Warning, $"Dropped {drops - reportedDrops} matched knock(s) because the queue was full.");
                reportedDrops = drops;
                lastDropReport = clock.GetTimestamp();
            }
            // V1 intentionally retains all source policy until service shutdown.
        }
    }

    public void Dispose() => _timer.Dispose();
}
