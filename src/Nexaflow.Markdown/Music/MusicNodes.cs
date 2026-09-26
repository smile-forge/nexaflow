using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music;

/// <summary>
/// A mark written on a note, as a notation's stages say what it means — ABC's <c>!trill!</c> or <c>.</c>, LilyPond's
/// <c>\trill</c> or <c>-.</c>. Which note it is written on is its notation's to say: ABC writes it before the note and LilyPond
/// after. It prints as the mark written.
/// </summary>
internal sealed class MusicMarkNode : ContentNode
{
    internal MusicMarkNode(ContentNode written, MusicMark mark) : base(written) => this.Mark = mark;

    public MusicMark Mark { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new MusicMarkNode(shape, this.Mark);
}

/// <summary>
/// Words written against a note, as a notation's stages say what they are: text set where <see cref="Placement"/> says, or — where
/// it says nothing — the name of the chord played there. It prints as the words written, quotes and all.
/// </summary>
internal sealed class MusicAnnotationNode : ContentNode
{
    internal MusicAnnotationNode(ContentNode written, string text, AnnotationPlacement? placement) : base(written)
    {
        this.Said = text;
        this.Placement = placement;
    }

    /// <summary>The words, without the quotes and the mark saying where they go.</summary>
    public string Said { get; }

    /// <summary>Where the words sit — null for a chord's name, which is set over the staff with the other chord names.</summary>
    public AnnotationPlacement? Placement { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new MusicAnnotationNode(shape, this.Said, this.Placement);
}
