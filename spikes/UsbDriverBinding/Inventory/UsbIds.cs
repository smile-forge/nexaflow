namespace Nexaflow.Spikes.UsbDriverBinding.Inventory;

/// <summary>Vendor and product names from a linux-usb.org usb.ids file (vendor lines flush left, products one tab in).</summary>
public sealed class UsbIds
{
    readonly Dictionary<ushort, string> _vendors = [];
    readonly Dictionary<uint, string> _products = [];

    public static UsbIds Load(string path)
    {
        var ids = new UsbIds();
        ushort? vendor = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length < 6 || line[0] == '#') continue;
            if (line.StartsWith("C ", StringComparison.Ordinal)) break; // class section follows the vendor list
            if (line[0] != '\t' && TryHex(line.AsSpan(0, 4), out var v))
            {
                vendor = v;
                ids._vendors[v] = line[4..].Trim();
            }
            else if (vendor is { } current && line[0] == '\t' && line.Length > 6 && line[1] != '\t' && TryHex(line.AsSpan(1, 4), out var p))
                ids._products[(uint)current << 16 | p] = line[5..].Trim();
        }
        return ids;
    }

    public string? Vendor(ushort vid) => _vendors.GetValueOrDefault(vid);
    public string? Product(ushort vid, ushort pid) => _products.GetValueOrDefault((uint)vid << 16 | pid);

    static bool TryHex(ReadOnlySpan<char> s, out ushort value) =>
        ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out value);
}
