using System.Net;
namespace NaylorNocker.Core.Admission;
public interface IFirewallBackend : IAsyncDisposable
{
    // DisposeAsync must revoke all owned policy, including in-flight operations, without
    // waiting on coordinator gates. It may run concurrently with lease operations.
    // Once disposal completes, no in-flight operation may commit a new permit.
    ValueTask<IGrantLease> CreateLeaseAsync(IPAddress source, CancellationToken cancellationToken);
}
