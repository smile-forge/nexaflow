using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>
/// Every edit made to one photo, as data. The recipe is applied each time the photo is shown, so the file on disk
/// is never touched until it is saved.
/// </summary>
/// <remarks>
/// A fixed set of sections rather than a list of operations: the pipeline runs in one order whatever order the edits
/// were made in, so re-editing a crop made an hour ago changes one value instead of replaying a history. Every
/// coordinate is in <b>oriented-source pixels</b> — the full-resolution image with its EXIF orientation applied —
/// so a recipe means the same thing at any preview size.
/// </remarks>
public sealed record ImageRecipe
{
    /// <summary>The format version this build writes. A recipe from a newer build is left alone, never rewritten.</summary>
    public const int CurrentVersion = 1;

    public static ImageRecipe Identity { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The image the recipe was made against; a mismatch means the photo was changed by something else.</summary>
    public SourceFingerprint? Source { get; init; }

    public RecipeGeometry Geometry { get; init; } = RecipeGeometry.Identity;

    public RecipeAdjustments Adjustments { get; init; } = RecipeAdjustments.Identity;

    /// <summary>Annotations, painted in list order over the adjusted image.</summary>
    /// <remarks>Never default: a recipe read from a file that names no markup has none, not a missing list.</remarks>
    public ImmutableArray<MarkupElement> Markup
    {
        get => _markup.IsDefault ? [] : _markup;
        init => _markup = value;
    }

    private readonly ImmutableArray<MarkupElement> _markup = [];

    /// <summary>The output size; null keeps the cropped size.</summary>
    public RecipeResize? Resize { get; init; }

    public RecipeExport Export { get; init; } = RecipeExport.Default;

    /// <summary>True when applying the recipe changes nothing — such a recipe is not worth a file.</summary>
    [JsonIgnore]
    public bool IsIdentity =>
        Geometry.IsIdentity && Adjustments.IsIdentity && Markup.IsDefaultOrEmpty && Resize is null && Export == RecipeExport.Default;

    /// <summary>True when the pixels differ from the source — what the export settings alone never do.</summary>
    [JsonIgnore]
    public bool ChangesPixels => !Geometry.IsIdentity || !Adjustments.IsIdentity || !Markup.IsDefaultOrEmpty || Resize is not null;

    public bool Equals(ImageRecipe? other) =>
        other is not null
        && Version == other.Version
        && Equals(Source, other.Source)
        && Geometry == other.Geometry
        && Adjustments == other.Adjustments
        && Markup.AsSpan().SequenceEqual(other.Markup.AsSpan())
        && Resize == other.Resize
        && Export == other.Export;

    public override int GetHashCode() => HashCode.Combine(Version, Source, Geometry, Adjustments, Markup.Length, Resize, Export);
}

/// <summary>Where the image goes on the way out: straighten, crop, quarter turns and flips.</summary>
/// <remarks>
/// The crop lives in the <b>straightened frame</b> — oriented-source pixels rotated by <see cref="StraightenDegrees"/>
/// about the image centre — so it is an ordinary rectangle at any angle. Turns and flips act on the cropped result as
/// it is displayed, which is why aspect presets are read in that orientation.
/// </remarks>
public sealed record RecipeGeometry
{
    public const double MaxStraightenDegrees = 45;

    public static RecipeGeometry Identity { get; } = new();

    /// <summary>Clockwise fine rotation, −45…45.</summary>
    public double StraightenDegrees { get; init; }

    /// <summary>The kept region in the straightened frame; null keeps everything (inset to the straightened image).</summary>
    public CropRect? Crop { get; init; }

    /// <summary>The aspect the crop frame was last constrained to — remembered for the next crop session.</summary>
    public AspectPreset Aspect { get; init; } = AspectPreset.Free;

    /// <summary>True when a non-square preset is held in portrait rather than landscape.</summary>
    public bool AspectPortrait { get; init; }

    /// <summary>Clockwise quarter turns, 0…3.</summary>
    public int QuarterTurns { get; init; }

    /// <summary>Mirror left-to-right, after the turns.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Mirror top-to-bottom, after the turns.</summary>
    public bool FlipVertical { get; init; }

    [JsonIgnore]
    public bool IsIdentity =>
        StraightenDegrees == 0 && Crop is null && QuarterTurns == 0 && !FlipHorizontal && !FlipVertical;

    /// <summary>Clockwise by a quarter (or counter-clockwise for a negative count), carrying the flips with the picture.</summary>
    public RecipeGeometry Turned(int quarters)
    {
        // A flip after a turn is the same picture as the opposite turn after the flip, so turning a flipped image
        // clockwise on screen is a counter-clockwise turn underneath.
        int signed = FlipHorizontal ^ FlipVertical ? -quarters : quarters;
        return (this with { QuarterTurns = (((QuarterTurns + signed) % 4) + 4) % 4 }).Normalized();
    }

    /// <summary>Mirrors the picture as it is displayed.</summary>
    public RecipeGeometry Flipped(bool horizontal) =>
        (horizontal ? this with { FlipHorizontal = !FlipHorizontal } : this with { FlipVertical = !FlipVertical }).Normalized();

    /// <summary>One spelling per picture: both flips together are a half turn.</summary>
    public RecipeGeometry Normalized() => FlipHorizontal && FlipVertical
        ? this with { FlipHorizontal = false, FlipVertical = false, QuarterTurns = (QuarterTurns + 2) % 4 }
        : this;
}

/// <summary>A rectangle in the straightened frame, in full-resolution oriented-source pixels.</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height)
{
    [JsonIgnore] public double Right  => X + Width;
    [JsonIgnore] public double Bottom => Y + Height;
    [JsonIgnore] public double CentreX => X + Width / 2;
    [JsonIgnore] public double CentreY => Y + Height / 2;

    public static CropRect FromEdges(double left, double top, double right, double bottom) =>
        new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));
}

/// <summary>Crop frame constraints offered in the editor.</summary>
public enum AspectPreset
{
    Free,
    Original,
    Square,
    FourThree,
    SixteenNine,
}

/// <summary>Tone and colour. Every value is 0 at identity; ranges are −100…100 except exposure, in stops (−3…3).</summary>
public sealed record RecipeAdjustments
{
    public const double ExposureRange = 3;
    public const double SliderRange = 100;

    public static RecipeAdjustments Identity { get; } = new();

    public double Exposure    { get; init; }
    public double Contrast    { get; init; }
    public double Highlights  { get; init; }
    public double Shadows     { get; init; }
    public double Saturation  { get; init; }
    public double Temperature { get; init; }
    public double Tint        { get; init; }

    [JsonIgnore]
    public bool IsIdentity =>
        Exposure == 0 && Contrast == 0 && Highlights == 0 && Shadows == 0 && Saturation == 0 && Temperature == 0 && Tint == 0;
}

/// <summary>How the output size is given.</summary>
public enum ResizeMode
{
    Percent,
    Pixels,
}

/// <summary>
/// The output size. A percentage follows the crop; pixels are held as typed, and with <see cref="LockAspect"/> only
/// the width is — the height follows the crop's aspect, so re-cropping never distorts.
/// </summary>
public sealed record RecipeResize
{
    public ResizeMode Mode { get; init; } = ResizeMode.Percent;
    public double Percent { get; init; } = 100;
    public int Width { get; init; }
    public int Height { get; init; }
    public bool LockAspect { get; init; } = true;
}

/// <summary>What a save writes besides the pixels.</summary>
public sealed record RecipeExport
{
    public const int DefaultJpegQuality = 92;

    public static RecipeExport Default { get; } = new();

    /// <summary>Drop EXIF, XMP and IPTC on save. The colour profile always stays, so colours stay right.</summary>
    public bool StripMetadata { get; init; }

    public int JpegQuality { get; init; } = DefaultJpegQuality;
}
