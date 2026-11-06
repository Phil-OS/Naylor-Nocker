using System.Net;
using System.Net.Sockets;

namespace NaylorNocker.Windows.Filtering;

public sealed record GrantFilterPlan(IPAddress Source, IPAddress? LocalAddress, ushort Port,
    Guid Layer, uint? InterfaceIndex)
{
    public static GrantFilterPlan Create(IPAddress source, IPAddress? localAddress, ushort port)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfZero(port);
        source = Normalize(source);
        localAddress = localAddress is null ? null : Normalize(localAddress);
        if (source.Equals(IPAddress.Any) || source.Equals(IPAddress.IPv6Any) ||
            source.Equals(IPAddress.Broadcast) || source.IsIPv6Multicast ||
            (source.AddressFamily == AddressFamily.InterNetwork && source.GetAddressBytes()[0] is >= 224 and <= 239))
            throw new ArgumentException("A specific unicast source address is required.", nameof(source));
        if (localAddress is not null && localAddress.AddressFamily != source.AddressFamily)
            throw new ArgumentException("Local and remote address families differ.", nameof(localAddress));
        if (source.AddressFamily == AddressFamily.InterNetwork)
            return new(source, localAddress?.Equals(IPAddress.Any) == true ? null : localAddress,
                port, new("e1cd9fe7-f4b5-4273-96c0-592e487b8650"), null);

        if (source.IsIPv6LinkLocal && source.ScopeId == 0)
            throw new ArgumentException("A link-local source requires its interface scope.", nameof(source));
        var remoteScope = source.ScopeId;
        var localScope = localAddress?.ScopeId ?? 0;
        if (remoteScope != 0 && localScope != 0 && remoteScope != localScope)
            throw new ArgumentException("Local and remote interface scopes differ.", nameof(localAddress));
        var scope = remoteScope != 0 ? remoteScope : localScope;
        return new(source, localAddress?.Equals(IPAddress.IPv6Any) == true ? null : localAddress,
            port, new("a3b42c97-9f04-4672-b87e-cee9c483257f"),
            scope == 0 ? null : checked((uint)scope));
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() :
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPAddress(address.GetAddressBytes(), address.ScopeId)
            : new IPAddress(address.GetAddressBytes());
}
