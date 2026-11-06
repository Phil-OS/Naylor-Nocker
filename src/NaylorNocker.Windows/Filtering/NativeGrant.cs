using System.Runtime.Versioning;
namespace NaylorNocker.Windows.Filtering;

[SupportedOSPlatform("windows")]
public sealed class NativeGrant : IDisposable
{
    private readonly WfpPolicySession _owner;
    private readonly GrantFilterPlan _plan;
    private bool _disposed;
    public ulong RetentionId { get; }
    public ulong? AdmissionId { get; private set; }

    internal NativeGrant(WfpPolicySession owner, GrantFilterPlan plan, ulong retention)
        => (_owner, _plan, RetentionId) = (owner, plan, retention);

    public void EnableAdmission()
    {
        lock (_owner.Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed || _owner.IsDisposed, this);
            if (AdmissionId is not null) return;
            AdmissionId = _owner.InTransaction(() =>
            {
                using var buffer = new NativeFilterBuffer(_plan, _owner.SubLayer, reauthorize: false);
                return _owner.AddFilter(ref buffer.Filter);
            });
        }
    }

    public void DisableAdmission()
    {
        lock (_owner.Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed || _owner.IsDisposed, this);
            if (AdmissionId is not ulong id) return;
            _owner.InTransaction(() => { _owner.DeleteFilter(id); return true; });
            AdmissionId = null;
        }
    }

    public void Dispose()
    {
        lock (_owner.Sync)
        {
            if (_disposed) return;
            if (!_owner.IsDisposed)
                _owner.InTransaction(() =>
                {
                    if (AdmissionId is ulong id) _owner.DeleteFilter(id);
                    _owner.DeleteFilter(RetentionId);
                    return true;
                });
            AdmissionId = null;
            _disposed = true;
        }
    }
}
