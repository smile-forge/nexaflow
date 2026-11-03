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
    /// rewritten. Version 2 added <see cref="Restore"/>, version 3 <see cref="CutOut"/>, version 4 <see cref="Erasures"/> and
    /// version 5 a <see cref="RecipeRegion"/> on a cut-out and an erasing step, and version 6 what the picture's scene depth
    /// draws — <see cref="LensBlur"/>, <see cref="DepthHaze"/> and a region step by distance (<see cref="RegionSceneDepth"/>);
    /// a recipe is written as the oldest version that holds what is in it, so a build from before reads every recipe it can.</summary>
    public const int CurrentVersion = 6;

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

    /// <summary>A lens's blur drawn from the picture's scene depth, after the picture tools and before tone; null leaves it sharp.</summary>
    public RecipeLensBlur? LensBlur { get; init; }

    /// <summary>Haze laid over the picture by its scene depth, before its lens blur; null leaves the air clear.</summary>
    public RecipeDepthHaze? DepthHaze { get; init; }

    /// <summary>Whether it draws anything from the picture's scene depth — what the picture's depth is estimated for.</summary>
    [JsonIgnore]
    public bool AsksSceneDepth => LensBlur is not null || DepthHaze is not null;

    /// <summary>The version a file of this recipe is written as: the oldest that holds everything in it.</summary>
    [JsonIgnore]
    public int WrittenVersion => AsksSceneDepth || SelectsByDistance ? 6 : UsesRegion ? 5 : !Erasures.IsEmpty ? 4 : CutOut is not null ? 3 : Restore is not null ? 2 : 1;

    /// <summary>Whether anything in it selects by a region drawn by hand.</summary>
    private bool UsesRegion => Regions.Any();

    /// <summary>Whether a region in it selects by distance.</summary>
    [JsonIgnore]
    public bool SelectsByDistance => Regions.Any(r => r.SelectsByDistance);

    private IEnumerable<RecipeRegion> Regions
        => new[] { CutOut?.Region }.Concat(Erasures.SelectMany(e => new[] { e.Region, e.Pick?.Region })).OfType<RecipeRegion>();

    /// <summary>True when applying the recipe changes nothing — such a recipe is not worth a file.</summary>
    [JsonIgnore]
    public bool IsIdentity =>
        Geometry.IsIdentity && Adjustments.IsIdentity && Markup.IsDefaultOrEmpty && Resize is null && Restore is null && CutOut is null
        && Erasures.IsEmpty && !AsksSceneDepth && Export == RecipeExport.Default;

    /// <summary>True when the pixels differ from the source — what the export settings alone never do.</summary>
    [JsonIgnore]
    public bool ChangesPixels => !Geometry.IsIdentity || !Adjustments.IsIdentity || !Markup.IsDefaultOrEmpty || Resize is not null || Restore is not null
                                 || CutOut is not null || !Erasures.IsEmpty || AsksSceneDepth;

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
        && LensBlur == other.LensBlur
        && DepthHaze == other.DepthHaze
        && Export == other.Export;

    public override int GetHashCode()
        => HashCode.Combine(HashCode.Combine(Version, Source, Geometry, Adjustments, Markup.Length, Resize, Restore, Export), CutOut, Erasures.Length,
                            LensBlur, DepthHaze);
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

/// <summary>The shape a lens's aperture gives what it blurs — a highlight out of focus spreads into it.</summary>
public enum BokehShape
{
    /// <summary>A round aperture: a disc.</summary>
    Circle,

    /// <summary>Six blades: a hexagon.</summary>
    Hexagon,
}

/// <summary>
/// A lens's blur drawn from the picture's scene depth: what lies from <see cref="Near"/> to <see cref="Far"/> — on the picture's
/// distance (<c>SceneDepthPlane.Distance</c>), 0 its nearest part, 1 a thousand times as far — is sharp, and what lies nearer
/// or farther blurs the more the farther out of that band it is.
/// </summary>
public sealed record RecipeLensBlur
{
    /// <summary>The widest blur a fully open aperture gives what lies farthest out of the band — the nearest part or the
    /// farthest, wherever the band is — as a share of the picture's longer side.</summary>
    public const double WidestBlur = 0.03;

    /// <summary>The narrowest band of distance a focus keeps sharp: a band no wider is a plane, and still sharp to the eye.</summary>
    public const double NarrowestBand = 0.01;

    /// <summary>Where the band kept sharp begins, 0…1 of the picture's distance.</summary>
    public double Near { get; init; }

    /// <summary>Where the band kept sharp ends, <see cref="Near"/>…1 of the picture's distance.</summary>
    public double Far { get; init; } = 0.1;

    /// <summary>How wide the aperture is, 0…1: what lies farthest out of the band is blurred by this share of
    /// <see cref="WidestBlur"/>, and the rest by as much less as it is nearer the band.</summary>
    public double Aperture { get; init; } = 0.5;

    public BokehShape Shape { get; init; } = BokehShape.Circle;

    /// <summary>This blur kept sharp from <paramref name="near"/> to <paramref name="far"/>, either way round, held to 0…1 and at
    /// least <see cref="NarrowestBand"/> wide.</summary>
    public RecipeLensBlur Banded(double near, double far)
    {
        double from = Math.Clamp(Math.Min(near, far), 0, 1), to = Math.Clamp(Math.Max(near, far), 0, 1);
        if (to - from < NarrowestBand)
        {
            double middle = Math.Clamp((from + to) / 2, NarrowestBand / 2, 1 - NarrowestBand / 2);
            (from, to) = (middle - NarrowestBand / 2, middle + NarrowestBand / 2);
        }
        return this with { Near = from, Far = to };
    }
}

/// <summary>
/// Haze laid over the picture by its scene depth, as air lays it over distance: what is nearer than <see cref="Start"/> stays
/// clear, and beyond it each part fades into the haze's colour the farther it is, as thick as <see cref="Density"/> says.
/// </summary>
public sealed record RecipeDepthHaze
{
    /// <summary>How thick the haze is, 0…1: at 1 the farthest part keeps under 2 % of its own light.</summary>
    public double Density { get; init; } = 0.4;

    /// <summary>Where it starts, 0…1 from the nearest to the farthest part of the picture — to begin with, a quarter of the way,
    /// so what is near the camera stays clear.</summary>
    public double Start { get; init; } = 0.25;

    /// <summary>The haze's colour, 32-bit ARGB as markup's ink is; null takes the colour of the picture's distance — its sky's,
    /// or its far hills' — so the haze is the air the picture already has.</summary>
    public uint? Colour { get; init; }
}

/// <summary>A click on the photo for its cut-out, in oriented-source pixels: on the thing kept, or off it.</summary>
public sealed record RecipeCutClick(double X, double Y, bool Include);

/// <summary>A box on the photo for its cut-out, in oriented-source pixels.</summary>
public sealed record RecipeCutBox(double Left, double Top, double Right, double Bottom);

/// <summary>
/// The photo cut out of its background: only what is selected is kept, the rest made clear — what picture selection picks
/// by clicks on the thing and off it and a box round it, or everything a phrase names, then the <see cref="Region"/> drawn
/// by hand over that. Kept as what was asked rather than as the mask, so it reads the same at any size and is picked again
/// by newer models.
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

    /// <summary>The selection drawn by hand, starting from what picture selection picks — or from nothing, where it is asked
    /// for nothing; null where none was drawn.</summary>
    public RecipeRegion? Region { get; init; }

    /// <summary>Whether picture selection is asked to pick anything: a phrase, a click or a box.</summary>
    [JsonIgnore]
    public bool AsksPictureSelection => Phrase is not null || !Clicks.IsEmpty || Box is not null;

    public bool Equals(RecipeCutOut? other) =>
        other is not null && Clicks.AsSpan().SequenceEqual(other.Clicks.AsSpan()) && Box == other.Box && Phrase == other.Phrase && Region == other.Region;

    public override int GetHashCode() => HashCode.Combine(Clicks.Length, Box, Phrase, Region);

    /// <summary>The cut-out as one line of text, the same for the same cut-out: what its kept mask is filed by.</summary>
    public string Describe()
    {
        var picked = Phrase is { } phrase
            ? $"phrase:{phrase}"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"clicks:{string.Join(";", Clicks.Select(c => $"{c.X:R},{c.Y:R},{(c.Include ? 1 : 0)}"))}|box:{(Box is { } b ? $"{b.Left:R},{b.Top:R},{b.Right:R},{b.Bottom:R}" : "")}");
        return Region is { } region ? $"{picked}|region:{region.Describe()}" : picked;
    }
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
/// One step of erasing: what it erases — a thing picked as the cut-out tool picks it (<see cref="Pick"/>), a selection
/// drawn by hand over that (<see cref="Region"/>), brush strokes over both, or any of them — and how the place is filled.
/// A step erases from the photo as the steps before it left it.
/// </summary>
public sealed record RecipeErase
{
    public EraseFill Fill { get; init; }

    /// <summary>The thing picked to erase, by clicks and a box or by a phrase; null where only strokes mark it.</summary>
    public RecipeCutOut? Pick { get; init; }

    /// <summary>The selection drawn by hand, starting from what is picked, grown past its edge — or from nothing; null where
    /// none was drawn.</summary>
    public RecipeRegion? Region { get; init; }

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
    public bool MarksAnything => Pick is not null || Region is { IsEmpty: false } || Strokes.Any(s => s.Marks && !s.Points.IsEmpty);

    public bool Equals(RecipeErase? other)
        => other is not null && Fill == other.Fill && Pick == other.Pick && Region == other.Region && HealFrom == other.HealFrom
           && Strokes.AsSpan().SequenceEqual(other.Strokes.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Fill, Pick, Region, Strokes.Length, HealFrom);

    /// <summary>The step as one line of text, the same for the same step: what what it makes is filed by.</summary>
    public string Describe()
    {
        var step = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"fill:{Fill}|pick:{Pick?.Describe()}|strokes:{string.Join("/", Strokes.Select(s => s.Describe()))}|from:{(HealFrom is { } f ? $"{f.X:R},{f.Y:R}" : "")}");
        return Region is { } region ? $"{step}|region:{region.Describe()}" : step;
    }
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
