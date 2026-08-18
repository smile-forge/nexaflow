using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.Network.Arp;

/// <summary>The neighbour states IP Helper reports. Only the reachable-ish ones are worth asserting as a
/// device — an <see cref="Incomplete"/> entry means we asked and nobody answered.</summary>
internal enum NeighborState
{
    Unreachable = 0, Incomplete = 1, Probe = 2, Delay = 3, Stale = 4, Reachable = 5, Permanent = 6, Maximum = 7,
}

/// <summary>One neighbour-table row, flattened out of the native struct.</summary>
internal sealed record NeighborEntry(
    IPAddress Address, string Mac, uint InterfaceIndex, NeighborState State, bool IsRouter);

/// <summary>
/// IP Helper interop for the neighbour (ARP / IPv6 ND) table.
///
/// <para>
/// <c>GetIpNetTable2</c> needs no elevation, which is why this is the cheapest discovery layer there is.
/// Its limitation is real and worth stating: the table only holds entries this host has recently
/// <i>talked to</i>, so on a quiet network it is nearly empty until something populates it — which is what
/// the (opt-in, consent-gated) sweep is for.
/// </para>
/// </summary>
internal static class NativeMethods
{
    private const uint AF_UNSPEC = 0;
    private const uint NO_ERROR = 0;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIpNetTable2(uint family, out IntPtr table);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeMibTable(IntPtr table);

    // SOCKADDR_INET. Alignment is 4, not 8 — in6_addr is a byte array, so no member forces 8-byte
    // alignment and the struct is exactly 28 bytes. Declaring the v6 address as two ulongs would pad it
    // to 32 and silently shift every field after it, which is the classic way to read garbage here.
    [StructLayout(LayoutKind.Sequential)]
    private struct SockaddrInet
    {
        public ushort Family;
        public ushort Port;
        public uint FlowInfoOrV4;      // for AF_INET this IS the address
        public uint V6A, V6B, V6C, V6D;
        public uint ScopeId;

        public readonly IPAddress? ToIPAddress()
        {
            switch ((AddressFamily)Family)
            {
                case AddressFamily.InterNetwork:
                    return new IPAddress(BitConverter.GetBytes(FlowInfoOrV4));

                case AddressFamily.InterNetworkV6:
                    var b = new byte[16];
                    BitConverter.GetBytes(V6A).CopyTo(b, 0);
                    BitConverter.GetBytes(V6B).CopyTo(b, 4);
                    BitConverter.GetBytes(V6C).CopyTo(b, 8);
                    BitConverter.GetBytes(V6D).CopyTo(b, 12);
                    return new IPAddress(b, ScopeId);

                default:
                    return null;
            }
        }
    }

    private const int IF_MAX_PHYS_ADDRESS_LENGTH = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpNetRow2
    {
        public SockaddrInet Address;                                    // 0
        public uint InterfaceIndex;                                     // 28
        public ulong InterfaceLuid;                                     // 32 (8-aligned)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IF_MAX_PHYS_ADDRESS_LENGTH)]
        public byte[] PhysicalAddress;                                  // 40
        public uint PhysicalAddressLength;                              // 72
        public uint State;                                              // 76
        public byte Flags;                                              // 80  (IsRouter : 1, IsUnreachable : 1)
        public uint ReachabilityTime;                                   // 84
    }

    /// <summary>
    /// Reads the neighbour table. Returns an empty list rather than throwing when IP Helper declines — a
    /// discovery layer that cannot read the table must degrade to "found nothing", never take the sweep
    /// down with it.
    /// </summary>
    public static IReadOnlyList<NeighborEntry> ReadNeighborTable()
    {
        IntPtr table = IntPtr.Zero;
        try
        {
            if (GetIpNetTable2(AF_UNSPEC, out table) != NO_ERROR || table == IntPtr.Zero) return [];

            // MIB_IPNET_TABLE2 { ULONG NumEntries; MIB_IPNET_ROW2 Table[ANY_SIZE]; } — the rows are
            // 8-aligned, so they start 8 bytes in, not 4.
            uint count = (uint)Marshal.ReadInt32(table);
            int rowSize = Marshal.SizeOf<MibIpNetRow2>();
            IntPtr rows = IntPtr.Add(table, 8);

            var result = new List<NeighborEntry>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibIpNetRow2>(IntPtr.Add(rows, (int)(i * (uint)rowSize)));

                var ip = row.Address.ToIPAddress();
                if (ip is null) continue;

                int macLen = (int)Math.Min(row.PhysicalAddressLength, IF_MAX_PHYS_ADDRESS_LENGTH);
                if (macLen != 6) continue;      // only real Ethernet/Wi-Fi addresses are useful identity

                var mac = FormatMac(row.PhysicalAddress, macLen);
                if (mac is null) continue;

                result.Add(new NeighborEntry(
                    ip, mac, row.InterfaceIndex, (NeighborState)row.State, (row.Flags & 0x01) != 0));
            }
            return result;
        }
        catch (DllNotFoundException) { return []; }
        catch (EntryPointNotFoundException) { return []; }
        finally
        {
            if (table != IntPtr.Zero) FreeMibTable(table);
        }
    }

    /// <summary>Lower-case colon form — the single normalised MAC representation the identity model uses.
    /// An all-zero address is not identity and is rejected.</summary>
    private static string? FormatMac(byte[] bytes, int len)
    {
        bool allZero = true;
        for (int i = 0; i < len; i++) if (bytes[i] != 0) { allZero = false; break; }
        if (allZero) return null;

        return string.Join(':', bytes.Take(len).Select(b => b.ToString("x2")));
    }
}
