using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Windows.Native;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// HID through hid.dll. A collection's layout comes from its preparsed data — Windows' compiled form of the report
/// descriptor — read over a handle opened with no access rights, which sends nothing to the device. Reports are
/// decoded the same way, so a field means exactly what the HID class driver thinks it means.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidAccess : IHidAccess
{
    const uint OverlappedFlag = 0x40000000;

    public IReadOnlyList<HidCollection> CollectionsOf(UsbFunctionNode function, UsbIdsDatabase? names)
    {
        var found = new List<HidCollection>();
        foreach (var instanceId in function.Children.Select(c => c.InstanceId).Prepend(function.InstanceId))
            foreach (var path in DevNodes.Interfaces(HidDll.InterfaceClass, instanceId))
                if (Read(path, instanceId, names) is { } collection) found.Add(collection);
        return found;
    }

    public IHidChannel Open(HidCollection collection) => new WindowsHidChannel(collection);

    /// <summary>Copies the preparsed blob out of hid.dll. Its length is nowhere stated, so it is worked out from the
    /// header (the layout hidapi documents): 44 bytes, then the caps, then the link collections, 16 bytes each.</summary>
    public byte[]? PreparsedData(HidCollection collection)
    {
        using var handle = OpenHandle(collection.DevicePath, 0, overlapped: false);
        if (handle.IsInvalid || !HidDll.HidD_GetPreparsedData(handle, out nint preparsed)) return null;
        try
        {
            var header = new byte[PreparsedHeader];
            Marshal.Copy(preparsed, header, 0, header.Length);
            if (!header.AsSpan(0, 8).SequenceEqual("HidP KDR"u8)) return null;
            int length = PreparsedHeader + U16(header, 40) + U16(header, 42) * 16;
            var blob = new byte[length];
            Marshal.Copy(preparsed, blob, 0, length);
            return blob;
        }
        finally { HidDll.HidD_FreePreparsedData(preparsed); }
    }

    const int PreparsedHeader = 44;

    static HidCollection? Read(string path, string instanceId, UsbIdsDatabase? names)
    {
        using var handle = OpenHandle(path, 0, overlapped: false);
        if (handle.IsInvalid || !HidDll.HidD_GetPreparsedData(handle, out nint preparsed)) return null;
        try
        {
            var caps = Caps(preparsed);
            var fields = new List<HidField>();
            foreach (var kind in (HidReportKind[])[HidReportKind.Input, HidReportKind.Output, HidReportKind.Feature])
            {
                fields.AddRange(ButtonFields(preparsed, kind, names));
                fields.AddRange(ValueFields(preparsed, kind, names));
            }
            var reports = fields.GroupBy(f => (f.Kind, f.ReportId))
                .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.ReportId)
                .Select(g => new HidReportLayout(g.Key.Kind, g.Key.ReportId, [.. g]))
                .ToList();

            return new HidCollection(instanceId, path, caps.UsagePage, caps.Usage,
                HidNames.Collection(names, caps.UsagePage, caps.Usage), Product(handle),
                caps.Input, caps.Output, caps.Feature, reports, Reach(path));
        }
        finally { HidDll.HidD_FreePreparsedData(preparsed); }
    }

    /// <summary>How far a handle can be opened: Windows refuses read access to keyboards and mice.</summary>
    static HidReach Reach(string path)
    {
        using (var both = OpenHandle(path, Kernel32.GenericRead | Kernel32.GenericWrite, overlapped: false))
            if (!both.IsInvalid) return HidReach.ReadWrite;
        using (var read = OpenHandle(path, Kernel32.GenericRead, overlapped: false))
            if (!read.IsInvalid) return HidReach.ReadOnly;
        return HidReach.LayoutOnly;
    }

    internal static SafeFileHandle OpenHandle(string path, uint access, bool overlapped) =>
        Kernel32.CreateFile(path, access, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting,
            overlapped ? OverlappedFlag : 0, 0);

    internal readonly record struct HidCaps(ushort Usage, ushort UsagePage, int Input, int Output, int Feature);

    internal static unsafe HidCaps Caps(nint preparsed)
    {
        byte* caps = stackalloc byte[HidDll.CapsSize];
        if (HidDll.HidP_GetCaps(preparsed, caps) != HidDll.StatusSuccess) return default;
        var s = new ReadOnlySpan<byte>(caps, HidDll.CapsSize);
        return new(U16(s, 0), U16(s, 2), U16(s, 4), U16(s, 6), U16(s, 8));
    }

    static unsafe IEnumerable<HidField> ButtonFields(nint preparsed, HidReportKind kind, UsbIdsDatabase? names)
    {
        ushort count = 512;
        var buffer = new byte[count * HidDll.ButtonCapsSize];
        int status;
        fixed (byte* p = buffer) status = HidDll.HidP_GetButtonCaps((int)kind, p, ref count, preparsed);
        if (status != HidDll.StatusSuccess) return [];

        var fields = new List<(int Index, HidField Field)>();
        for (int i = 0; i < count; i++)
        {
            var c = buffer.AsSpan(i * HidDll.ButtonCapsSize, HidDll.ButtonCapsSize);
            if (c[3] != 0) continue; // an alias names the same control a second way
            bool range = c[12] != 0, variable = (U16(c, 4) & 0x02) != 0;
            ushort page = U16(c, 0), min = U16(c, 56), max = range ? U16(c, 58) : min, reportCount = U16(c, 16);
            ushort span = (ushort)(max - min + 1);
            // A bitmap of buttons is one bit each; an array (a keyboard's key slots) is reportCount slots wide, whose
            // width the button caps do not say.
            var field = variable
                ? new HidField(kind, c[2], page, min, max, true, 1, span, 0, 1, c[15] != 0, U16(c, 6), HidNames.Field(names, page, min, max, true))
                : new HidField(kind, c[2], page, min, max, true, 0, Math.Max(reportCount, (ushort)1), min, max, c[15] != 0, U16(c, 6),
                    $"{HidNames.Field(names, page, min, max, true)} (array)");
            fields.Add((U16(c, 68), field));
        }
        return fields.OrderBy(f => f.Index).Select(f => f.Field);
    }

    static unsafe IEnumerable<HidField> ValueFields(nint preparsed, HidReportKind kind, UsbIdsDatabase? names)
    {
        ushort count = 512;
        var buffer = new byte[count * HidDll.ValueCapsSize];
        int status;
        fixed (byte* p = buffer) status = HidDll.HidP_GetValueCaps((int)kind, p, ref count, preparsed);
        if (status != HidDll.StatusSuccess) return [];

        var fields = new List<(int Index, HidField Field)>();
        for (int i = 0; i < count; i++)
        {
            var c = buffer.AsSpan(i * HidDll.ValueCapsSize, HidDll.ValueCapsSize);
            if (c[3] != 0) continue;
            bool range = c[12] != 0;
            ushort page = U16(c, 0), min = U16(c, 56), max = range ? U16(c, 58) : min;
            fields.Add((U16(c, 68), new HidField(kind, c[2], page, min, max, false, U16(c, 18), U16(c, 20),
                BinaryPrimitives.ReadInt32LittleEndian(c[40..]), BinaryPrimitives.ReadInt32LittleEndian(c[44..]),
                c[15] != 0, U16(c, 6), HidNames.Field(names, page, min, max, false))));
        }
        return fields.OrderBy(f => f.Index).Select(f => f.Field);
    }

    static unsafe string? Product(SafeFileHandle handle)
    {
        char* text = stackalloc char[128];
        if (!HidDll.HidD_GetProductString(handle, text, 256)) return null;
        var s = new string(text).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    static ushort U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16LittleEndian(s[at..]);
}

/// <summary>
/// An open HID collection. Input arrives over an overlapped handle, one report per read; feature and output reports go
/// over a second, synchronous one, because hid.dll's calls block on the handle they are given.
/// </summary>
[SupportedOSPlatform("windows")]
sealed unsafe class WindowsHidChannel : IHidChannel
{
    readonly SafeFileHandle _control;
    readonly FileStream? _input;
    readonly nint _preparsed;
    readonly bool _numbered;
    int _disposed;

    public WindowsHidChannel(HidCollection collection)
    {
        Collection = collection;
        uint access = collection.Reach switch
        {
            HidReach.ReadWrite => Kernel32.GenericRead | Kernel32.GenericWrite,
            HidReach.ReadOnly => Kernel32.GenericRead,
            _ => 0,
        };
        _control = WindowsHidAccess.OpenHandle(collection.DevicePath, access, overlapped: false);
        if (_control.IsInvalid) throw new IOException(Kernel32.LastError("Opening the HID collection"));
        if (!HidDll.HidD_GetPreparsedData(_control, out _preparsed))
        {
            _control.Dispose();
            throw new IOException("Windows has no report layout for this collection.");
        }
        if (collection.Reach != HidReach.LayoutOnly)
        {
            var reader = WindowsHidAccess.OpenHandle(collection.DevicePath, access, overlapped: true);
            if (!reader.IsInvalid) _input = new FileStream(reader, FileAccess.Read, bufferSize: 0, isAsync: true);
        }
        _numbered = collection.Reports.Any(r => r.ReportId != 0);
    }

    public HidCollection Collection { get; }

    public async IAsyncEnumerable<HidReport> ReadInputAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (_input is null) throw new InvalidOperationException("Windows keeps this collection's input to itself.");
        var buffer = new byte[Math.Max(Collection.InputLength, 1)];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try { read = await _input.ReadAsync(buffer, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { yield break; }
            catch (ObjectDisposedException) { yield break; }
            if (read == 0) yield break;
            yield return Decode(HidReportKind.Input, buffer.AsSpan(0, read).ToArray());
        }
    }

    public Task<HidReport> GetFeatureAsync(byte reportId, CancellationToken ct) => Task.Run(() =>
    {
        var report = new byte[Math.Max(Collection.FeatureLength, 1)];
        report[0] = reportId;
        if (!Call(report, static (h, p, n) => HidDll.HidD_GetFeature(h, p, n)))
            throw new IOException(Kernel32.LastError($"Reading feature report {reportId}"));
        return Decode(HidReportKind.Feature, report);
    }, ct);

    public Task SetFeatureAsync(byte[] report, CancellationToken ct) => Task.Run(() =>
    {
        if (!Call(Padded(report, Collection.FeatureLength), static (h, p, n) => HidDll.HidD_SetFeature(h, p, n)))
            throw new IOException(Kernel32.LastError("Sending the feature report"));
    }, ct);

    public Task SetOutputAsync(byte[] report, CancellationToken ct) => Task.Run(() =>
    {
        if (!Call(Padded(report, Collection.OutputLength), static (h, p, n) => HidDll.HidD_SetOutputReport(h, p, n)))
            throw new IOException(Kernel32.LastError("Sending the output report"));
    }, ct);

    unsafe delegate bool ReportCall(SafeFileHandle handle, byte* report, uint length);

    unsafe bool Call(byte[] report, ReportCall call)
    {
        fixed (byte* p = report) return call(_control, p, (uint)report.Length);
    }

    static byte[] Padded(byte[] report, int length)
    {
        if (report.Length > length) throw new ArgumentException($"The report is {report.Length} bytes; this collection takes {length}.");
        var padded = new byte[length];
        report.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>Every field of the report's ID, decoded by hid.dll; the buttons grouped by page.</summary>
    unsafe HidReport Decode(HidReportKind kind, byte[] report)
    {
        byte id = _numbered && report.Length > 0 ? report[0] : (byte)0;
        var layout = Collection.Reports.FirstOrDefault(r => r.Kind == kind && r.ReportId == id);
        var values = new List<HidValue>();
        if (layout is null) return new(DateTimeOffset.Now, kind, id, report, values);

        fixed (byte* r = report)
        {
            var buttonPages = layout.Fields.Where(f => f.IsButton).Select(f => f.UsagePage).Distinct().ToList();
            if (buttonPages.Count > 0)
            {
                uint length = (uint)Math.Max(layout.Fields.Where(f => f.IsButton).Sum(f => f.UsageMax - f.UsageMin + 1 + f.Count), 16);
                var pressed = new ushort[length * 2];
                fixed (ushort* u = pressed)
                    if (HidDll.HidP_GetUsagesEx((int)kind, 0, u, ref length, _preparsed, r, (uint)report.Length) != HidDll.StatusSuccess)
                        length = 0;
                foreach (var page in buttonPages)
                {
                    var down = Enumerable.Range(0, (int)length).Where(i => pressed[i * 2 + 1] == page).Select(i => pressed[i * 2]).ToList();
                    string name = page == HidNames.ButtonPage ? "Buttons" : page == 0x07 ? "Keys" : $"Page 0x{page:X2}";
                    values.Add(new(name, down.Count == 0 ? "none" : string.Join(", ", down.Select(u => page == HidNames.ButtonPage ? u.ToString() : $"0x{u:X2}"))));
                }
            }

            foreach (var field in layout.Fields.Where(f => !f.IsButton))
                for (int usage = field.UsageMin; usage <= Math.Min(field.UsageMax, field.UsageMin + 15); usage++)
                    values.Add(new(field.UsageMin == field.UsageMax ? field.Name : $"{field.Name} [0x{usage:X2}]", Value(kind, field, (ushort)usage, r, report.Length)));
        }
        return new(DateTimeOffset.Now, kind, id, report, values);
    }

    unsafe string Value(HidReportKind kind, HidField field, ushort usage, byte* report, int length)
    {
        if (field.Count > 1 && field.UsageMin == field.UsageMax)
        {
            int bytes = (field.Bits + 7) / 8;
            var array = new byte[bytes];
            fixed (byte* a = array)
                return HidDll.HidP_GetUsageValueArray((int)kind, field.UsagePage, field.LinkCollection, usage, a, (ushort)bytes, _preparsed, report, (uint)length) == HidDll.StatusSuccess
                    ? Descriptors.UsbHex.Format(array) : "?";
        }
        if (HidDll.HidP_GetUsageValue((int)kind, field.UsagePage, field.LinkCollection, usage, out uint raw, _preparsed, report, (uint)length) != HidDll.StatusSuccess)
            return "?";
        if (field.LogicalMin < 0 && field.BitSize is > 0 and < 32)
        {
            int shift = 32 - field.BitSize;
            return ((int)(raw << shift) >> shift).ToString();
        }
        return raw.ToString();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _input?.Dispose();
        HidDll.HidD_FreePreparsedData(_preparsed);
        _control.Dispose();
    }
}
