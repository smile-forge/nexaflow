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
    /// <summary>The tune's name — the first <c>T:</c>, printed above the first system.</summary>
    public string? Title { get; init; }

    /// <summary>Further <c>T:</c> lines, printed under the title in a smaller face.</summary>
    public IReadOnlyList<string> Subtitles { get; init; } = [];

    /// <summary>The rhythm (<c>R:</c>) — printed at the top left, in italics.</summary>
    public string? Rhythm { get; init; }

    /// <summary>The composer (<c>C:</c>) — printed at the top right.</summary>
    public string? Composer { get; init; }

    /// <summary>Where the tune comes from (<c>O:</c>) — printed in brackets after the composer.</summary>
    public string? Origin { get; init; }

    /// <summary>Lines printed under the score, each already labelled the way an engraver labels it.</summary>
    public IReadOnlyList<string> Footer { get; init; } = [];

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
        var titles = new List<string>();
        var footer = new List<string>();
        string? rhythm = null, composer = null, origin = null, source = null, transcription = null;

        var started = false;

        foreach (var line in reading.Root.Children)
        {
            // The music starting is what ends the header — for the fields that are a header.
            if (line.Kind == AbcKinds.Line) { started = true; continue; }
            if (line.Kind != AbcKinds.Field) continue;

            if (line.Part(Roles.Name)?.Node.Text is not { Length: 2 } name) continue;

            var value = Written(line);
            if (name[0] != 'W' && value.Trim().Length == 0) continue;

            switch (name[0])
            {
                // A heading is a phrase rather than a layout, so its own spacing is noise.
                case 'T' when !started: titles.Add(value.Trim()); break;
                case 'R' when !started: rhythm ??= value.Trim(); break;
                case 'C' when !started: composer ??= value.Trim(); break;
                case 'O' when !started: origin ??= value.Trim(); break;

                // Read wherever they are written, because they are about the tune rather than part of its
                // heading. `S:` and `Z:` are deliberately not printed: where a tune was collected and who
                // typed it in are facts ABOUT it, and a transcriber's credit is as often a bare URL as a
                // name. They are kept — a details panel is the place for them — and they are not drawn.
                case 'S': source ??= value.Trim(); break;
                case 'Z': transcription ??= value.Trim(); break;
                case 'N': footer.Add("Notes: " + value.Trim()); break;

                // Kept as written, indentation and blank lines included. A verse is laid out by whoever
                // typed it — the second line of a stanza is indented under the first, and an empty `W:`
                // is the gap between stanzas — so trimming it is not tidying, it is discarding the only
                // formatting the field has.
                case 'W': footer.Add(value); break;
            }
        }

        while (footer.Count > 0 && footer[^1].Trim().Length == 0) footer.RemoveAt(footer.Count - 1);

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
    /// A field's value as it was written: trailing space gone, and the one space ABC allows after the
    /// colon gone with it, but everything the writer put there on purpose kept.
    /// </summary>
    private static string Written(ContentPart line)
    {
        var value = (line.Part(AbcRoles.Value)?.Node.Text ?? "").TrimEnd();
        return value.StartsWith(' ') ? value[1..] : value;
    }

    /// <summary>The composer with the origin in brackets after it, which is how the two are printed.</summary>
    public string? Credit =>
        (Composer, Origin) switch
        {
            (null, null) => null,
            (null, { } where) => $"({where})",
            ({ } who, null) => who,
            var (who, where) => $"{who} ({where})",
        };
}
