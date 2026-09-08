using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// The prose around a tune: what is printed above the first system and what is printed under the last.
///
/// <para>
/// Read off the tree rather than engraved, because it is <em>text</em>. A title is words a reader wants
/// to select and copy, and a title painted into a picture of a score is words nobody can reach. So this
/// says what the fields hold and where each one belongs, and whatever is drawing the tune turns that into
/// whatever it uses for text.
/// </para>
/// <para>
/// Where each field goes is ABC's answer, not ours — the letters are a fixed vocabulary and every
/// engraver puts them in the same places, which is why this is a mapping rather than a decision.
/// </para>
/// </summary>
public sealed record AbcHeader
{
    /// <summary>
    /// One line of the prose around a tune: what it says, and the stretch of source it was written in.
    ///
    /// <para>
    /// The part is what makes it selectable, and it names <em>exactly</em> the characters the text is —
    /// space around a heading trimmed off, and the one space ABC allows after the colon gone with it. That
    /// exactness is what lets the words be drawn a letter at a time: the nth character of the text is the
    /// nth character of the part, so each letter can say where it was written.
    /// </para>
    /// <para>
    /// Where the two cannot line up — a note field printed as "Notes: …", a composer and an origin set as
    /// one line — the text simply is not the source, and it is drawn as one piece naming the whole of what
    /// it came from.
    /// </para>
    /// </summary>
    public readonly record struct Prose(string Text, ISourcePart? Part)
    {
        /// <summary>Whether each character of the text is a character of the source it names.</summary>
        public bool IsWritten => Part is { } part && part.Length == Text.Length;

        public override string ToString() => Text;
    }

    /// <summary>The tune's name — the first <c>T:</c>, printed above the first system.</summary>
    public Prose? Title { get; init; }

    /// <summary>Further <c>T:</c> lines, printed under the title in a smaller face.</summary>
    public IReadOnlyList<Prose> Subtitles { get; init; } = [];

    /// <summary>The rhythm (<c>R:</c>) — printed at the top left, in italics.</summary>
    public Prose? Rhythm { get; init; }

    /// <summary>The composer (<c>C:</c>) — printed at the top right.</summary>
    public Prose? Composer { get; init; }

    /// <summary>Where the tune comes from (<c>O:</c>) — printed in brackets after the composer.</summary>
    public Prose? Origin { get; init; }

    /// <summary>Lines printed under the score, each already labelled the way an engraver labels it.</summary>
    public IReadOnlyList<Prose> Footer { get; init; } = [];

    /// <summary>Where the tune was collected (<c>S:</c>) — read, kept, and not printed.</summary>
    public string? Source { get; init; }

    /// <summary>Who transcribed it (<c>Z:</c>) — read, kept, and not printed.</summary>
    public string? Transcription { get; init; }

    /// <summary>Whether there is anything at all to print around the tune.</summary>
    public bool IsEmpty =>
        Title is null && Subtitles.Count == 0 && Rhythm is null && Composer is null && Footer.Count == 0;

    /// <summary>
    /// Reads the prose fields of a tune.
    ///
    /// <para>
    /// What is printed <em>above</em> the music is read from before it: a <c>T:</c> in the middle is a
    /// section heading rather than a title, and belongs to the bar it stands over.
    /// </para>
    /// <para>
    /// What is printed <em>below</em> is read from anywhere, because that is where it is usually written.
    /// <c>W:</c> is the verses an engraver sets under the score, and a tune with four of them puts the
    /// music first and the words after it — so stopping at the first note, as this did, threw away the
    /// normal case and kept only the unusual one. Two of four corpus tunes taken at random lost every
    /// verse that way.
    /// </para>
    /// </summary>
    public static AbcHeader Of(ContentReading reading)
    {
        var titles = new List<Prose>();
        var footer = new List<Prose>();
        Prose? rhythm = null, composer = null, origin = null;
        string? source = null, transcription = null;

        var started = false;

        foreach (var line in reading.Root.Children)
        {
            // The music starting is what ends the header — for the fields that are a header.
            if (line.Kind == AbcKinds.Line) { started = true; continue; }
            if (line.Kind != AbcKinds.Field) continue;

            if (line.Part(Roles.Name)?.Node.Text is not { Length: 2 } name) continue;

            var heading = Read(line, heading: true);
            if (name[0] != 'W' && heading.Text.Length == 0) continue;

            switch (name[0])
            {
                case 'T' when !started: titles.Add(heading); break;
                case 'R' when !started: rhythm ??= heading; break;
                case 'C' when !started: composer ??= heading; break;
                case 'O' when !started: origin ??= heading; break;

                // Read wherever they are written, because they are about the tune rather than part of its
                // heading. `S:` and `Z:` are deliberately not printed: where a tune was collected and who
                // typed it in are facts ABOUT it, and a transcriber's credit is as often a bare URL as a
                // name. They are kept — a details panel is the place for them — and they are not drawn.
                case 'S': source ??= heading.Text; break;
                case 'Z': transcription ??= heading.Text; break;

                // Labelled, so the text is no longer the source and it is drawn as one piece.
                case 'N': footer.Add(new Prose("Notes: " + heading.Text, heading.Part)); break;

                case 'W': footer.Add(Read(line, heading: false)); break;
            }
        }

        while (footer.Count > 0 && footer[^1].Text.Trim().Length == 0) footer.RemoveAt(footer.Count - 1);

        return new AbcHeader
        {
            Title = titles.Count > 0 ? titles[0] : null,
            Subtitles = titles.Count > 1 ? [.. titles[1..]] : [],
            Rhythm = rhythm,
            Composer = composer,
            Origin = origin,
            Source = source,
            Transcription = transcription,
            Footer = footer,
        };
    }

    /// <summary>
    /// A field's value and the stretch of source it occupies, with whichever space around it is not part
    /// of what was written left out.
    ///
    /// <para>
    /// Always the one space ABC allows after the colon, and always trailing space. For a heading,
    /// everything either side: a title is a phrase rather than a layout, so its own spacing is noise. For a
    /// verse, nothing else — the second line of a stanza is indented under the first, and an empty
    /// <c>W:</c> is the gap between stanzas, so trimming it is not tidying but discarding the only
    /// formatting the field has.
    /// </para>
    /// <para>
    /// The offset moves with the text, which is the whole point: it is what lets the words be selected a
    /// letter at a time rather than all or nothing.
    /// </para>
    /// </summary>
    private static Prose Read(ContentPart line, bool heading)
    {
        var part = line.Part(AbcRoles.Value);
        var raw = part?.Node.Text ?? "";

        var from = 0;
        var to = raw.Length;

        while (to > from && char.IsWhiteSpace(raw[to - 1])) to--;

        if (heading) while (from < to && char.IsWhiteSpace(raw[from])) from++;
        else if (from < to && raw[from] == ' ') from++;

        var text = raw[from..to];
        return new Prose(text, part is null ? null : new SourceSpan(part.Start + from, text.Length));
    }

    /// <summary>
    /// The composer with the origin in brackets after it, which is how the two are printed.
    /// <para>
    /// It names the composer's own field where there is one, because that is the half a reader means by
    /// pointing at it; a tune with only an origin names that instead. Two fields drawn as one line can
    /// only stand for one of them, and the alternative — drawing them as two pieces — would put a gap
    /// between a name and its bracket that no engraver leaves.
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
