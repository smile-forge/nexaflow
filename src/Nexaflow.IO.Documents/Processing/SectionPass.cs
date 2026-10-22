using System.Text;

using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// What one question asked of every section of a document came to (<see cref="SectionPass"/>): what was said of each section,
/// and what was said of the sections under it — of the whole document, at the root.
/// </summary>
/// <param name="Section">The section, or null for the whole document.</param>
/// <param name="Title">What the section's heading says, or the document's title.</param>
/// <param name="Said">What was said of it — empty where it says nothing of its own and nothing is under it.</param>
/// <param name="Under">What was said of the sections under it, in the order written.</param>
public sealed record SectionResult(MarkdownSection? Section, string Title, string Said, IReadOnlyList<SectionResult> Under);

/// <summary>What a question asked of one part of a document is closed with.</summary>
/// <param name="Whole">Whether the part is the whole document.</param>
/// <param name="Characters">How much is handed with it — what it says, and what was said of the parts under it — which an answer is sized by.</param>
/// <param name="Gist">What the document is.</param>
public readonly record struct SectionAsk(bool Whole, int Characters, DocumentGist Gist);

/// <summary>
/// One question asked of every section of a document, from the deepest up (<see cref="SectionPass"/>): what the model is told,
/// how what was said of other parts is handed to it, how each question closes, and how an answer is read. A summary is one
/// (<see cref="SummaryQuestion"/>); the characters a story names, the events it tells, the notions it explains are others —
/// each said of a section from what it says itself and from what was said of the sections under it.
/// </summary>
public interface ISectionQuestion
{
    /// <summary>The shelf its notes, and its record (<see cref="DocumentRecord"/>), are kept on.</summary>
    string Shelf { get; }

    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    string Version { get; }

    /// <summary>What a job asking it is doing, as its progress is said.</summary>
    DocumentStep Step { get; }

    /// <summary>What introduces what was said of the part before, handed to the next.</summary>
    string Before { get; }

    /// <summary>What the model is told before every question, about the document <paramref name="gist"/> says it is.</summary>
    string Instructions(DocumentGist gist);

    /// <summary>What introduces what was said of the parts under a part — of the document's outermost parts, where <paramref name="whole"/>.</summary>
    string Under(bool whole);

    /// <summary>What closes the question asked of a part, or of the whole document.</summary>
    string Asked(SectionAsk ask);

    /// <summary>What closes the question asked of a stretch of a part too long to read at once: notes on all of it so far, enough to answer from.</summary>
    string Noted(SectionAsk ask);

    /// <summary>What an answer says, as it is kept; null where it cannot be worked from, and the question is asked again.</summary>
    string? Read(string answer);
}

/// <summary>
/// Asks one question (<see cref="ISectionQuestion"/>) of every section of a document, from the deepest up — of each section
/// from what it says itself and what was said of the sections under it, of the whole document from what comes before its first
/// heading and what was said of its outermost sections — so a document a model cannot read at once is answered of as a whole.
///
/// <para>
/// Every question is handed the document's gist (<see cref="DocumentGist"/>), where the section stands among the headings, and
/// what was said of the section before it, so every part is answered of as the one document it is. Every question is sized to
/// the model (<see cref="DocumentBudget"/>): a section too long to read at once is read a stretch at a time, the notes so far
/// carried into the next, and so is what was said of the sections under it where that is too much at once. Each answer is kept
/// under everything it was worked out from, so asking again asks only about the sections that changed and the ones holding them.
/// </para>
///
/// <para>
/// What it came to is kept whole as the document's record (<see cref="DocumentRecord"/>), under what the document says now, so
/// whatever asks about the document afterwards — a question routed down its sections (<see cref="DocumentAnswerer"/>) — reads
/// it without asking a model anything again.
/// </para>
/// </summary>
public sealed class SectionPass(IDocumentModel model, IDocumentNotes notes, ISectionQuestion question, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>How many times a question that filled what the model reads is asked again with half as much.</summary>
    private const int Narrowings = 3;

    private int _done;
    private int _of;

    /// <summary>What the question comes to for <paramref name="reading"/>, which is <paramref name="gist"/>; kept as its record.</summary>
    public async Task<SectionResult> RunAsync(DocumentReading reading, DocumentGist gist, CancellationToken ct)
    {
        _done = 0;
        _of = reading.Sections.All().Count() + 1;

        var title = reading.Title ?? string.Empty;
        var under = await UnderAsync(reading, gist, reading.Sections.Sections, [], ct);
        var opening = reading.Text(0, reading.Preamble);

        // A document whose whole body stands under one heading is said of by what was said of that section.
        var whole = opening.Length == 0 && under.Count == 1
            ? under[0].Said
            : await SayAsync(gist, [], opening, under, before: null, whole: true, ct);

        progress?.Report(new(question.Step, _of, _of));

        var result = new SectionResult(null, title, whole, under);
        await new DocumentRecord(gist, result).WriteAsync(notes, question.Shelf, reading, ct);
        return result;
    }

    /// <summary>What is said of <paramref name="sections"/>, standing under the headings <paramref name="path"/> names.</summary>
    private async Task<IReadOnlyList<SectionResult>> UnderAsync(DocumentReading reading, DocumentGist gist, IReadOnlyList<MarkdownSection> sections,
                                                              IReadOnlyList<string> path, CancellationToken ct)
    {
        var made = new List<SectionResult>();
        string? before = null;

        foreach (var section in sections)
        {
            var title = DocumentReading.Words(reading.Part(section.Heading));
            IReadOnlyList<string> at = [.. path, title];

            var under = await UnderAsync(reading, gist, section.Sections, at, ct);
            var said = await SayAsync(gist, at, reading.Text(section.Heading + 1, section.Body), under, before, whole: false, ct);

            progress?.Report(new(question.Step, ++_done, _of));
            made.Add(new SectionResult(section, title, said, under));

            if (said.Length > 0) before = said;
        }

        return made;
    }

    /// <summary>
    /// What is said of a section — or of the whole document, where <paramref name="whole"/> — from <paramref name="own"/>, what it
    /// says itself, and what was said of the sections <paramref name="under"/> it.
    /// </summary>
    private async Task<string> SayAsync(DocumentGist gist, IReadOnlyList<string> path, string own, IReadOnlyList<SectionResult> under,
                                        string? before, bool whole, CancellationToken ct)
    {
        var told = string.Join("\n\n", under.Where(node => node.Said.Length > 0).Select(node => $"{node.Title}:\n{node.Said}"));
        if (own.Length == 0 && told.Length == 0) return string.Empty;

        var where = string.Join(" › ", path);
        var key = DocumentKeys.Of(question.Version, model.Key, gist.Key, where, whole ? "whole" : "part", own, told);
        if (await notes.ReadAsync(question.Shelf, key, ct) is { } kept) return kept;

        var room = DocumentBudget.Of(model).Share(0.45);

        for (var narrowed = 0; ; narrowed++)
        {
            // What does not fit is read a stretch at a time first; what is under it, too, where there is too much of that.
            var read = own;
            var parts = told;
            if (read.Length + parts.Length > room && parts.Length > room / 2) parts = await NotesAsync(gist, where, parts, room, ct);
            if (read.Length + parts.Length > room) read = await NotesAsync(gist, where, read, Math.Max(room - parts.Length, room / 2), ct);

            var asked = new StringBuilder();
            if (where.Length > 0) asked.Append("Where it stands in the document: ").Append(where).Append("\n\n");
            if (before is not null) asked.Append(question.Before).Append('\n').Append(before).Append("\n\n");
            if (read.Length > 0) asked.Append(whole ? "What the document says before its first heading:\n\n" : "What it says:\n\n").Append(read).Append("\n\n");
            if (parts.Length > 0) asked.Append(question.Under(whole)).Append("\n\n").Append(parts).Append("\n\n");
            asked.Append(question.Asked(new(whole, read.Length + parts.Length, gist)));

            var instructions = question.Instructions(gist);
            var reply = await model.AnswerAsync(instructions, asked.ToString(), ct);

            if (reply.Ended == DocumentReplyEnd.ContextFull && narrowed < Narrowings)
            {
                room /= 2;
                continue;
            }

            var said = await AnsweredAsync(reply, instructions, asked.ToString(), where, ct);
            await notes.WriteAsync(question.Shelf, key, said, ct);
            return said;
        }
    }

    /// <summary>
    /// Notes on <paramref name="text"/> — too long to read at once — read a stretch of at most <paramref name="room"/> characters
    /// at a time, the notes so far carried into the next: shorter than it, and enough to answer from.
    /// </summary>
    private async Task<string> NotesAsync(DocumentGist gist, string where, string text, int room, CancellationToken ct)
    {
        string? sofar = null;

        foreach (var stretch in DocumentCuts.Cut(text, room))
        {
            var key = DocumentKeys.Of(question.Version, "notes", model.Key, gist.Key, where, sofar ?? string.Empty, stretch);
            if (await notes.ReadAsync(question.Shelf, key, ct) is { } kept)
            {
                sofar = kept;
                continue;
            }

            var asked = new StringBuilder();
            if (where.Length > 0) asked.Append("Where it stands in the document: ").Append(where).Append("\n\n");
            if (sofar is not null) asked.Append("Your notes on it so far:\n").Append(sofar).Append("\n\nIt goes on:\n\n");
            asked.Append(stretch).Append("\n\n").Append(question.Noted(new(false, stretch.Length + (sofar?.Length ?? 0), gist)));

            var instructions = question.Instructions(gist);
            sofar = await AnsweredAsync(await model.AnswerAsync(instructions, asked.ToString(), ct), instructions, asked.ToString(), where, ct);
            await notes.WriteAsync(question.Shelf, key, sofar, ct);
        }

        return sofar ?? string.Empty;
    }

    /// <summary>What a model answered, as the question reads it — asked once more where it cannot be read — or why it could not be worked from.</summary>
    private async Task<string> AnsweredAsync(DocumentReply reply, string instructions, string asked, string where, CancellationToken ct)
    {
        var named = where.Length > 0 ? $"“{where}”" : "the document";

        for (var again = false; ; again = true)
        {
            if (reply.Ended == DocumentReplyEnd.Failed) throw new DocumentWorkException($"The model could not be asked about {named}.");
            if (question.Read(reply.Text) is { } said) return said;
            if (again) throw new DocumentWorkException($"The model did not answer as asked about {named}.");

            reply = await model.AnswerAsync(instructions, asked, ct);
        }
    }
}
