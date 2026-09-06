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

    /// <summary>Whether there is anything at all to print around the tune.</summary>
    public bool IsEmpty =>
        Title is null && Subtitles.Count == 0 && Rhythm is null && Composer is null && Footer.Count == 0;

    /// <summary>
    /// Reads the header fields of a tune. Only the ones written before the music: a <c>T:</c> in the
    /// middle is a section heading rather than a title, and belongs to the bar it stands over.
    /// </summary>
    public static AbcHeader Of(ContentReading reading)
    {
        var titles = new List<string>();
        var footer = new List<string>();
        string? rhythm = null, composer = null, origin = null;

        foreach (var line in reading.Root.Children)
        {
            // The music starting is what ends the header, whatever fields come after it.
            if (line.Kind == AbcKinds.Line) break;
            if (line.Kind != AbcKinds.Field) continue;

            if (line.Part(Roles.Name)?.Node.Text is not { Length: 2 } name) continue;
            var value = (line.Part(AbcRoles.Value)?.Node.Text ?? "").Trim();
            if (value.Length == 0) continue;

            switch (name[0])
            {
                case 'T': titles.Add(value); break;
                case 'R': rhythm ??= value; break;
                case 'C': composer ??= value; break;
                case 'O': origin ??= value; break;

                // Everything an engraver prints under the score, labelled as it prints it.
                case 'S': footer.Add("Source: " + value); break;
                case 'Z': footer.Add("Transcription: " + value); break;
                case 'N': footer.Add("Notes: " + value); break;
                case 'W': footer.Add(value); break;
            }
        }

        return new AbcHeader
        {
            Title = titles.Count > 0 ? titles[0] : null,
            Subtitles = titles.Count > 1 ? [.. titles[1..]] : [],
            Rhythm = rhythm,
            Composer = composer,
            Origin = origin,
            Footer = footer,
        };
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
