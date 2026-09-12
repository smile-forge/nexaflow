using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music;

/// <summary>
/// The prose around a piece of music: what is printed above the first system and what is printed under the
/// last.
///
/// <para>
/// Read off the tree rather than engraved, because it is <em>text</em>. A title is words a reader wants to
/// select and copy, so this says what each field holds, where it was written, and where it belongs, and
/// whatever is drawing the music turns that into whatever it uses for text.
/// </para>
/// <para>
/// Every notation fills the same places — a title centred, a rhythm or a poet at the top left, the composer
/// at the top right, words under the music — because every engraver prints them there. Which of a notation's
/// fields goes in which place is that notation's reader to say.
/// </para>
/// </summary>
public sealed record MusicHeader
{
    /// <summary>
    /// One line of the prose around the music: what it says, and the stretch of source it was written in.
    ///
    /// <para>
    /// The part is what makes it selectable, and it names <em>exactly</em> the characters the text is — space
    /// around a heading trimmed off, and whatever quotes or colon introduced it gone with it. That exactness is
    /// what lets the words be drawn a letter at a time: the nth character of the text is the nth character of
    /// the part, so each letter can say where it was written.
    /// </para>
    /// <para>
    /// Where the two cannot line up — a note field printed as "Notes: …", a composer and an origin set as one
    /// line — the text simply is not the source, and it is drawn as one piece naming the whole of what it came
    /// from.
    /// </para>
    /// </summary>
    public readonly record struct Prose(string Text, ISourcePart? Part)
    {
        /// <summary>Whether each character of the text is a character of the source it names.</summary>
        public bool IsWritten => Part is { } part && part.Length == Text.Length;

        public override string ToString() => Text;
    }

    /// <summary>The name of the piece, printed above the first system.</summary>
    public Prose? Title { get; init; }

    /// <summary>Further titles, printed under the title in a smaller face.</summary>
    public IReadOnlyList<Prose> Subtitles { get; init; } = [];

    /// <summary>The rhythm, or the poet — printed at the top left, in italics.</summary>
    public Prose? Rhythm { get; init; }

    /// <summary>The composer — printed at the top right.</summary>
    public Prose? Composer { get; init; }

    /// <summary>Where the piece comes from — printed in brackets after the composer.</summary>
    public Prose? Origin { get; init; }

    /// <summary>Lines printed under the music, each already labelled the way an engraver labels it.</summary>
    public IReadOnlyList<Prose> Footer { get; init; } = [];

    /// <summary>Where the piece was collected — read, kept, and not printed.</summary>
    public string? Source { get; init; }

    /// <summary>Who transcribed it — read, kept, and not printed.</summary>
    public string? Transcription { get; init; }

    /// <summary>Whether there is anything at all to print around the music.</summary>
    public bool IsEmpty =>
        Title is null && Subtitles.Count == 0 && Rhythm is null && Composer is null && Footer.Count == 0;

    /// <summary>
    /// The composer with the origin in brackets after it, which is how the two are printed.
    /// <para>
    /// It names the composer's own field where there is one, because that is the half a reader means by
    /// pointing at it; a piece with only an origin names that instead. Two fields drawn as one line can only
    /// stand for one of them, and the alternative — drawing them as two pieces — would put a gap between a name
    /// and its bracket that no engraver leaves.
    /// </para>
    /// </summary>
    public Prose? Credit =>
        (Composer, Origin) switch
        {
            (null, null) => null,
            (null, { } where) => new Prose($"({where.Text})", where.Part),
            ({ } who, null) => who,
            var (who, where) => new Prose($"{who.Value.Text} ({where!.Value.Text})", who.Value.Part),
        };
}
