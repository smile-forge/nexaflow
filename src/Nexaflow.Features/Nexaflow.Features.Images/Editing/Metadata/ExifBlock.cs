using System.Buffers.Binary;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// Operations on an EXIF block (a TIFF structure): marking it upright at a new size, and lifting one out of a TIFF
/// file, whose EXIF is woven through the image's own directory.
/// </summary>
public static class ExifBlock
{
    public const ushort Orientation = 0x0112, ImageWidth = 0x0100, ImageLength = 0x0101;
    public const ushort PixelXDimension = 0xA002, PixelYDimension = 0xA003;

    /// <summary>
    /// The image-directory tags that describe pixels and their storage rather than the photo. Lifting EXIF out of a
    /// TIFF leaves these behind; they belong to whatever writes the pixels next.
    /// </summary>
    internal static readonly HashSet<ushort> StructuralTags =
    [
        254, 255, 256, 257, 258, 259, 262, 263, 264, 265, 266, 273, 274, 277, 278, 279, 280, 281, 284, 317, 320,
        322, 323, 324, 325, 330, 338, 339, 340, 341, 347, 512, 513, 514, 515, 517, 518, 519, 520, 521, 529, 530,
        531, 532, 700, 33723, 34377, 34665, 34675, 34853, 37724,
    ];

    /// <summary>
    /// A copy of <paramref name="exif"/> describing an upright <paramref name="width"/>×<paramref name="height"/>
    /// picture: Orientation set to 1, the pixel dimensions rewritten, and the link to the thumbnail directory cut, so
    /// no reader shows a thumbnail of the unedited picture.
    /// </summary>
    /// <remarks>
    /// Patched in place rather than rebuilt: maker notes hold offsets into the block that a rebuild would break, and
    /// every value changed here is stored inline in its entry.
    /// </remarks>
    public static byte[] Upright(byte[] exif, int width, int height)
    {
        var patched = (byte[])exif.Clone();
        if (TiffReader.Open(patched) is not { } reader) return patched;

        uint first = reader.FirstDirectoryOffset;
        uint? exifDirectory = null;
        foreach (var (tag, type, count, fieldAt) in reader.Fields(first))
        {
            switch (tag)
            {
                case Orientation when count == 1:  WriteInline(patched, reader.BigEndian, type, fieldAt, 1); break;
                case ImageWidth when count == 1:   WriteInline(patched, reader.BigEndian, type, fieldAt, (uint)width); break;
                case ImageLength when count == 1:  WriteInline(patched, reader.BigEndian, type, fieldAt, (uint)height); break;
                case TiffReader.ExifPointer:       exifDirectory = reader.U32(fieldAt); break;
            }
        }

        if (exifDirectory is { } at)
        {
            foreach (var (tag, type, count, fieldAt) in reader.Fields(at))
            {
                if (count != 1) continue;
                if (tag == PixelXDimension) WriteInline(patched, reader.BigEndian, type, fieldAt, (uint)width);
                else if (tag == PixelYDimension) WriteInline(patched, reader.BigEndian, type, fieldAt, (uint)height);
            }
        }

        int link = reader.NextLinkAt(first);
        if (link + 4 <= patched.Length) patched.AsSpan(link, 4).Clear();
        return patched;
    }

    /// <summary>
    /// The EXIF of a TIFF file as a standalone block: the descriptive tags of its first image directory (camera,
    /// dates, resolution, author…) with its EXIF, GPS and interoperability directories, and none of its pixel layout.
    /// Null when the file carries none.
    /// </summary>
    /// <remarks>
    /// The block is rebuilt, so a maker note whose internal offsets count from the start of the file may no longer
    /// resolve for its maker's software; every standard tag survives.
    /// </remarks>
    public static byte[]? FromTiffFile(byte[] tiff)
    {
        if (Descriptive(tiff) is not { } lifted) return null;
        return lifted.Directory.Entries.Count == 0 && lifted.Directory.Children.Count == 0
            ? null
            : new TiffWriter(lifted.BigEndian).Write(lifted.Directory);
    }

    /// <summary>
    /// The first directory of <paramref name="tiffStructure"/> — a TIFF file or an EXIF block — without its
    /// structural tags, with its EXIF and GPS directories, and the byte order its values are stored in.
    /// </summary>
    internal static (TiffDirectory Directory, bool BigEndian)? Descriptive(byte[] tiffStructure)
    {
        if (TiffReader.Open(tiffStructure) is not { } reader) return null;
        var directory = reader.ReadDirectory(reader.FirstDirectoryOffset, out _, TiffReader.ExifPointer, TiffReader.GpsPointer);
        if (directory is null) return null;

        directory.Entries.RemoveAll(e => StructuralTags.Contains(e.Tag));
        return (directory, reader.BigEndian);
    }

    private static void WriteInline(byte[] data, bool bigEndian, ushort type, int fieldAt, uint value)
    {
        var field = data.AsSpan(fieldAt, 4);
        switch (type)
        {
            case TiffEntry.Short:
                if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(field, (ushort)Math.Min(value, ushort.MaxValue));
                else BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)Math.Min(value, ushort.MaxValue));
                break;
            case TiffEntry.Long:
                if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(field, value);
                else BinaryPrimitives.WriteUInt32LittleEndian(field, value);
                break;
        }
    }
}
