using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// A <c>wordcloud</c> block as its stages leave it (<see cref="Stages.ResolveCloud"/>): what the cloud is drawn like, and what its
/// words are drawn in. It prints as the lines written.
/// </summary>
internal sealed class WordCloudBlockNode : ContentNode
{
    internal WordCloudBlockNode(ContentNode written, WordCloudSettings settings, WordCloudColours colours) : base(written)
    {
        this.Settings = settings;
        this.Colours = colours;
    }

    /// <summary>The settings written above the words, defaults where none is.</summary>
    public WordCloudSettings Settings { get; }

    /// <summary>What the words are drawn in, and on.</summary>
    public WordCloudColours Colours { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new WordCloudBlockNode(shape, this.Settings, this.Colours);
}

/// <summary>
/// A word of the cloud as its stage leaves it (<see cref="Stages.ResolveWords"/>): the word as written — inside its quotes, where it
/// was written in any — with what it counts for, the size that sets it at, and when it is packed. Only a word the cloud can be made
/// of is one of these; a word that will not read keeps its line, marked with why.
/// </summary>
internal sealed class WordCloudWordNode : ContentNode
{
    internal WordCloudWordNode(ContentNode written, double weight, double size, int rank) : base(written)
    {
        this.Weight = weight;
        this.Size = size;
        this.Rank = rank;
    }

    /// <summary>What it counts for, as written after its colon.</summary>
    public double Weight { get; }

    /// <summary>The size it is set at: the lightest word at <c>minSize</c>, the heaviest at <c>maxSize</c>, the rest between by <c>scale</c>.</summary>
    public double Size { get; }

    /// <summary>
    /// When it is packed, from nought. Packing is greedy — each word takes the best place left, and there is no going back — so the
    /// heaviest goes first, or the word the reader came for ends up wherever the small ones left room. Ties keep the order they
    /// were written in, so a block whose weights are all the same draws as it reads.
    /// </summary>
    public int Rank { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new WordCloudWordNode(shape, this.Weight, this.Size, this.Rank);
}

/// <summary>How a cloud's words are coloured.</summary>
internal enum WordCloudColouring
{
    /// <summary>The palette's series colours, in turn.</summary>
    Themed,

    /// <summary><c>wordcloud2.js</c>'s scattered throw at the dark end of the lightness.</summary>
    RandomDark,

    /// <summary>The same throw at the light end.</summary>
    RandomLight,

    /// <summary>The colours written out, in turn.</summary>
    Written,
}

/// <summary>What a cloud's words are drawn in, and the ground they are drawn on — null for the page it sits on.</summary>
internal sealed record WordCloudColours(WordCloudColouring Colouring, IReadOnlyList<HexColor> Written, HexColor? Background);
