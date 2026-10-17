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
    /// <summary>The newest format version this build reads and writes. A recipe from a newer build is left alone, never
    /// rewritten. Version 2 added <see cref="Restore"/>, version 3 <see cref="CutOut"/> and version 4 <see cref="Erasures"/>;
    /// a recipe is written as the oldest version that holds what is in it, so a build from before reads every recipe it can.</summary>
    public const int CurrentVersion = 4;

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

    /// <summary>The picture restored by the picture tools before anything else is done to it; null leaves it as it is.</summary>
    public RecipeRestore? Restore { get; init; }

    /// <summary>The photo cut out of its background — only what this picks kept — before anything else is done to it;
    /// null keeps the whole photo.</summary>
    public RecipeCutOut? CutOut { get; init; }

    /// <summary>What is erased from the photo, a step at a time in list order, before anything else is done to it —
    /// before it is restored too, so a restoration is of the photo as erased.</summary>
    /// <remarks>Never default: a recipe read from a file that names no erasures has none, not a missing list.</remarks>
    public ImmutableArray<RecipeErase> Erasures
    {
        get => _erasures.IsDefault ? [] : _erasures;
        init => _erasures = value;
    }

    private readonly ImmutableArray<RecipeErase> _erasures = [];

    /// <summary>The version a file of this recipe is written as: the oldest that holds everything in it.</summary>
    [JsonIgnore]
    public int WrittenVersion => !Erasures.IsEmpty ? 4 : CutOut is not null ? 3 : Restore is not null ? 2 : 1;

    /// <summary>True when applying the recipe changes nothing — such a recipe is not worth a file.</summary>
    [JsonIgnore]
    public bool IsIdentity =>
        Geometry.IsIdentity && Adjustments.IsIdentity && Markup.IsDefaultOrEmpty && Resize is null && Restore is null && CutOut is null
        && Erasures.IsEmpty && Export == RecipeExport.Default;

    /// <summary>True when the pixels differ from the source — what the export settings alone never do.</summary>
    [JsonIgnore]
    public bool ChangesPixels => !Geometry.IsIdentity || !Adjustments.IsIdentity || !Markup.IsDefaultOrEmpty || Resize is not null || Restore is not null
                                 || CutOut is not null || !Erasures.IsEmpty;

    public bool Equals(ImageRecipe? other) =>
        other is not null
        && Version == other.Version
        && Equals(Source, other.Source)
        && Geometry == other.Geometry
        && Adjustments == other.Adjustments
        && Markup.AsSpan().SequenceEqual(other.Markup.AsSpan())
        && Resize == other.Resize
        && Restore == other.Restore
        && CutOut == other.CutOut
        && Erasures.AsSpan().SequenceEqual(other.Erasures.AsSpan())
        && Export == other.Export;

    public override int GetHashCode()
        => HashCode.Combine(HashCode.Combine(Version, Source, Geometry, Adjustments, Markup.Length, Resize, Restore, Export), CutOut, Erasures.Length);
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

/// <summary>
/// The picture restored by the picture tools before any other edit: made <see cref="Scale"/> times its size by Real-ESRGAN,
/// or kept at its size and cleaned of softness, noise and JPEG blocks, its faces remade where <see cref="Faces"/> — then
/// laid over the picture as it was at <see cref="Strength"/>. The restored picture is the same picture larger: every other
/// section still speaks of the source's pixels, so a crop or a note made before stays where it was.
/// </summary>
public sealed record RecipeRestore
{
    /// <summary>The sizes a picture is restored at: kept at its size, twice, four times.</summary>
    public static IReadOnlyList<int> Scales { get; } = [1, 2, 4];

    public int Scale { get; init; } = 1;

    /// <summary>How much of the restored picture shows, 0…1, over the picture as it was.</summary>
    public double Strength { get; init; } = 1;

    public bool Faces { get; init; } = true;

    /// <summary>The same restored picture as <paramref name="other"/>, whatever strength each lays it at.</summary>
    public bool MakesSameAs(RecipeRestore? other) => other is not null && Scale == other.Scale && Faces == other.Faces;
}

/// <summary>A click on the photo for its cut-out, in oriented-source pixels: on the thing kept, or off it.</summary>
public sealed record RecipeCutClick(double X, double Y, bool Include);

/// <summary>A box on the photo for its cut-out, in oriented-source pixels.</summary>
public sealed record RecipeCutBox(double Left, double Top, double Right, double Bottom);

/// <summary>
/// The photo cut out of its background: only what picture selection picks by these is kept, the rest made clear — by
/// clicks on the thing and off it and a box round it, or everything a phrase names. Kept as what was asked rather than as
/// the mask, so it reads the same at any size and is picked again by newer models.
/// </summary>
public sealed record RecipeCutOut
{
    /// <summary>The clicks, in the order they were made.</summary>
    /// <remarks>Never default: a recipe read from a file that names no clicks has none, not a missing list.</remarks>
    public ImmutableArray<RecipeCutClick> Clicks
    {
        get => _clicks.IsDefault ? [] : _clicks;
        init => _clicks = value;
    }

    private readonly ImmutableArray<RecipeCutClick> _clicks = [];

    /// <summary>The box drawn round the thing; null where none was.</summary>
    public RecipeCutBox? Box { get; init; }

    /// <summary>What the kept things are called — everything it names is kept — instead of clicks and a box.</summary>
    public string? Phrase { get; init; }

    public bool Equals(RecipeCutOut? other) =>
        other is not null && Clicks.AsSpan().SequenceEqual(other.Clicks.AsSpan()) && Box == other.Box && Phrase == other.Phrase;

    public override int GetHashCode() => HashCode.Combine(Clicks.Length, Box, Phrase);

    /// <summary>The cut-out as one line of text, the same for the same cut-out: what its kept mask is filed by.</summary>
    public string Describe()
        => Phrase is { } phrase
            ? $"phrase:{phrase}"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"clicks:{string.Join(";", Clicks.Select(c => $"{c.X:R},{c.Y:R},{(c.Include ? 1 : 0)}"))}|box:{(Box is { } b ? $"{b.Left:R},{b.Top:R},{b.Right:R},{b.Bottom:R}" : "")}");
}

/// <summary>How an erased place is filled.</summary>
public enum EraseFill
{
    /// <summary>By picture erasing, a neural network (LaMa): best where what is erased crosses edges and shapes.</summary>
    Ai,

    /// <summary>With the photo's own patches (content-aware fill): sharp copied texture, nothing to download.</summary>
    ContentAware,

    /// <summary>With texture copied from <see cref="RecipeErase.HealFrom"/> away, made to meet the colour and light round
    /// it (the healing brush).</summary>
    Heal,
}

/// <summary>A brush stroke over the photo: round, <see cref="Width"/> source pixels across, through <see cref="Points"/>.
/// It marks what to erase, or — where <see cref="Marks"/> is false — takes marking back.</summary>
public sealed record RecipeEraseStroke
{
    /// <remarks>Never default: a stroke read from a file that names no points has none.</remarks>
    public ImmutableArray<MarkupPoint> Points
    {
        get => _points.IsDefault ? [] : _points;
        init => _points = value;
    }

    private readonly ImmutableArray<MarkupPoint> _points = [];

    public double Width { get; init; }

    public bool Marks { get; init; } = true;

    public bool Equals(RecipeEraseStroke? other)
        => other is not null && Width == other.Width && Marks == other.Marks && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Points.Length, Width, Marks);

    /// <summary>The stroke as text, the same for the same stroke.</summary>
    public string Describe()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(Marks ? '+' : '-')}{Width:R}:{string.Join(";", Points.Select(p => $"{p.X:R},{p.Y:R}"))}");
}

/// <summary>
/// One step of erasing: what it erases — a thing picked as the cut-out tool picks it (<see cref="Pick"/>), brush
/// strokes, or both — and how the place is filled. A step erases from the photo as the steps before it left it.
/// </summary>
public sealed record RecipeErase
{
    public EraseFill Fill { get; init; }

    /// <summary>The thing picked to erase, by clicks and a box or by a phrase; null where only strokes mark it.</summary>
    public RecipeCutOut? Pick { get; init; }

    /// <summary>The strokes, in the order they were made, over what is picked.</summary>
    /// <remarks>Never default: a step read from a file that names no strokes has none.</remarks>
    public ImmutableArray<RecipeEraseStroke> Strokes
    {
        get => _strokes.IsDefault ? [] : _strokes;
        init => _strokes = value;
    }

    private readonly ImmutableArray<RecipeEraseStroke> _strokes = [];

    /// <summary>For <see cref="EraseFill.Heal"/>: how far, in source pixels, each healed pixel's texture is copied from.</summary>
    public MarkupPoint? HealFrom { get; init; }

    /// <summary>Whether the step marks anything to erase.</summary>
    [JsonIgnore]
    public bool MarksAnything => Pick is not null || Strokes.Any(s => s.Marks && !s.Points.IsEmpty);

    public bool Equals(RecipeErase? other)
        => other is not null && Fill == other.Fill && Pick == other.Pick && HealFrom == other.HealFrom
           && Strokes.AsSpan().SequenceEqual(other.Strokes.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Fill, Pick, Strokes.Length, HealFrom);

    /// <summary>The step as one line of text, the same for the same step: what what it makes is filed by.</summary>
    public string Describe()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"fill:{Fill}|pick:{Pick?.Describe()}|strokes:{string.Join("/", Strokes.Select(s => s.Describe()))}|from:{(HealFrom is { } f ? $"{f.X:R},{f.Y:R}" : "")}");
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
