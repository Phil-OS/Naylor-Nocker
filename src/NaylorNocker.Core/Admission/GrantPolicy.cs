namespace NaylorNocker.Core.Admission;
public static class GrantPolicy
{
    public const ushort RdpPort = 3389;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    public const int MaxSources = 1024;
}
