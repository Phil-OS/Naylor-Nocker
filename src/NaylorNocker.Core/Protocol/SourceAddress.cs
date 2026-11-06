using System.Net;
using System.Net.Sockets;
namespace NaylorNocker.Core.Protocol;
public static class SourceAddress
{
    public static IPAddress Normalize(IPAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.IsIPv4MappedToIPv6) source = source.MapToIPv4();
        if (source.Equals(IPAddress.Any) || source.Equals(IPAddress.IPv6Any) ||
            source.Equals(IPAddress.Broadcast) || source.IsIPv6Multicast ||
            (source.AddressFamily == AddressFamily.InterNetwork && source.GetAddressBytes()[0] is >= 224 and <= 239) ||
            (source.IsIPv6LinkLocal && source.ScopeId == 0))
            throw new ArgumentException("A specific unicast source and required scope are necessary.", nameof(source));
        return source.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPAddress(source.GetAddressBytes(), source.ScopeId) : new IPAddress(source.GetAddressBytes());
    }
}
