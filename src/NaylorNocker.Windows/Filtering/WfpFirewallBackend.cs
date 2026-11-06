using System.Net;
using System.Runtime.Versioning;
using NaylorNocker.Core.Admission;
using NaylorNocker.Core.Protocol;

namespace NaylorNocker.Windows.Filtering;

public sealed class WfpFirewallBackend : IFirewallBackend
{
    private readonly object _sync = new();
    private readonly IPAddress _bindAddress;
    private readonly Func<IPAddress, IPAddress?, ushort, IGrantPolicySession> _factory;
    private readonly HashSet<Task<IGrantPolicySession>> _pending = [];
    private readonly List<WfpGrantLease> _leases = [];
    private volatile bool _stopping;
    private Task? _shutdown;

    [SupportedOSPlatform("windows")]
    public WfpFirewallBackend(IPAddress bindAddress) : this(bindAddress, OwnedWfpGrantPolicy.Create)
    {
        // Fail startup before binding UDP if BFE or the required privileges are unavailable.
        using var check = WfpPolicySession.Open();
    }

    internal WfpFirewallBackend(IPAddress bindAddress, Func<IPAddress, IPAddress?, ushort, IGrantPolicySession> factory)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        _bindAddress = bindAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new IPAddress(bindAddress.GetAddressBytes(), bindAddress.ScopeId) : new IPAddress(bindAddress.GetAddressBytes());
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async ValueTask<IGrantLease> CreateLeaseAsync(IPAddress source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source = SourceAddress.Normalize(source);
        if (source.AddressFamily != _bindAddress.AddressFamily)
            throw new ArgumentException("The source must use the configured address family.", nameof(source));
        var local = _bindAddress.Equals(IPAddress.Any) || _bindAddress.Equals(IPAddress.IPv6Any) ? null : _bindAddress;
        Task<IGrantPolicySession> creation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            creation = Task.Run(() => _factory(source, local, GrantPolicy.RdpPort));
            _pending.Add(creation);
        }
        IGrantPolicySession? policy = null;
        try
        {
            policy = await creation;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                var lease = new WfpGrantLease(policy, () => _stopping);
                _leases.Add(lease);
                policy = null;
                return lease;
            }
        }
        finally
        {
            try { if (policy is not null) await Task.Run(policy.Dispose); }
            finally { lock (_sync) _pending.Remove(creation); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _stopping = true;
            var leases = _leases.ToArray();
            var pending = _pending.ToArray();
            _shutdown = Task.Run(async () =>
            {
                // Start all rundown operations before awaiting any one source or creation.
                var closing = leases.Select(lease => lease.DisposeAsync().AsTask()).ToList();
                closing.AddRange(pending.Select(async creation =>
                {
                    var policy = await creation;
                    await Task.Run(policy.Dispose);
                }));
                await Task.WhenAll(closing);
                lock (_sync) { _leases.Clear(); _pending.Clear(); }
            });
            return new(_shutdown);
        }
    }
}
