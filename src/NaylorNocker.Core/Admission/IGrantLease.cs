namespace NaylorNocker.Core.Admission;
public interface IGrantLease : IAsyncDisposable
{
    ValueTask EnableAdmissionAsync(CancellationToken cancellationToken);
    ValueTask DisableAdmissionAsync(CancellationToken cancellationToken);
    ValueTask<bool> CanReleaseAsync(CancellationToken cancellationToken);
}
