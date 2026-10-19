using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System.Globalization;

namespace Nexaflow.Features.Images.Details;

/// <summary>One heading of the Details panel and the facts under it.</summary>
public sealed record DetailsSection(string Title, IReadOnlyList<DetailsRow> Rows);

/// <summary>One fact: what it is, and what the picture says it is.</summary>
public sealed record DetailsRow(string Label, string Value);

/// <summary>
/// <see cref="PhotoDetails"/> laid out as the Details panel reads them — file, camera, exposure, when, where, rights,
/// colour — each fact in words, and a heading only where the picture has something to put under it.
/// </summary>
public static class PhotoDetailsSections
{
    /// <param name="original">The picture's size as it stands, upright, once it is decoded.</param>
    /// <param name="saved">The size it is saved at with its edits, where that differs.</param>
    public static IReadOnlyList<DetailsSection> Build(PhotoDetails details, SKSizeI? original, SKSizeI? saved)
    {
        var camera = details.Camera;
        var sections = new List<DetailsSection>();

        Add(sections, "Images.Details.Section.File",
            Row("Images.Details.Name", details.FileName),
            Row("Images.Details.Type", details.Container.ToString().ToUpperInvariant()),
            Row("Images.Details.Size", SizeFormatter.FormatBytes(details.FileSize)),
            Row("Images.Details.Dimensions", original is { } size ? Dimensions(size) : null),
            Row("Images.Details.SavedSize", saved is { } edited && edited != original ? Dimensions(edited) : null),
            Row("Images.Details.Modified", details.Modified?.ToString("g", CultureInfo.CurrentCulture)));

        Add(sections, "Images.Details.Section.Camera",
            Row("Images.Details.Camera", CameraName(camera.Make, camera.Model)),
            Row("Images.Details.Lens", camera.Lens),
            Row("Images.Details.Software", camera.Software));

        Add(sections, "Images.Details.Section.Exposure",
            Row("Images.Details.Aperture", camera.FNumber is { } f ? Str.Format("Images.Details.ApertureFormat", f) : null),
            Row("Images.Details.Shutter", camera.ExposureSeconds is { } seconds ? Shutter(seconds) : null),
            Row("Images.Details.Iso", camera.Iso?.ToString(CultureInfo.CurrentCulture)),
            Row("Images.Details.FocalLength", Focal(camera.FocalLength, camera.FocalLength35mm)),
            Row("Images.Details.Bias", camera.ExposureBias is { } bias ? Str.Format("Images.Details.BiasFormat", bias) : null),
            Row("Images.Details.Flash", camera.FlashFired is { } fired ? Str.Get(fired ? "Images.Details.FlashFired" : "Images.Details.FlashNotFired") : null));

        Add(sections, "Images.Details.Section.Taken",
            Row("Images.Details.Date", Taken(camera.Taken, camera.TakenOffset)));

        if (camera.Location is { } place)
            Add(sections, "Images.Details.Section.Location",
                Row("Images.Details.Position", Position(place)),
                Row("Images.Details.Altitude", place.Altitude is { } metres
                    ? Str.Format(metres < 0 ? "Images.Details.AltitudeBelowFormat" : "Images.Details.AltitudeAboveFormat", Math.Abs(metres))
                    : null));

        Add(sections, "Images.Details.Section.Rights",
            Row("Images.Details.Description", camera.Description),
            Row("Images.Details.Artist", camera.Artist),
            Row("Images.Details.Copyright", camera.Copyright));

        Add(sections, "Images.Details.Section.Colour",
            Row("Images.Details.Profile", details.ColourProfile));

        return sections;
    }

    private static void Add(List<DetailsSection> sections, string title, params DetailsRow?[] rows)
    {
        var present = rows.OfType<DetailsRow>().ToList();
        if (present.Count > 0) sections.Add(new DetailsSection(Str.Get(title), present));
    }

    private static DetailsRow? Row(string label, string? value) => value is null ? null : new DetailsRow(Str.Get(label), value);

    private static string Dimensions(SKSizeI size) =>
        Str.Format("Images.Details.DimensionsFormat", size.Width, size.Height, size.Width * (double)size.Height / 1_000_000);

    /// <summary>The model, led by the maker unless the model's name already starts with it ("Canon EOS R6", not "Canon Canon EOS R6").</summary>
    private static string? CameraName(string? make, string? model) => (make, model) switch
    {
        (null, null)                                                          => null,
        (null, _)                                                             => model,
        (_, null)                                                             => make,
        _ when model!.StartsWith(make!, StringComparison.OrdinalIgnoreCase)   => model,
        _                                                                     => $"{make} {model}",
    };

    /// <summary>A fraction of a second as photographers write it (1/250 s), a second or more as seconds.</summary>
    private static string Shutter(double seconds) => seconds >= 1
        ? Str.Format("Images.Details.ShutterSecondsFormat", seconds)
        : Str.Format("Images.Details.ShutterFractionFormat", Math.Round(1 / seconds));

    private static string? Focal(double? focal, int? equivalent) => (focal, equivalent) switch
    {
        ({ } mm, { } film) when Math.Abs(mm - film) >= 0.5 => Str.Format("Images.Details.FocalEquivalentFormat", mm, film),
        ({ } mm, _)                                         => Str.Format("Images.Details.FocalFormat", mm),
        (null, { } film)                                    => Str.Format("Images.Details.FocalFilmFormat", film),
        _                                                   => null,
    };

    private static string? Taken(DateTime? taken, TimeSpan? offset)
    {
        if (taken is not { } when) return null;
        var stamp = when.ToString("g", CultureInfo.CurrentCulture);
        if (offset is not { } span) return stamp;
        var utc = (span < TimeSpan.Zero ? "-" : "+") + span.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        return Str.Format("Images.Details.OffsetFormat", stamp, utc);
    }

    private static string Position(GeoPosition place) => Str.Format("Images.Details.PositionFormat",
        Math.Abs(place.Latitude), Str.Get(place.Latitude < 0 ? "Images.Details.South" : "Images.Details.North"),
        Math.Abs(place.Longitude), Str.Get(place.Longitude < 0 ? "Images.Details.West" : "Images.Details.East"));
}
