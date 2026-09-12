using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// Reads the prose around a tune: which field goes where on the page.
///
/// <para>
/// Where each field goes is ABC's answer, not ours — the letters are a fixed vocabulary and every engraver
/// puts them in the same places, which is why this is a mapping rather than a decision.
/// </para>
/// </summary>
public static class AbcHeader
{
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
    public static MusicHeader Of(ContentReading reading)
    {
        var titles = new List<MusicHeader.Prose>();
        var footer = new List<MusicHeader.Prose>();
        MusicHeader.Prose? rhythm = null, composer = null, origin = null;
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
                case 'N': footer.Add(new MusicHeader.Prose("Notes: " + heading.Text, heading.Part)); break;

                case 'W': footer.Add(Read(line, heading: false)); break;
            }
        }

        while (footer.Count > 0 && footer[^1].Text.Trim().Length == 0) footer.RemoveAt(footer.Count - 1);

        return new MusicHeader
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
    private static MusicHeader.Prose Read(ContentPart line, bool heading)
    {
        var part = line.Part(AbcRoles.Value);
        var raw = part?.Node.Text ?? "";

        var from = 0;
        var to = raw.Length;

        while (to > from && char.IsWhiteSpace(raw[to - 1])) to--;

        if (heading) while (from < to && char.IsWhiteSpace(raw[from])) from++;
        else if (from < to && raw[from] == ' ') from++;

        var text = raw[from..to];
        return new MusicHeader.Prose(text, part is null ? null : new SourceSpan(part.Start + from, text.Length));
    }
}
