using System.Text.Json;

using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// What a section pass (<see cref="SectionPass"/>) came to for a document as it stands now, kept whole with what the document
/// is, so whatever asks about the document later — a question routed down its sections (<see cref="DocumentAnswerer"/>) —
/// reads it rather than asking a model again.
///
/// <para>
/// It is kept on the pass's own shelf, under what the document says: a document edited since has none until the pass works
/// over it again, which asks only about what changed, and settling the shelf lets go of the record of what it said before.
/// Only what was said is kept; the sections it was said of are the document's own, read from it as it is and matched part for
/// part, so a record never says where anything is that the document does not.
/// </para>
/// </summary>
/// <param name="Gist">What the document is.</param>
/// <param name="Said">What the pass came to, from the whole document down.</param>
public sealed record DocumentRecord(DocumentGist Gist, SectionResult Said)
{
    /// <summary>Whether <paramref name="notes"/> keep a record on <paramref name="shelf"/> of a document saying <paramref name="source"/>.</summary>
    public static bool Held(IDocumentNotes notes, string shelf, string source) => notes.Holds(shelf, KeyOf(source));

    /// <summary>
    /// The record <paramref name="notes"/> keep on <paramref name="shelf"/> of <paramref name="reading"/>, attached to its
    /// sections; null where none is kept of what it says now, or what is kept does not match its sections.
    /// </summary>
    public static async Task<DocumentRecord?> ReadAsync(IDocumentNotes notes, string shelf, DocumentReading reading, CancellationToken ct)
    {
        if (await notes.ReadAsync(shelf, KeyOf(reading.Source), ct) is not { } kept) return null;

        Kept? read;
        try { read = JsonSerializer.Deserialize<Kept>(kept); }
        catch (JsonException) { return null; }

        if (read is null || DocumentGist.Read(read.Gist) is not { } gist) return null;

        return Attached(reading, reading.Sections.Sections, read.Under) is { } under
            ? new DocumentRecord(gist, new SectionResult(null, reading.Title ?? string.Empty, read.Said, under))
            : null;
    }

    /// <summary>Keeps it on <paramref name="shelf"/> of <paramref name="notes"/>, as the record of <paramref name="reading"/>.</summary>
    public Task WriteAsync(IDocumentNotes notes, string shelf, DocumentReading reading, CancellationToken ct) =>
        notes.WriteAsync(shelf, KeyOf(reading.Source), JsonSerializer.Serialize(new Kept(Gist.Said, Said.Said, KeptOf(Said.Under))), ct);

    /// <summary>What a record of a document saying <paramref name="source"/> is kept under.</summary>
    private static string KeyOf(string source) => DocumentKeys.Of("record", source);

    private static List<KeptSection> KeptOf(IReadOnlyList<SectionResult> sections) =>
        [.. sections.Select(section => new KeptSection(section.Said, KeptOf(section.Under)))];

    /// <summary>What was kept of <paramref name="sections"/>, matched to them part for part; null where it does not match.</summary>
    private static List<SectionResult>? Attached(DocumentReading reading, IReadOnlyList<MarkdownSection> sections, IReadOnlyList<KeptSection>? kept)
    {
        if (kept is null || kept.Count != sections.Count) return null;

        var attached = new List<SectionResult>(sections.Count);
        for (var at = 0; at < sections.Count; at++)
        {
            if (Attached(reading, sections[at].Sections, kept[at].Under) is not { } under) return null;
            attached.Add(new SectionResult(sections[at], DocumentReading.Words(reading.Part(sections[at].Heading)), kept[at].Said ?? string.Empty, under));
        }

        return attached;
    }

    /// <summary>A record as it is kept: what the document is, what was said of it, and what of each section under it.</summary>
    private sealed record Kept(string Gist, string Said, List<KeptSection> Under);

    /// <summary>What was said of one section, and of each under it.</summary>
    private sealed record KeptSection(string Said, List<KeptSection> Under);
}
