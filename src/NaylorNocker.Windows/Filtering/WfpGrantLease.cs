using NaylorNocker.Core.Admission;

namespace NaylorNocker.Windows.Filtering;

public sealed class WfpGrantLease : IGrantLease
{
    private readonly object _sync = new();
    private readonly IGrantPolicySession _policy;
    private readonly Func<bool> _backendStopping;
    private bool _disposed;

    internal WfpGrantLease(IGrantPolicySession policy, Func<bool> backendStopping) =>
        (_policy, _backendStopping) = (policy, backendStopping);

    public ValueTask EnableAdmissionAsync(CancellationToken cancellationToken) =>
        UpdateAsync(_policy.EnableAdmission, cancellationToken);

    public ValueTask DisableAdmissionAsync(CancellationToken cancellationToken) =>
        UpdateAsync(_policy.DisableAdmission, cancellationToken);

    private ValueTask UpdateAsync(Action update, CancellationToken cancellationToken) => new(Task.Run(() =>
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed || _backendStopping(), this);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                update();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                _disposed = true;
                _policy.Dispose();
                throw;
            }
        }
    }));

    public ValueTask<bool> CanReleaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Approved v1 policy: remember sources and retain reauthorization until shutdown.
        return ValueTask.FromResult(false);
    }

    public ValueTask DisposeAsync() => new(Task.Run(() =>
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _policy.Dispose();
        }
    }));
}
