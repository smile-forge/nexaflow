using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nexaflow.IO.Network.Adapters;

/// <summary>One IP address assigned to an adapter, with the prefix it sits on.</summary>
/// <param name="Address">The address.</param>
/// <param name="PrefixLength">CIDR prefix length.</param>
public readonly record struct AdapterAddress(IPAddress Address, int PrefixLength)
{
    /// <summary>The directed broadcast for this prefix — the only broadcast target the guard will permit,
    /// and what Wake-on-LAN needs. Null for IPv6, which has no broadcast.</summary>
    public IPAddress? DirectedBroadcast
    {
        get
        {
            if (Address.AddressFamily != AddressFamily.InterNetwork || PrefixLength is < 0 or > 32) return null;

            var bytes = Address.GetAddressBytes();
            uint addr = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            uint mask = PrefixLength == 0 ? 0u : uint.MaxValue << (32 - PrefixLength);
            uint bcast = addr | ~mask;
            return new IPAddress(new[] { (byte)(bcast >> 24), (byte)(bcast >> 16), (byte)(bcast >> 8), (byte)bcast });
        }
    }

    /// <summary>True if <paramref name="other"/> is on this same prefix — the test the send guard uses to
    /// decide whether a target is locally attached.</summary>
    public bool Contains(IPAddress other)
    {
        if (other.AddressFamily != Address.AddressFamily) return false;

        var a = Address.GetAddressBytes();
        var b = other.GetAddressBytes();
        if (a.Length != b.Length) return false;

        int fullBytes = PrefixLength / 8;
        int spareBits = PrefixLength % 8;

        for (int i = 0; i < fullBytes; i++) if (a[i] != b[i]) return false;
        if (spareBits == 0) return true;

        int m = 0xFF << (8 - spareBits);
        return (a[fullBytes] & m) == (b[fullBytes] & m);
    }

    public override string ToString() => $"{Address}/{PrefixLength}";
}

/// <summary>
/// A local network adapter, flattened into just what discovery and the send guard need. Deliberately a
/// snapshot rather than a live <see cref="NetworkInterface"/> wrapper: probes run on background threads and
/// must not race the OS view mid-sweep.
/// </summary>
public sealed class NetworkAdapterInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required NetworkInterfaceType Type { get; init; }
    public required OperationalStatus Status { get; init; }

    /// <summary>Normalised lower-case colon form, e.g. <c>aa:bb:cc:dd:ee:ff</c>. Empty for adapters with
    /// no hardware address (loopback, some tunnels).</summary>
    public required string MacAddress { get; init; }

    public long SpeedBitsPerSecond { get; init; }
    public List<AdapterAddress> Addresses { get; } = [];
    public List<IPAddress> Gateways { get; } = [];
    public List<IPAddress> DnsServers { get; } = [];

    /// <summary>True for adapters that carry no real LAN — loopback and tunnels. Excluded from sweeps and,
    /// importantly, from the guard's allow-list, so "hidden from the UI" and "not a legal target" stay the
    /// same decision.</summary>
    public bool IsLoopbackOrTunnel
        => Type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel;

    /// <summary>True if the adapter is up and has at least one usable unicast address.</summary>
    public bool IsUsable
        => Status == OperationalStatus.Up && !IsLoopbackOrTunnel && Addresses.Count > 0;

    /// <summary>
    /// A stable id for the L2 segment this adapter sits on, used to scope IP and MAC identity claims. Two
    /// devices on two different subnets that share <c>192.168.1.10</c> must not fuse, and this is what
    /// keeps them apart.
    /// </summary>
    public string SegmentId
    {
        get
        {
            var v4 = Addresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (v4.Address is null) return Id;

            var bytes = v4.Address.GetAddressBytes();
            uint addr = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            uint mask = v4.PrefixLength == 0 ? 0u : uint.MaxValue << (32 - v4.PrefixLength);
            uint net = addr & mask;
            return $"{net >> 24}.{(net >> 16) & 0xFF}.{(net >> 8) & 0xFF}.{net & 0xFF}/{v4.PrefixLength}";
        }
    }

    public override string ToString() => $"{Name} ({MacAddress})";
}
