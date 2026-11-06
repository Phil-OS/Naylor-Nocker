using System.Net;
using System.Runtime.ExceptionServices;
using NaylorNocker.Core.Protocol;
namespace NaylorNocker.Core.Admission;
public sealed class GrantCoordinator : IAsyncDisposable
{
    private long _totalExpirations;
    public long TotalExpirations => Interlocked.Read(ref _totalExpirations);
    private readonly object _sync = new();
    private readonly Dictionary<IPAddress, Entry> _entries = [];
    private readonly IFirewallBackend _backend;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _shutdown = new();
    private volatile bool _stopping;
    private Task? _stopTask;

    public GrantCoordinator(IFirewallBackend backend, TimeProvider timeProvider) =>
        (_backend, _clock) = (backend ?? throw new ArgumentNullException(nameof(backend)),
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)));

    public async ValueTask<GrantResult> TryGrantAsync(IPAddress source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source = SourceAddress.Normalize(source);
        EnsureRunning();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        while (true)
        {
            Entry entry;
            lock (_sync)
            {
                EnsureRunning();
                if (!_entries.TryGetValue(source, out entry!))
                {
                    if (_entries.Count >= GrantPolicy.MaxSources) return GrantResult.CapacityRejected;
                    _entries.Add(source, entry = new(source));
                }
            }
            try { await entry.Gate.WaitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                if (entry.Gate.Wait(0))
                    try { if (entry.Lease is null) Retire(entry); }
                    finally { entry.Gate.Release(); }
                throw;
            }
            Task? failedShutdown = null;
            try
            {
                if (entry.Retired) continue;
                EnsureRunning();
                entry.Lease ??= await _backend.CreateLeaseAsync(source, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                EnsureRunning();
                var refresh = entry.AdmissionEnabled;
                // An already enabled lease needs only a local deadline refresh, not a native RPC.
                if (!refresh)
                {
                    var enable = Task.Run(async () => await entry.Lease.EnableAdmissionAsync(linked.Token));
                    await enable.WaitAsync(TimeSpan.FromSeconds(2), linked.Token);
                }
                linked.Token.ThrowIfCancellationRequested();
                EnsureRunning();
                entry.OpenedAt = _clock.GetTimestamp();
                entry.AdmissionEnabled = true;
                return refresh ? GrantResult.Refreshed : GrantResult.Opened;
            }
            catch { failedShutdown = BeginStop(); throw; }
            finally
            {
                entry.Gate.Release();
                if (failedShutdown is not null) await failedShutdown;
            }
        }
    }

    public async ValueTask ExpireDueAsync(CancellationToken cancellationToken)
    {
        EnsureRunning();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        Exception? failure = null, cleanupFailure = null, loopFailure = null;
        try
        {
            await Parallel.ForEachAsync(Snapshot(), new ParallelOptions
                { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken }, async (entry, _) =>
            {
                if (_stopping || !entry.Gate.Wait(0)) return;
                Task? failedShutdown = null;
                try
                {
                    if (entry.Retired || !entry.AdmissionEnabled ||
                        _clock.GetElapsedTime(entry.OpenedAt, _clock.GetTimestamp()) < GrantPolicy.Window) return;
                    var disable = Task.Run(async () => await entry.Lease!.DisableAdmissionAsync(linked.Token));
                    await disable.WaitAsync(TimeSpan.FromSeconds(2), linked.Token);
                    entry.AdmissionEnabled = false;
                    Interlocked.Increment(ref _totalExpirations);
                }
                catch (Exception error)
                {
                    Interlocked.CompareExchange(ref failure, error, null);
                    failedShutdown = BeginStop();
                }
                finally { entry.Gate.Release(); }
                if (failedShutdown is not null)
                    try { await failedShutdown; }
                    catch (Exception error) { Interlocked.CompareExchange(ref cleanupFailure, error, null); }
            });
        }
        catch (Exception error) { loopFailure = error; }
        // Never hide the original native failure behind shutdown-induced cancellation,
        // or a cleanup failure behind the original error.
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (loopFailure is not null) ExceptionDispatchInfo.Capture(loopFailure).Throw();
    }

    public async ValueTask ReclaimInactiveAsync(CancellationToken cancellationToken)
    {
        EnsureRunning();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        foreach (var entry in Snapshot())
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!entry.Gate.Wait(0)) continue;
            Task? failedShutdown = null;
            try
            {
                if (entry.Retired || entry.AdmissionEnabled) continue;
                if (entry.Lease is not null)
                {
                    if (!await entry.Lease.CanReleaseAsync(linked.Token)) continue;
                    await entry.Lease.DisposeAsync();
                    entry.Lease = null;
                }
                Retire(entry);
            }
            catch { failedShutdown = BeginStop(); throw; }
            finally
            {
                entry.Gate.Release();
                if (failedShutdown is not null) await failedShutdown;
            }
        }
    }

    private Entry[] Snapshot()
    {
        lock (_sync) { EnsureRunning(); return [.. _entries.Values]; }
    }

    // Caller holds this entry's gate. Waiters retry against the dictionary after retirement.
    private void Retire(Entry entry)
    {
        lock (_sync)
            if (_entries.TryGetValue(entry.Source, out var current) && ReferenceEquals(entry, current))
            {
                entry.Retired = true;
                _entries.Remove(entry.Source);
            }
    }

    private void EnsureRunning() => ObjectDisposedException.ThrowIf(_stopping, this);

    private Task BeginStop()
    {
        lock (_sync)
        {
            _stopping = true;
            return _stopTask ??= BoundShutdownAsync(Task.Run(StopCoreAsync));
        }
    }

    private static async Task BoundShutdownAsync(Task cleanup)
    {
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { throw new FirewallShutdownException(); }
    }

    private async Task StopCoreAsync()
    {
        List<Exception> failures = [];
        var cancellation = _shutdown.CancelAsync();
        // Engine/session rundown comes first; an unrelated stuck operation must not
        // keep an expired permit installed or stop the failure reaching the host.
        try { await _backend.DisposeAsync(); }
        catch (Exception error) { failures.Add(error); }
        Entry[] entries;
        lock (_sync) entries = [.. _entries.Values];
        foreach (var entry in entries)
        {
            if (!entry.Gate.Wait(0)) continue;
            try
            {
                if (entry.Lease is not null) await entry.Lease.DisposeAsync();
            }
            catch (Exception error) { failures.Add(error); }
            finally { entry.Retired = true; entry.Gate.Release(); }
        }
        try { await cancellation; }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            lock (_sync) _entries.Clear();
            _shutdown.Dispose();
        }
        if (failures.Count != 0) throw new AggregateException("Firewall shutdown failed.", failures);
    }

    public ValueTask DisposeAsync() => new(BeginStop());

    private sealed class Entry(IPAddress source)
    {
        public IPAddress Source { get; } = source;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IGrantLease? Lease { get; set; }
        public bool AdmissionEnabled { get; set; }
        public long OpenedAt { get; set; }
        public bool Retired { get; set; }
    }
}
