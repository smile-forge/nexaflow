using System.Text;

using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// A summary of a document as its sections stand: what each section says in brief, and the summaries of the sections under it —
/// the whole document's, at the root, its abstract.
/// </summary>
/// <param name="Section">The section summarised, or null for the whole document.</param>
/// <param name="Title">What the section's heading says, or the document's title.</param>
/// <param name="Summary">What it says, in brief — empty where it says nothing of its own and nothing is under it.</param>
/// <param name="Under">The summaries of the sections under it, in the order written.</param>
public sealed record SummaryNode(MarkdownSection? Section, string Title, string Summary, IReadOnlyList<SummaryNode> Under);

/// <summary>
/// Summarises a document a section at a time, from the deepest up: each section from what it says itself and from the
/// summaries of the sections under it, the whole document from what comes before its first heading and the summaries of its
/// outermost sections.
///
/// <para>
/// Every question is handed the document's gist (<see cref="DocumentGist"/>), where the section stands among the headings, and
/// what the section before it said in brief, so every part is summarised as the one document it is. Every question is sized to
/// the model (<see cref="DocumentBudget"/>): a section too long to read at once is read a stretch at a time, the notes so far
/// carried into the next, and so are summaries too many to read at once. Each summary is kept under everything it was worked out
/// from, so summarising again asks only about the sections that changed and the ones holding them.
/// </para>
/// </summary>
public sealed class DocumentSummarizer(IDocumentModel model, IDocumentNotes notes, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>The shelf its notes are kept on.</summary>
    public const string Shelf = "summary";

    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    private const string Version = "summary-1";

    /// <summary>How many times a question that filled what the model reads is asked again with half as much.</summary>
    private const int Narrowings = 3;

    private int _done;
    private int _of;

    /// <summary>The summary of <paramref name="reading"/>, which is <paramref name="gist"/>.</summary>
    public async Task<SummaryNode> SummarizeAsync(DocumentReading reading, DocumentGist gist, CancellationToken ct)
    {
        _done = 0;
        _of = reading.Sections.All().Count() + 1;

        var title = reading.Title ?? string.Empty;
        var under = await UnderAsync(reading, gist, reading.Sections.Sections, [], ct);
        var opening = reading.Text(0, reading.Preamble);

        // A document whose whole body stands under one heading is said in brief by that section's summary.
        var whole = opening.Length == 0 && under.Count == 1
            ? under[0].Summary
            : await SummaryAsync(gist, [], opening, under, before: null, whole: true, ct);

        progress?.Report(new(DocumentStep.Summary, _of, _of));
        return new SummaryNode(null, title, whole, under);
    }

    /// <summary>The summaries of <paramref name="sections"/>, standing under the headings <paramref name="path"/> names.</summary>
    private async Task<IReadOnlyList<SummaryNode>> UnderAsync(DocumentReading reading, DocumentGist gist, IReadOnlyList<MarkdownSection> sections,
                                                             IReadOnlyList<string> path, CancellationToken ct)
    {
        var made = new List<SummaryNode>();
        string? before = null;

        foreach (var section in sections)
        {
            var title = DocumentReading.Words(reading.Part(section.Heading));
            IReadOnlyList<string> at = [.. path, title];

            var under = await UnderAsync(reading, gist, section.Sections, at, ct);
            var summary = await SummaryAsync(gist, at, reading.Text(section.Heading + 1, section.Body), under, before, whole: false, ct);

            progress?.Report(new(DocumentStep.Summary, ++_done, _of));
            made.Add(new SummaryNode(section, title, summary, under));

            if (summary.Length > 0) before = summary;
        }

        return made;
    }

    /// <summary>
    /// What a section says in brief — or the whole document, where <paramref name="whole"/> — from <paramref name="own"/>, what it
    /// says itself, and the summaries <paramref name="under"/> it.
    /// </summary>
    private async Task<string> SummaryAsync(DocumentGist gist, IReadOnlyList<string> path, string own, IReadOnlyList<SummaryNode> under,
                                            string? before, bool whole, CancellationToken ct)
    {
        var told = string.Join("\n\n", under.Where(node => node.Summary.Length > 0).Select(node => $"{node.Title}:\n{node.Summary}"));
        if (own.Length == 0 && told.Length == 0) return string.Empty;

        var where = string.Join(" › ", path);
        var key = DocumentKeys.Of(Version, model.Key, gist.Key, where, whole ? "whole" : "part", own, told);
        if (await notes.ReadAsync(Shelf, key, ct) is { } kept) return kept;

        var room = DocumentBudget.Of(model).Share(0.45);

        for (var narrowed = 0; ; narrowed++)
        {
            // What does not fit is read a stretch at a time first; what is under it, too, where there is too much of that.
            var read = own;
            var summaries = told;
            if (read.Length + summaries.Length > room && summaries.Length > room / 2) summaries = await NotesAsync(gist, where, summaries, room, ct);
            if (read.Length + summaries.Length > room) read = await NotesAsync(gist, where, read, Math.Max(room - summaries.Length, room / 2), ct);

            var asked = new StringBuilder();
            if (where.Length > 0) asked.Append("Where it stands in the document: ").Append(where).Append("\n\n");
            if (before is not null) asked.Append("What the part before it says, in brief:\n").Append(before).Append("\n\n");
            if (read.Length > 0) asked.Append(whole ? "What the document says before its first heading:\n\n" : "What it says:\n\n").Append(read).Append("\n\n");
            if (summaries.Length > 0) asked.Append(whole ? "What its parts say, in brief:\n\n" : "What the parts under it say, in brief:\n\n").Append(summaries).Append("\n\n");
            asked.Append(whole ? "Summarise the whole document" : "Summarise this part")
                 .Append($" in at most {Words(read.Length + summaries.Length, whole)} words, in {gist.Language}.");

            var reply = await model.AnswerAsync(Instructions(gist), asked.ToString(), ct);

            if (reply.Ended == DocumentReplyEnd.ContextFull && narrowed < Narrowings)
            {
                room /= 2;
                continue;
            }

            var summary = Answered(reply, where);
            await notes.WriteAsync(Shelf, key, summary, ct);
            return summary;
        }
    }

    /// <summary>
    /// Notes on <paramref name="text"/> — too long to read at once — read a stretch of at most <paramref name="room"/> characters
    /// at a time, the notes so far carried into the next: shorter than it, and enough to summarise it from.
    /// </summary>
    private async Task<string> NotesAsync(DocumentGist gist, string where, string text, int room, CancellationToken ct)
    {
        string? sofar = null;

        foreach (var stretch in DocumentCuts.Cut(text, room))
        {
            var key = DocumentKeys.Of(Version, "notes", model.Key, gist.Key, where, sofar ?? string.Empty, stretch);
            if (await notes.ReadAsync(Shelf, key, ct) is { } kept)
            {
                sofar = kept;
                continue;
            }

            var asked = new StringBuilder();
            if (where.Length > 0) asked.Append("Where it stands in the document: ").Append(where).Append("\n\n");
            if (sofar is not null) asked.Append("Your notes on it so far:\n").Append(sofar).Append("\n\nIt goes on:\n\n");
            asked.Append(stretch).Append("\n\n")
                 .Append($"Write notes on all of it so far, in at most {Words(stretch.Length + (sofar?.Length ?? 0), whole: false) * 2} words, in {gist.Language}: ")
                 .Append("everything a summary of it would need, in the order it comes.");

            sofar = Answered(await model.AnswerAsync(Instructions(gist), asked.ToString(), ct), where);
            await notes.WriteAsync(Shelf, key, sofar, ct);
        }

        return sofar ?? string.Empty;
    }

    /// <summary>What a model answered, or why it could not be worked from.</summary>
    private static string Answered(DocumentReply reply, string where)
    {
        if (reply.Ended == DocumentReplyEnd.Failed)
            throw new DocumentWorkException($"The model could not be asked about {(where.Length > 0 ? $"“{where}”" : "the document")}.");

        return reply.Text.Trim();
    }

    /// <summary>About how many words a summary of <paramref name="characters"/> characters runs to.</summary>
    private static int Words(int characters, bool whole) => Math.Clamp(characters / 48, 40, whole ? 400 : 250);

    private static string Instructions(DocumentGist gist) =>
        "You summarise a longer document one part at a time, so that the summaries of all its parts read as one. The whole " +
        $"document is:\n{gist.Said}\n\n" +
        "Write in its language, register, tone and voice, for its audience. Say what the part says, briefly, as the document " +
        "itself would — do not describe the document or the part. Plain prose only: no heading, no list, no preamble, nothing " +
        "but the summary.";
}
