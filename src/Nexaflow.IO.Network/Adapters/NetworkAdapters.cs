using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nexaflow.IO.Network.Adapters;

/// <summary>
/// Reads the machine's adapters into <see cref="NetworkAdapterInfo"/> snapshots.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot rather than a live view, deliberately: a sweep runs across several adapters on background
/// threads and must not race the OS changing one underneath it. Re-read between runs, not during.
/// </para>
/// <para>
/// This is also what feeds the guard's allow-list, so what it does <b>not</b> return matters as much as
/// what it does. An adapter that is down has no neighbours worth asking about and is not a legal target.
/// </para>
/// </remarks>
public static class NetworkAdapters
{
    /// <summary>Every adapter the OS reports, as snapshots. Loopback and tunnels are included — the caller
    /// filters with <see cref="NetworkAdapterInfo.IsUsable"/>, because "hidden from a sweep" and "not a
    /// legal target" are decisions the guard and the UI make together rather than ones taken here.</summary>
    public static IReadOnlyList<NetworkAdapterInfo> Read()
    {
        List<NetworkAdapterInfo> found = [];

        foreach (var nic in Interfaces())
        {
            var info = new NetworkAdapterInfo
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                Type = nic.NetworkInterfaceType,
                Status = nic.OperationalStatus,
                MacAddress = Mac(nic),
                SpeedBitsPerSecond = Speed(nic),
            };

            Fill(info, nic);
            found.Add(info);
        }

        return found;
    }

    /// <summary>Every adapter the caller can usefully sweep — up, addressed, and carrying a real LAN.</summary>
    public static IReadOnlyList<NetworkAdapterInfo> Usable() => [.. Read().Where(a => a.IsUsable)];

    private static IEnumerable<NetworkInterface> Interfaces()
    {
        // A machine with no network at all is an ordinary state, not a failure — an empty list is the
        // right answer and the page says "no adapters" rather than showing an error.
        try { return NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return []; }
    }

    private static string Mac(NetworkInterface nic)
    {
        var bytes = nic.GetPhysicalAddress().GetAddressBytes();

        // Normalised to lower-case colon form here and nowhere else. The MAC is the strongest identity key
        // the device graph has, so two spellings of one address would fail to fuse — and the OS spells it
        // differently from a neighbour table, an ARP reply and a UPnP description.
        return bytes.Length == 0 ? "" : string.Join(':', bytes.Select(b => b.ToString("x2")));
    }

    private static long Speed(NetworkInterface nic)
    {
        try { return nic.Speed; }
        catch (NetworkInformationException) { return 0; }   // some tunnels have no answer
    }

    private static void Fill(NetworkAdapterInfo info, NetworkInterface nic)
    {
        IPInterfaceProperties props;
        try { props = nic.GetIPProperties(); }
        catch (NetworkInformationException) { return; }

        foreach (var unicast in props.UnicastAddresses)
        {
            if (unicast.Address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                continue;

            info.Addresses.Add(new AdapterAddress(unicast.Address, PrefixOf(unicast)));
        }

        foreach (var gateway in props.GatewayAddresses)
            if (!gateway.Address.Equals(IPAddress.Any)) info.Gateways.Add(gateway.Address);

        foreach (var dns in props.DnsAddresses)
            info.DnsServers.Add(dns);
    }

    /// <summary>The prefix length, or the classful-ish default when the OS declines to say. Windows leaves
    /// this at zero on some virtual adapters, and a zero prefix would make the whole address space look
    /// locally attached — which is precisely the mistake the guard exists to prevent.</summary>
    private static int PrefixOf(UnicastIPAddressInformation unicast)
    {
        int declared;
        try { declared = unicast.PrefixLength; }
        catch (PlatformNotSupportedException) { declared = 0; }

        if (declared > 0) return declared;

        return unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 ? 64 : 32;
    }
}
