using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using Nexaflow.Features.Images.Editing.Selection;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>How a shape drawn for a selection meets what is selected already.</summary>
public enum RegionMode
{
    /// <summary>The shape is the selection; what was selected before goes.</summary>
    Replace,

    /// <summary>The shape joins the selection: the more covered of the two, pixel by pixel.</summary>
    Add,

    /// <summary>The shape is taken out of the selection: what was selected, as much as the shape leaves uncovered.</summary>
    Subtract,

    /// <summary>Only what both cover stays: the less covered of the two, pixel by pixel.</summary>
    Intersect,
}

/// <summary>
/// One step of a selection made by hand: a shape drawn on the photo, or a refinement of everything selected so far.
/// Every length is in oriented-source pixels, as every recipe value is.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RegionRectangle), "rectangle")]
[JsonDerivedType(typeof(RegionEllipse), "ellipse")]
[JsonDerivedType(typeof(RegionPolygon), "polygon")]
[JsonDerivedType(typeof(RegionInvert), "invert")]
[JsonDerivedType(typeof(RegionFeather), "feather")]
[JsonDerivedType(typeof(RegionGrow), "grow")]
[JsonDerivedType(typeof(RegionSmooth), "smooth")]
[JsonDerivedType(typeof(RegionCoverage), "coverage")]
public abstract record RegionStep
{
    /// <summary>The step as text, the same for the same step: part of what a mask made from it is filed by.</summary>
    public abstract string Describe();

    private protected static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>A shape drawn by a marquee or a lasso, and how it meets what is selected already.</summary>
public abstract record RegionShape : RegionStep
{
    public RegionMode Mode { get; init; }

    /// <summary>The middle of the box round the shape: where a step anchored to the shape is anchored.</summary>
    public abstract MarkupPoint Centre { get; }
}

/// <summary>A rectangle drawn by the rectangle marquee, by two opposite corners.</summary>
public sealed record RegionRectangle(double Left, double Top, double Right, double Bottom) : RegionShape
{
    public override MarkupPoint Centre => new((Left + Right) / 2, (Top + Bottom) / 2);

    public override string Describe() => $"rect:{Mode}:{Number(Left)},{Number(Top)},{Number(Right)},{Number(Bottom)}";
}

/// <summary>An ellipse drawn by the ellipse marquee: the one filling the box with these opposite corners.</summary>
public sealed record RegionEllipse(double Left, double Top, double Right, double Bottom) : RegionShape
{
    public override MarkupPoint Centre => new((Left + Right) / 2, (Top + Bottom) / 2);

    public override string Describe() => $"ellipse:{Mode}:{Number(Left)},{Number(Top)},{Number(Right)},{Number(Bottom)}";
}

/// <summary>An outline drawn by the lasso — freehand, or straight from click to click — closed back to its first point.
/// What it encloses is selected, however often the outline crosses itself.</summary>
public sealed record RegionPolygon : RegionShape
{
    /// <remarks>Never default: a polygon read from a file that names no points has none.</remarks>
    public ImmutableArray<MarkupPoint> Points
    {
        get => _points.IsDefault ? [] : _points;
        init => _points = value;
    }

    private readonly ImmutableArray<MarkupPoint> _points = [];

    public override MarkupPoint Centre => Points.IsEmpty ? default
        : new((Points.Min(p => p.X) + Points.Max(p => p.X)) / 2, (Points.Min(p => p.Y) + Points.Max(p => p.Y)) / 2);

    public bool Equals(RegionPolygon? other) => base.Equals(other) && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Points.Length);

    public override string Describe() => $"polygon:{Mode}:{string.Join(";", Points.Select(p => $"{Number(p.X)},{Number(p.Y)}"))}";
}

/// <summary>Everything selected becomes unselected, and everything unselected selected.</summary>
public sealed record RegionInvert : RegionStep
{
    public override string Describe() => "invert";
}

/// <summary>The selection's edge softened: blurred so it fades over about <see cref="Radius"/> pixels each way.</summary>
public sealed record RegionFeather(double Radius) : RegionStep
{
    public override string Describe() => $"feather:{Number(Radius)}";
}

/// <summary>The selection grown outward by <see cref="By"/> pixels, its corners rounded — or, by a negative amount,
/// shrunk inward by as much. The picture's edge is not an edge of the selection: nothing shrinks away from it.</summary>
public sealed record RegionGrow(double By) : RegionStep
{
    public override string Describe() => $"grow:{Number(By)}";
}

/// <summary>The selection's outline smoothed: corners and jagged steps smaller than about <see cref="Radius"/> pixels
/// rounded off, its edge left crisp.</summary>
public sealed record RegionSmooth(double Radius) : RegionStep
{
    public override string Describe() => $"smooth:{Number(Radius)}";
}

/// <summary>
/// A selection made other than by drawing — from a layer's mask or transparency, or carried onto a picture layer from its
/// canvas's selection — as coverage over the oriented source, meeting what is selected as <see cref="Mode"/> says. The
/// coverage is kept in the package holding the recipe, as a grey PNG at <see cref="File"/>; a recipe kept on its own, beside a
/// photo, never holds one, and one whose coverage is not here selects nothing.
/// </summary>
public sealed record RegionCoverage : RegionStep
{
    private string? _hash;

    public RegionMode Mode { get; init; }

    /// <summary>The coverage, over the oriented source; null where it was not read with the recipe.</summary>
    [JsonIgnore]
    public TiledCoverage? Coverage
    {
        get => _coverage;
        init
        {
            _coverage = value;
            _hash = null;
        }
    }

    private readonly TiledCoverage? _coverage;

    /// <summary>Where the package holding the recipe keeps the coverage.</summary>
    public string File { get; init; } = "";

    /// <summary>What the coverage is, the same for the same coverage: a hash of its bytes, worked out once.</summary>
    [JsonIgnore]
    public string Hash => _hash ??= Coverage is { } coverage
        ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(coverage.ToBytes()))[..32] + $"@{coverage.Width}x{coverage.Height}"
        : $"file:{File}";

    public bool Equals(RegionCoverage? other)
        => other is not null && Mode == other.Mode && (ReferenceEquals(Coverage, other.Coverage) || Hash == other.Hash);

    public override int GetHashCode() => HashCode.Combine(Mode, Coverage?.Width, Coverage?.Height);

    public override string Describe() => $"coverage:{Mode}:{Hash}";
}

/// <summary>
/// A selection made by hand on the photo, kept as the steps it was made by, in order — shapes drawn with a marquee or a
/// lasso, each meeting what is selected as its <see cref="RegionShape.Mode"/> says, and refinements of everything selected
/// so far — rather than as a mask, so it reads the same at any size. It starts from what picture selection picked beside
/// it, where anything was; a shape that replaces the selection starts it again.
/// </summary>
public sealed record RecipeRegion
{
    /// <remarks>Never default: a region read from a file that names no steps has none.</remarks>
    public ImmutableArray<RegionStep> Steps
    {
        get => _steps.IsDefault ? [] : _steps;
        init => _steps = value;
    }

    private readonly ImmutableArray<RegionStep> _steps = [];

    /// <summary>Whether it has no step at all — selecting nothing beyond what it starts from.</summary>
    [JsonIgnore]
    public bool IsEmpty => Steps.IsEmpty;

    /// <summary>The middle of the first shape drawn; null where no shape is.</summary>
    [JsonIgnore]
    public MarkupPoint? Centre => Steps.OfType<RegionShape>().FirstOrDefault()?.Centre;

    public bool Equals(RecipeRegion? other) => other is not null && Steps.AsSpan().SequenceEqual(other.Steps.AsSpan());

    public override int GetHashCode() => Steps.Length;

    /// <summary>The region as text, the same for the same region.</summary>
    public string Describe() => string.Join("/", Steps.Select(s => s.Describe()));
}
