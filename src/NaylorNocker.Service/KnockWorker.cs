using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Channels;
using NaylorNocker.Core.Admission;
using NaylorNocker.Core.Configuration;
using NaylorNocker.Core.Networking;
using NaylorNocker.Core.Protocol;
using NaylorNocker.Service.Diagnostics;
namespace NaylorNocker.Service;
public sealed class KnockWorker : IHostedService, IAsyncDisposable
{
    private readonly object _shutdownSync = new();
    private readonly GrantCoordinator _grants;
    private readonly ServiceDiagnostics _diagnostics;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Action<int> _terminateProcess;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<IPAddress> _matches = Channel.CreateBounded<IPAddress>(256);
    private readonly UdpKnockReceiver _receiver;
    private readonly GrantMaintenanceWorker _maintenance;
    private Task[] _loops = [];
    private Task? _shutdown;
    private int _exitCode;

    public KnockWorker(ValidatedKnockOptions options, IFirewallBackend backend, TimeProvider clock,
        ServiceDiagnostics diagnostics, IHostApplicationLifetime lifetime, Action<int> terminateProcess)
    {
        _clock = clock;
        _grants = new GrantCoordinator(backend, clock);
        _diagnostics = diagnostics;
        _lifetime = lifetime;
        _terminateProcess = terminateProcess;
        _receiver = new UdpKnockReceiver(options, new KnockMatcher(options.ExpectedMessageBytes), _matches.Writer);
        _maintenance = new GrantMaintenanceWorker(clock, _grants, _receiver, diagnostics);
    }

    public int ExitCode => Volatile.Read(ref _exitCode);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var receiving = SuperviseAsync("receiver", () => _receiver.RunAsync(_stop.Token));
            _loops = [receiving];
            await _receiver.Ready.WaitAsync(cancellationToken);
            _loops = [receiving, SuperviseAsync("grants", ConsumeKnocksAsync),
                SuperviseAsync("expiry", () => _maintenance.RunAsync(_stop.Token))];
            _diagnostics.Write(1000, LogLevel.Information, "naylor nocker is listening.");
        }
        catch (Exception error)
        {
            Fail("startup", error);
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task SuperviseAsync(string stage, Func<Task> work)
    {
        try
        {
            await work();
            if (!_stop.IsCancellationRequested) Fail(stage, new InvalidOperationException());
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { Fail(stage, error); }
    }

    private async Task ConsumeKnocksAsync()
    {
        long? lastCapacityLog = null;
        await foreach (var source in _matches.Reader.ReadAllAsync(_stop.Token))
        {
            var result = await _grants.TryGrantAsync(source, _stop.Token);
            if (result == GrantResult.CapacityRejected)
            {
                if (lastCapacityLog is null || _clock.GetElapsedTime(lastCapacityLog.Value) >= TimeSpan.FromSeconds(1))
                {
                    _diagnostics.Write(1200, LogLevel.Warning, "Source capacity reached; new source IPs require service restart.");
                    lastCapacityLog = _clock.GetTimestamp();
                }
            }
            else _diagnostics.Write(1100, result == GrantResult.Opened ? LogLevel.Information : LogLevel.Debug,
                $"Admission {result.ToString().ToLowerInvariant()} for {source} for ten seconds.");
        }
    }

    private void Fail(string stage, Exception error)
    {
        Interlocked.Exchange(ref _exitCode, 1);
        _diagnostics.Fault(stage, error);
        _stop.Cancel();
        _lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_shutdownSync) return _shutdown ??= Task.Run(StopCoreAsync);
    }

    private async Task StopCoreAsync()
    {
        _stop.Cancel();
        try
        {
            // Policy rundown precedes waiting for potentially stuck receive/grant work.
            await _grants.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(2500));
            await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _exitCode, 1);
            _diagnostics.Fault("shutdown", error);
            _terminateProcess(1); // Dynamic-session process rundown is the final cleanup boundary.
            throw; // A test terminator may return; production Environment.Exit never does.
        }
        finally { _maintenance.Dispose(); }
        _diagnostics.Write(1001, LogLevel.Information, "naylor nocker stopped; owned policy removed.");
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
}
