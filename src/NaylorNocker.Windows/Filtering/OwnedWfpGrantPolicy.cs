using System.Net;
using System.Runtime.Versioning;

namespace NaylorNocker.Windows.Filtering;

[SupportedOSPlatform("windows")]
internal sealed class OwnedWfpGrantPolicy(WfpPolicySession session, NativeGrant grant) : IGrantPolicySession
{
    public static IGrantPolicySession Create(IPAddress source, IPAddress? local, ushort port)
    {
        var session = WfpPolicySession.Open();
        try { return new OwnedWfpGrantPolicy(session, session.AddGrant(source, local, port)); }
        catch { session.Dispose(); throw; }
    }

    public void EnableAdmission() => grant.EnableAdmission();
    public void DisableAdmission() => grant.DisableAdmission();
    public void Dispose() => session.Dispose();
}
