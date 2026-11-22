using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Nexaflow.Interaction;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using WpfDataObject = System.Windows.IDataObject;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// Files a drag carries as content: the table of their names (FILEGROUPDESCRIPTORW), and the content of each, asked by
/// its place in that table — which WPF's own <c>GetData</c> cannot say, so it is asked of the COM object beneath.
/// </summary>
internal static class FileContentsReader
{
    // FILEDESCRIPTORW, as laid out in the table.
    private const int DescriptorSize = 592, AttributesAt = 36, SizeHighAt = 64, SizeLowAt = 68, NameAt = 72, NameBytes = 520;
    private const uint HasAttributes = 0x04, HasSize = 0x40, IsDirectory = 0x10;

    public static IReadOnlyList<CarriedFile> Read(WpfDataObject data, byte[] table)
    {
        if (table.Length < sizeof(int) || data is not ComDataObject com) return [];

        var count = BinaryPrimitives.ReadInt32LittleEndian(table);
        if (count <= 0 || count > (table.Length - sizeof(int)) / DescriptorSize) return [];

        var files = new List<CarriedFile>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = table.AsSpan(sizeof(int) + i * DescriptorSize, DescriptorSize);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var folder = (flags & HasAttributes) != 0
                         && (BinaryPrimitives.ReadUInt32LittleEndian(entry[AttributesAt..]) & IsDirectory) != 0;
            long? size = (flags & HasSize) != 0
                ? ((long)BinaryPrimitives.ReadUInt32LittleEndian(entry[SizeHighAt..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(entry[SizeLowAt..])
                : null;

            var name = Encoding.Unicode.GetString(entry.Slice(NameAt, NameBytes));
            var end = name.IndexOf('\0');
            if (end >= 0) name = name[..end];

            var index = i;
            files.Add(new CarriedFile(name, folder, into => Copy(com, index, size, into)));
        }
        return files;
    }

    private static void Copy(ComDataObject data, int index, long? size, Stream into)
    {
        var wanted = new FORMATETC
        {
            cfFormat = unchecked((short)System.Windows.DataFormats.GetDataFormat("FileContents").Id),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex   = index,
            ptd      = IntPtr.Zero,
            tymed    = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
        };
        data.GetData(ref wanted, out var medium);
        try
        {
            switch (medium.tymed)
            {
                case TYMED.TYMED_ISTREAM: CopyStream(medium.unionmember, into); break;
                case TYMED.TYMED_HGLOBAL: CopyMemory(medium.unionmember, size, into); break;
                default: throw new IOException($"The file was handed over as {medium.tymed}, which is neither a stream nor memory.");
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static void CopyStream(IntPtr pointer, Stream into)
    {
        var source = (IStream)Marshal.GetObjectForIUnknown(pointer);
        var buffer = new byte[128 * 1024];
        var read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (true)
            {
                Marshal.WriteInt32(read, 0);
                source.Read(buffer, buffer.Length, read);
                var got = Marshal.ReadInt32(read);
                if (got <= 0) break;
                into.Write(buffer, 0, got);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(read);
        }
    }

    private static void CopyMemory(IntPtr handle, long? size, Stream into)
    {
        // The block is rounded up, so the size the table gives is the file's where it gives one.
        var held = (long)GlobalSize(handle);
        var left = size is { } told ? Math.Min(told, held) : held;

        var start = GlobalLock(handle);
        if (start == IntPtr.Zero) throw new IOException("The dropped file's content could not be read.");
        try
        {
            var buffer = new byte[(int)Math.Min(left, 1024 * 1024)];
            for (long at = 0; at < left;)
            {
                var take = (int)Math.Min(left - at, buffer.Length);
                Marshal.Copy(new IntPtr(start.ToInt64() + at), buffer, 0, take);
                into.Write(buffer, 0, take);
                at += take;
            }
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);
}
