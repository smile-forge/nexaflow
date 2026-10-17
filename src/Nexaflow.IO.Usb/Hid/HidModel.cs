using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Hid;

public enum HidReportKind { Input, Output, Feature }

/// <summary>
/// How far Nexaflow can reach a HID collection. Windows keeps keyboards and mice to itself — their input goes to the
/// desktop and nowhere else — so for those only the layout and the feature reports are on offer.
/// </summary>
public enum HidReach { ReadWrite, ReadOnly, LayoutOnly }

/// <summary>
/// One run of a report: a block of buttons or a value (or an array of values), with the usages it carries.
/// <see cref="LinkCollection"/> is Windows' index of the nested collection it sits in, which decoding needs.
/// </summary>
public sealed record HidField(
    HidReportKind Kind, byte ReportId, ushort UsagePage, ushort UsageMin, ushort UsageMax, bool IsButton,
    ushort BitSize, ushort Count, int LogicalMin, int LogicalMax, bool IsAbsolute, ushort LinkCollection, string Name)
{
    public int Bits => BitSize * Count;
}

/// <summary>Every field of one report, in the order Windows lists them.</summary>
public sealed record HidReportLayout(HidReportKind Kind, byte ReportId, IReadOnlyList<HidField> Fields)
{
    public int Bits => Fields.Sum(f => f.Bits);
}

/// <summary>
/// A top-level HID collection — what Windows makes one device of: a mouse, a keyboard's media keys, a vendor channel.
/// Report lengths include the report-ID byte, as Windows counts them.
/// </summary>
public sealed record HidCollection(
    string InstanceId, string DevicePath, ushort UsagePage, ushort Usage, string Title, string? Product,
    int InputLength, int OutputLength, int FeatureLength, IReadOnlyList<HidReportLayout> Reports, HidReach Reach)
{
    public IEnumerable<HidReportLayout> Of(HidReportKind kind) => Reports.Where(r => r.Kind == kind);
}

/// <summary>One decoded value: "X" = "-3", "Buttons" = "1, 3".</summary>
public sealed record HidValue(string Name, string Value);

/// <summary>A report as it arrived or was read, with its fields decoded.</summary>
public sealed record HidReport(DateTimeOffset At, HidReportKind Kind, byte ReportId, byte[] Bytes, IReadOnlyList<HidValue> Values);

/// <summary>An open collection: its input as it arrives, and its feature and output reports on request.</summary>
public interface IHidChannel : IDisposable
{
    HidCollection Collection { get; }

    /// <summary>Input reports as the device sends them, until <paramref name="ct"/> is cancelled or the device goes.</summary>
    IAsyncEnumerable<HidReport> ReadInputAsync(CancellationToken ct);

    /// <summary>Asks the device for feature report <paramref name="reportId"/> (0 when it numbers none).</summary>
    Task<HidReport> GetFeatureAsync(byte reportId, CancellationToken ct);

    /// <summary>Sends a feature report — the first byte is its ID — padded to the collection's feature length.</summary>
    Task SetFeatureAsync(byte[] report, CancellationToken ct);

    /// <summary>Sends an output report — the first byte is its ID — padded to the collection's output length.</summary>
    Task SetOutputAsync(byte[] report, CancellationToken ct);
}

/// <summary>Finds a function's HID collections and opens them. Reading a layout sends nothing to the device.</summary>
public interface IHidAccess
{
    /// <summary>The collections Windows made of <paramref name="function"/>, each with its report layout.</summary>
    IReadOnlyList<HidCollection> CollectionsOf(UsbFunctionNode function, UsbIdsDatabase? names);

    IHidChannel Open(HidCollection collection);
}

/// <summary>HID usage names from usb.ids, with the number when the file has no name for it.</summary>
public static class HidNames
{
    public const ushort ButtonPage = 0x09;

    public static string Page(UsbIdsDatabase? names, ushort page) =>
        names?.UsagePageName(page) ?? (page >= 0xFF00 ? $"Vendor page 0x{page:X4}" : $"Page 0x{page:X2}");

    public static string Usage(UsbIdsDatabase? names, ushort page, ushort usage) =>
        names?.UsageName(page, usage) is { } name && !name.Equals("Undefined", StringComparison.OrdinalIgnoreCase)
            ? name : $"Usage 0x{usage:X2}";

    /// <summary>"Generic Desktop Controls › Mouse".</summary>
    public static string Collection(UsbIdsDatabase? names, ushort page, ushort usage) =>
        $"{Page(names, page)} › {Usage(names, page, usage)}";

    /// <summary>A field as a person reads it: "Buttons 1–5", "X", "Usages 0x01–0x3F (Vendor page 0xFF00)".</summary>
    public static string Field(UsbIdsDatabase? names, ushort page, ushort min, ushort max, bool isButton)
    {
        if (page == ButtonPage) return min == max ? $"Button {min}" : $"Buttons {min}–{max}";
        if (page >= 0xFF00) return min == max ? $"0x{min:X2} ({Page(names, page)})" : $"0x{min:X2}–0x{max:X2} ({Page(names, page)})";
        if (min == max) return Usage(names, page, min);
        return isButton && max - min > 8
            ? $"{Page(names, page)} 0x{min:X2}–0x{max:X2}"
            : string.Join(", ", Enumerable.Range(min, max - min + 1).Select(u => Usage(names, page, (ushort)u)));
    }
}
