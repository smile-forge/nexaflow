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
    /// Words a score sets, and what they were written as: the part they name, and — where each character of the words is a
    /// character written in the source — the part each one is, so a reader can select them a letter at a time.
    /// </summary>
    public readonly record struct Prose(string Text, ISourcePart? Part, IReadOnlyList<ISourcePart>? Letters = null)
    {
        /// <summary>Whether each character of the text is a character of the source, with a part of its own.</summary>
        public bool IsWritten => Letters is not null;

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
