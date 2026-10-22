using Nexaflow.Features.Images.Editing.Metadata;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nexaflow.Features.Images.Details;

/// <summary>
/// What a camera wrote into a photo's EXIF block: what took it, how it was exposed, when, where, and who it belongs
/// to. Every fact is optional — a field the camera left out, or wrote as nothing, is null.
/// </summary>
public sealed record CameraFacts
{
    public static CameraFacts None { get; } = new();

    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Lens { get; init; }
    public string? Software { get; init; }
    public string? Artist { get; init; }
    public string? Copyright { get; init; }
    public string? Description { get; init; }

    /// <summary>The aperture as an f-number: 2.8 for ƒ/2.8.</summary>
    public double? FNumber { get; init; }

    /// <summary>How long the shutter was open, in seconds.</summary>
    public double? ExposureSeconds { get; init; }

    public int? Iso { get; init; }

    /// <summary>The lens's focal length in millimetres, and what it equals on 35 mm film.</summary>
    public double? FocalLength { get; init; }
    public int? FocalLength35mm { get; init; }

    public bool? FlashFired { get; init; }

    /// <summary>Exposure compensation, in stops.</summary>
    public double? ExposureBias { get; init; }

    /// <summary>When the picture was taken, by the camera's clock, and that clock's offset from UTC where it says.</summary>
    public DateTime? Taken { get; init; }
    public TimeSpan? TakenOffset { get; init; }

    public GeoPosition? Location { get; init; }

    /// <summary>True when any fact about the shot itself is here — not just a program's name.</summary>
    public bool DescribesTheShot =>
        Make is not null || Model is not null || Lens is not null || FNumber is not null || ExposureSeconds is not null
        || Iso is not null || FocalLength is not null || Taken is not null || Location is not null;
}

/// <summary>Where a photo was taken: degrees north and east (negative south and west), and metres above sea level.</summary>
public sealed record GeoPosition(double Latitude, double Longitude, double? Altitude);

/// <summary>
/// Reads <see cref="CameraFacts"/> out of an EXIF block — the TIFF-shaped body <see cref="MetadataTransfer"/> lifts out
/// of a JPEG, PNG, WebP or TIFF — through its first directory and the EXIF and GPS directories it points to.
/// </summary>
public static class ExifFacts
{
    // The first directory.
    private const ushort ImageDescription = 0x010E, Make = 0x010F, Model = 0x0110, Software = 0x0131, Artist = 0x013B, Copyright = 0x8298;

    // The EXIF directory.
    private const ushort ExposureTime = 0x829A, FNumber = 0x829D, IsoSpeed = 0x8827, DateTimeOriginal = 0x9003, OffsetTimeOriginal = 0x9011,
                         ExposureBias = 0x9204, Flash = 0x9209, FocalLength = 0x920A, FocalLength35mm = 0xA405, LensModel = 0xA434;

    // The GPS directory.
    private const ushort LatitudeRef = 1, Latitude = 2, LongitudeRef = 3, Longitude = 4, AltitudeRef = 5, Altitude = 6;

    public static CameraFacts Read(byte[]? exif)
    {
        if (exif is null || TiffReader.Open(exif) is not { } reader) return CameraFacts.None;
        if (reader.ReadDirectory(reader.FirstDirectoryOffset, out _, TiffReader.ExifPointer, TiffReader.GpsPointer) is not { } root)
            return CameraFacts.None;

        var values = new TiffValues(reader.BigEndian);
        var shot = root.Children.GetValueOrDefault(TiffReader.ExifPointer) ?? new TiffDirectory();
        var (taken, offset) = When(values.Text(shot.Find(DateTimeOriginal)), values.Text(shot.Find(OffsetTimeOriginal)));

        return new CameraFacts
        {
            Make            = values.Text(root.Find(Make)),
            Model           = values.Text(root.Find(Model)),
            Software        = values.Text(root.Find(Software)),
            Artist          = values.Text(root.Find(Artist)),
            Copyright       = values.Text(root.Find(Copyright)),
            Description     = values.Text(root.Find(ImageDescription)),
            Lens            = values.Text(shot.Find(LensModel)),
            FNumber         = Positive(values.Number(shot.Find(FNumber))),
            ExposureSeconds = Positive(values.Number(shot.Find(ExposureTime))),
            Iso             = values.Whole(shot.Find(IsoSpeed)) is > 0 and var iso ? iso : null,
            FocalLength     = Positive(values.Number(shot.Find(FocalLength))),
            FocalLength35mm = values.Whole(shot.Find(FocalLength35mm)) is > 0 and var focal ? focal : null,
            FlashFired      = values.Whole(shot.Find(Flash)) is { } flash ? (flash & 1) == 1 : null,
            ExposureBias    = values.Number(shot.Find(ExposureBias)),
            Taken           = taken,
            TakenOffset     = offset,
            Location        = root.Children.GetValueOrDefault(TiffReader.GpsPointer) is { } gps ? Where(gps, values) : null,
        };
    }

    private static double? Positive(double? value) => value is > 0 ? value : null;

    /// <summary>EXIF's "YYYY:MM:DD HH:MM:SS", and its "+HH:MM" offset; a clock never set ("0000:00:00 …") is no date.</summary>
    private static (DateTime? Taken, TimeSpan? Offset) When(string? stamp, string? offset)
    {
        if (stamp is null || !DateTime.TryParseExact(stamp, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
            return (null, null);
        if (offset is not { Length: 6 } || offset[0] is not ('+' or '-')
            || !TimeSpan.TryParseExact(offset[1..], @"hh\:mm", CultureInfo.InvariantCulture, out var span))
            return (taken, null);
        return (taken, offset[0] == '-' ? -span : span);
    }

    private static GeoPosition? Where(TiffDirectory gps, TiffValues values)
    {
        if (Degrees(gps.Find(Latitude), values) is not { } latitude || Degrees(gps.Find(Longitude), values) is not { } longitude) return null;
        if (latitude > 90 || longitude > 180) return null;
        if (values.Text(gps.Find(LatitudeRef)) is "S") latitude = -latitude;
        if (values.Text(gps.Find(LongitudeRef)) is "W") longitude = -longitude;

        double? altitude = values.Number(gps.Find(Altitude));
        if (altitude is not null && values.Whole(gps.Find(AltitudeRef)) == 1) altitude = -altitude;
        return new GeoPosition(latitude, longitude, altitude);
    }

    /// <summary>Degrees, minutes and seconds as three rationals, folded into decimal degrees.</summary>
    private static double? Degrees(TiffEntry? entry, TiffValues values)
    {
        if (entry is not { Count: 3 }) return null;
        if (values.Number(entry, 0) is not { } degrees || values.Number(entry, 1) is not { } minutes || values.Number(entry, 2) is not { } seconds)
            return null;
        return degrees + minutes / 60 + seconds / 3600;
    }
}

/// <summary>A TIFF entry's value read as text or as a number, in the byte order of the block it came from.</summary>
internal readonly struct TiffValues(bool bigEndian)
{
    private const ushort SignedLong = 9, SignedRational = 10;

    /// <summary>An ASCII field up to its terminator, trimmed; null when it holds nothing. Cameras write UTF-8 here too,
    /// so it is read as UTF-8, which ASCII is a part of.</summary>
    public string? Text(TiffEntry? entry)
    {
        if (entry is not { Type: TiffEntry.Ascii }) return null;
        var bytes = entry.Value.AsSpan();
        int end = bytes.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The <paramref name="index"/>th value of a numeric field; null for a missing value or a zero denominator.</summary>
    public double? Number(TiffEntry? entry, int index = 0)
    {
        if (entry is null || index >= entry.Count) return null;
        int size = TiffEntry.SizeOf(entry.Type);
        if (size == 0 || (index + 1) * size > entry.Value.Length) return null;
        var at = entry.Value.AsSpan(index * size, size);

        return entry.Type switch
        {
            TiffEntry.Byte     => at[0],
            TiffEntry.Short    => U16(at),
            TiffEntry.Long     => U32(at),
            SignedLong         => (int)U32(at),
            TiffEntry.Rational => U32(at[4..]) is var den and > 0 ? (double)U32(at) / den : null,
            SignedRational     => (int)U32(at[4..]) is var sden and not 0 ? (double)(int)U32(at) / sden : null,
            _                  => null,
        };
    }

    public int? Whole(TiffEntry? entry) => Number(entry) is { } value ? (int)Math.Round(value) : null;

    private ushort U16(ReadOnlySpan<byte> at) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(at) : BinaryPrimitives.ReadUInt16LittleEndian(at);
    private uint U32(ReadOnlySpan<byte> at) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(at) : BinaryPrimitives.ReadUInt32LittleEndian(at);
}
