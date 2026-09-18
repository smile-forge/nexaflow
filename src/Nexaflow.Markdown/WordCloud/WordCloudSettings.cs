using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.WordCloud;

/// <summary>How a weight becomes a size.</summary>
public enum WordCloudScale
{
    /// <summary>Size runs with the weight itself — twice the count is twice the height.</summary>
    Linear,

    /// <summary>Size runs with the square root of the weight, so a word's <em>area</em> runs with its count.</summary>
    Root,

    /// <summary>Size runs with the logarithm of the weight, for counts spread over orders of magnitude.</summary>
    Logarithmic,
}

/// <summary>
/// Everything about a cloud except the words in it: how big the picture is, what shape the words are packed
/// into, how a weight becomes a size, which of them are turned, and what they are drawn in.
///
/// <para>
/// The colours are held as they were written rather than as colours, because this assembly has no colours in
/// it: what <c>theme</c> means is the palette's, and only the builder has one. Everything else is settled
/// here, so a bad number stops the block being a block before anything is measured.
/// </para>
/// </summary>
public sealed record WordCloudSettings
{
    public static readonly WordCloudSettings Default = new();

    /// <summary>Nought for "as wide as the column it is in", which is what a cloud with no <c>width:</c> takes.</summary>
    public double Width { get; init; }

    /// <summary>Nought for <see cref="HeightShare"/> of the width, which is what a cloud with no <c>height:</c> takes.</summary>
    public double Height { get; init; }

    /// <summary>How tall a cloud is for how wide it is, where it was not told.</summary>
    public const double HeightShare = 0.6;

    public const double MinSide = 80;
    public const double MaxSide = 4000;

    /// <summary>The widest a cloud with no <c>width:</c> is drawn, however wide the column is.</summary>
    public const double RoomLimit = 900;

    public WordCloudShape Shape { get; init; } = WordCloudShape.Circle;

    /// <summary>
    /// Letters the cloud is packed into the shape of, or null. A stencil rather than a
    /// <see cref="WordCloudShape"/>: the words fill the letters from the inside, and whatever
    /// <see cref="Shape"/> says is set aside while one is in force, because a letter's edge is not a
    /// function of its angle.
    /// </summary>
    public string? Letters { get; init; }

    /// <summary>
    /// A picture whose silhouette the cloud is packed into, named as a picture in the document is, or null.
    /// Where both this and <see cref="Letters"/> are written, the picture wins — it is the more particular of
    /// the two, and a reader who named one meant it.
    /// </summary>
    public string? Mask { get; init; }

    /// <summary>How far the cloud is squashed towards its waist: one is round, less is flatter.</summary>
    public double Ellipticity { get; init; } = 0.65;

    public const double MinEllipticity = 0.1;
    public const double MaxEllipticity = 4;

    /// <summary>The faces a word is set in, first that the machine has.</summary>
    public string Font { get; init; } = DefaultFont;

    public const string DefaultFont = "Segoe UI, Arial, sans-serif";

    public bool Bold { get; init; } = true;

    /// <summary>The size the lightest word is set at.</summary>
    public double MinSize { get; init; } = 12;

    /// <summary>The size the heaviest word is set at.</summary>
    public double MaxSize { get; init; } = 72;

    public const double SmallestSize = 4;
    public const double LargestSize = 400;

    public WordCloudScale Scale { get; init; } = WordCloudScale.Root;

    /// <summary>
    /// How finely a word's shape is known when it is fitted against its neighbours, in pixels. Smaller packs
    /// tighter and costs more: the fitting is done on a grid this size, and halving it quadruples the cells.
    /// </summary>
    public double GridSize { get; init; } = 4;

    public const double MinGridSize = 1;
    public const double MaxGridSize = 32;

    /// <summary>Clear air kept around every word, in pixels, so letters of neighbouring words do not touch.</summary>
    public double Gap { get; init; } = 2;

    public const double MaxGap = 40;

    /// <summary>The share of the words that are turned, nought to one.</summary>
    public double Rotate { get; init; } = 0.1;

    /// <summary>The angles a turned word may take, in degrees — negative reads upward.</summary>
    public double MinRotation { get; init; } = -90;

    public double MaxRotation { get; init; } = 90;

    public const double RotationLimit = 360;

    /// <summary>
    /// How many angles there are between <see cref="MinRotation"/> and <see cref="MaxRotation"/>; nought — the
    /// default, as it is <c>wordcloud2.js</c>'s — for any angle between them. Two gives the tidier look of a
    /// cloud whose words are either level or on their side, and nothing in between.
    /// </summary>
    public int RotationSteps { get; init; }

    public const int MaxRotationSteps = 64;

    /// <summary>What a word is drawn in, as it was written: <c>theme</c>, <c>random-dark</c>, <c>random-light</c>, or colours.</summary>
    public string Colour { get; init; } = Themed;

    public const string Themed = "theme";
    public const string RandomDark = "random-dark";
    public const string RandomLight = "random-light";

    /// <summary>What the picture is drawn on, as it was written, or null for the page it sits on.</summary>
    public string? Background { get; init; }

    /// <summary>
    /// The throw every choice in the placement is made from. A cloud is laid out again on every keystroke, so
    /// the throw has to be the source's rather than the clock's: the same block always gives the same cloud,
    /// and a reader typing into one watches it settle rather than jump.
    /// </summary>
    public int Seed { get; init; } = 1;

    /// <summary>Whether the places at one radius are tried in a shuffled order, which is what keeps a cloud from combing.</summary>
    public bool Shuffle { get; init; } = true;

    /// <summary>Whether a word too big for the room left is set smaller until it fits, rather than left out.</summary>
    public bool Fit { get; init; } = true;
}

/// <summary>
/// Which keys name a setting rather than a word.
///
/// <para>
/// A cloud's body is one grammar doing two jobs: most of its lines are the words, and a few of them are how
/// the words are drawn. Which a line is, is its key — and a key written in quotes is always a word, which is
/// how a cloud counts the word <c>shape</c> without losing its shape.
/// </para>
/// </summary>
public static class WordCloudSetting
{
    /// <summary>Every setting name, as it is written.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "width", "height", "shape", "letters", "mask", "ellipticity", "font", "bold",
        "minSize", "maxSize", "scale", "gridSize", "gap",
        "rotate", "minRotation", "maxRotation", "rotationSteps",
        "color", "colour", "background", "seed", "shuffle", "fit",
    ];

    /// <summary>What a diagnostic lists when it names them all.</summary>
    public static readonly string Names = string.Join(", ", Keys);

    /// <summary>Whether a key names a setting. Case and hyphens are ignored, so <c>min-size</c> is <c>minSize</c>.</summary>
    public static bool Is(string? key) => SettingKeys.Is(key, Keys);
}
