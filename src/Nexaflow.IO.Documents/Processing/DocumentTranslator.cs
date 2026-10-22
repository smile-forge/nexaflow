using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>A document translated (<see cref="DocumentTranslator"/>).</summary>
/// <param name="Text">The translation: the document written again, its words in the other language.</param>
/// <param name="Untranslated">
/// Where the passages kept as the document writes them stand — those no usable translation was had for — each section named
/// once, in the order written; empty where every passage was translated.
/// </param>
public sealed record DocumentTranslation(string Text, IReadOnlyList<string> Untranslated);

/// <summary>
/// Translates a markdown document a passage at a time, writing it again with its words in another language and everything
/// else — its markup, its code, its diagrams, its front matter, where its links go — exactly as it was.
///
/// <para>
/// Each block's words are handed over with everything else held back (<see cref="ProtectedText"/>); blocks are gathered into
/// passages as large as the model can translate at once (<see cref="DocumentBudget"/>), a block too long for one cut at its
/// sentences. Every passage is handed the document's gist (<see cref="DocumentGist"/>), the glossary entries it uses
/// (<see cref="DocumentGlossary"/>) and the passage before it with its translation, so the passages read as one text. An answer
/// is held to twice the length of what it translates. One that does not keep every <c>⟦n⟧</c> once is asked for again, saying
/// what was wrong, then in halves; one that ran out of room, or ran on past its length, is asked for in halves. A block whose
/// markup still does not read as it did once its words are put back is asked for again on its own. One that cannot be
/// translated keeping its markup, or for which no reply could be had, is kept as the document writes it, a note before it
/// saying so, and named in what the translation comes to (<see cref="DocumentTranslation"/>) — the rest of the document is
/// translated all the same, and asking again tries it again. Only a model that cannot be asked at all, its replies failing
/// one after another, stops the job, what was translated before kept, so asking again carries on from there.
/// </para>
/// </summary>
public sealed partial class DocumentTranslator(IDocumentModel model, IDocumentNotes notes, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    private const string Version = "translation-4";

    /// <summary>What a kept answer's pieces are kept apart by.</summary>
    private const char Apart = '\u0002';

    /// <summary>The shortest piece cut in two when no answer for it is right; one shorter is kept as written.</summary>
    private const int ShortestCut = 400;

    /// <summary>How often a unit is asked for before it is asked for another way.</summary>
    private const int Attempts = 2;

    /// <summary>The most replies in a row that may fail — two passages', each asked twice — before the model is taken for gone.</summary>
    private const int FailuresInARow = 2 * Attempts;

    /// <summary>The shelf translations into <paramref name="language"/> are kept on.</summary>
    public static string ShelfFor(string language) => "translation-" + DocumentKeys.Of(language.Trim().ToLowerInvariant())[..12];

    /// <summary><paramref name="reading"/>, which is <paramref name="gist"/>, written again in <paramref name="language"/>.</summary>
    /// <param name="note">What the note before a passage kept as written says, in the reader's language.</param>
    public async Task<DocumentTranslation> TranslateAsync(DocumentReading reading, DocumentGist gist, DocumentGlossary glossary, string language,
                                                          string note, CancellationToken ct)
    {
        var job = new Job(reading, gist, glossary, language, ShelfFor(language), DocumentBudget.Of(model), []);
        var passages = Passages(reading);

        // Each block's words cut where they are too long to hand over at once, then gathered into what can be.
        var room = job.Budget.Share(0.3);
        var pieces = passages.SelectMany(passage => passage.Text.Cut(room).Select(text => new Piece(passage, text))).ToList();
        var units = Units(pieces, room);

        var answers = new Dictionary<Piece, string>();
        (string Source, string Target)? before = null;

        for (var at = 0; at < units.Count; at++)
        {
            progress?.Report(new(DocumentStep.Translation, at, units.Count));

            var answered = await UnitAsync(job, units[at], before, hint: null, ct);
            for (var piece = 0; piece < units[at].Count; piece++) answers[units[at][piece]] = answered[piece];

            // What a passage kept as written says is no lead for the next.
            if (!job.Untranslatable.Contains(units[at][^1].Passage)) before = (units[at][^1].Text, answered[^1]);
        }

        // Every block written again with its words, and asked for again on its own where its markup did not survive.
        var definitions = string.Join("\n", reading.Root.Children.Where(part => part.Kind == MarkdownKinds.Reference).Select(part => part.Print()));
        var written = new Dictionary<ContentPart, string>();

        foreach (var passage in passages)
        {
            var mine = pieces.Where(piece => piece.Passage == passage).ToList();
            var rebuilt = job.Untranslatable.Contains(passage) ? null : passage.Text.Rebuilt(Joined(mine, mine.Select(piece => answers[piece]).ToList()));

            if (rebuilt is not null && !ProtectedText.Keeps(passage.Block, rebuilt, definitions))
            {
                var again = await UnitAsync(job, mine, null, "Keep each <n>…</n> pair round the very words it marked, and each ⟦n⟧ beside the words it was beside.", ct, fresh: true);
                rebuilt = job.Untranslatable.Contains(passage) ? null : passage.Text.Rebuilt(Joined(mine, again));

                if (rebuilt is not null && !ProtectedText.Keeps(passage.Block, rebuilt, definitions))
                {
                    job.Untranslatable.Add(passage);
                    rebuilt = null;
                }
            }

            written[passage.Block] = rebuilt ?? Noted(note, passage);
        }

        progress?.Report(new(DocumentStep.Translation, units.Count, units.Count));

        var document = new StringBuilder(reading.Source.Length);
        foreach (var part in reading.Root.Children) document.Append(written.TryGetValue(part, out var translated) ? translated : part.Print());

        return new DocumentTranslation(Anchored(reading, document.ToString()),
                                       [.. passages.Where(job.Untranslatable.Contains).Select(passage => passage.Where).Distinct()]);
    }

    /// <summary>
    /// A passage kept as the document writes it, the note before it — a paragraph of its own, in emphasis, spelled as words so
    /// nothing in it reads as markup — so a reader finds it where it stands.
    /// </summary>
    private static string Noted(string note, Passage passage) =>
        $"*{MarkdownParser.SpellWords(note, startsLine: false).Trim()}*\n\n{passage.Block.Print()}";

    /// <summary>
    /// <paramref name="translated"/> with every link into the document itself still leading where it led. A heading's name is
    /// made from its words, so a translated heading answers to another: each <c>#name</c> the document writes — in a link, a
    /// picture or a link's definition — is given the name its heading has now, matched by the order the headings are written in,
    /// which a translation keeps.
    /// </summary>
    private static string Anchored(DocumentReading original, string translated)
    {
        var was = original.Sections.All().Select(section => section.Anchor).ToList();
        var now = DocumentReading.Of(translated).Sections.All().Select(section => section.Anchor).ToList();
        if (was.Count != now.Count) throw new InvalidOperationException("A translation's headings are not the document's.");

        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at < was.Count; at++)
            if (was[at] is { } before && now[at] is { } after && before != after) renamed.TryAdd(before, after);

        if (renamed.Count == 0) return translated;

        var written = new StringBuilder(translated.Length);
        var from = 0;

        foreach (var (at, address) in Nexaflow.Markdown.Packaging.MarkdownAttachments.Addresses(translated).OrderBy(found => found.At))
        {
            var bracketed = address.Length > 1 && address[0] == '<' && address[^1] == '>';
            var bare = bracketed ? address[1..^1] : address;
            if (bare.Length < 2 || bare[0] != '#' || !renamed.TryGetValue(Uri.UnescapeDataString(bare[1..]), out var name)) continue;

            written.Append(translated, from, at - from).Append(bracketed ? $"<#{name}>" : $"#{name}");
            from = at + address.Length;
        }

        return written.Append(translated, from, translated.Length - from).ToString();
    }

    /// <summary>
    /// What a unit of pieces comes to in the job's language, a piece each — kept, unless <paramref name="fresh"/>, from when it
    /// was asked before. A mark an answer made up is taken out of it; an answer missing a piece or a <c>⟦n⟧</c> is asked for
    /// again saying what was wrong, then in halves; one that ran out of room is asked for in halves too, and one that could not
    /// be had is asked for again, then kept as written.
    /// </summary>
    private async Task<IReadOnlyList<string>> UnitAsync(Job job, IReadOnlyList<Piece> unit, (string Source, string Target)? before, string? hint,
                                                        CancellationToken ct, bool fresh = false)
    {
        var said = string.Concat(unit.Select(piece => piece.Text));
        var hits = job.Glossary.In(said);
        var key = DocumentKeys.Of(Version, model.Key, job.Gist.Key, job.Language, DocumentGlossary.Said(hits), string.Join(Apart, unit.Select(piece => piece.Text)));

        if (!fresh && await notes.ReadAsync(job.Shelf, key, ct) is { } kept
            && kept.Split(Apart) is { } stored && stored.Length == unit.Count
            && unit.Select((piece, at) => ProtectedText.Problem(piece.Text, stored[at])).All(problem => problem is null))
            return stored;

        string? wrong = null;
        var failed = false;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var reply = await model.AnswerAsync(Instructions(job, hits), Asked(job, unit, before, hint, wrong), Longest(unit), ct);

            // A reply that could not be had costs only its own passage, asked for once more first. Replies failing one after
            // another are a model that cannot be asked at all — one that has answered nothing yet, from its first passage's
            // asks — and stop the job, what was translated kept, so asking again carries on from there.
            failed = reply.Ended == DocumentReplyEnd.Failed;
            if (failed)
            {
                if (++job.FailedInARow >= (job.Answered ? FailuresInARow : Attempts))
                    throw DocumentWorkException.NotAsked($"to translate the passage in “{unit[0].Passage.Where}”", reply);
                continue;
            }

            job.FailedInARow = 0;
            job.Answered = true;

            if (reply.RanOut) return await HalvesAsync(job, unit, before, hint, ct);

            // A mark the model made up is taken out rather than its whole answer turned away.
            var answers = Split(reply.Text, unit.Count) is { } split ? split.Select((answer, at) => ProtectedText.Mended(unit[at].Text, answer)).ToList() : null;
            wrong = answers is null
                ? $"answer with exactly {unit.Count} @@ line{(unit.Count == 1 ? "" : "s")}, numbered from 1, each followed by its translation"
                : unit.Select((piece, at) => ProtectedText.Problem(piece.Text, answers[at]) is { } problem ? $"in @@ {at + 1}, {problem}" : null)
                      .FirstOrDefault(problem => problem is not null);

            if (wrong is null)
            {
                await notes.WriteAsync(job.Shelf, key, string.Join(Apart, answers!), ct);
                return answers!;
            }
        }

        // No reply to be had for it: kept as written, since asking for less of it is no likelier to be answered.
        if (failed) return Untranslatable(job, unit);

        // Asked together and still wrong: in halves, each fewer pieces and marks for a model to keep track of — down to a piece
        // at a time where it takes that, and no further than it takes.
        if (unit.Count > 1)
        {
            var half = unit.Count / 2;
            return [.. await UnitAsync(job, [.. unit.Take(half)], before, hint, ct, fresh), .. await UnitAsync(job, [.. unit.Skip(half)], before, hint, ct, fresh)];
        }

        // One piece still wrong: its halves, cut at a sentence, each with fewer marks to keep — but not one already short, where
        // what makes it hard is its marks, not its length: its passage is kept as written.
        return unit[0].Text.Length < ShortestCut ? Untranslatable(job, unit) : await HalvesAsync(job, unit, before, hint, ct);
    }

    /// <summary>
    /// A unit that ran out of room, or one piece no answer was right for, asked for in two halves — a single piece cut in two at
    /// a sentence, its halves' answers joined back into one; a piece too short to cut has its passage kept as written.
    /// </summary>
    private async Task<IReadOnlyList<string>> HalvesAsync(Job job, IReadOnlyList<Piece> unit, (string Source, string Target)? before, string? hint, CancellationToken ct)
    {
        if (unit.Count > 1)
        {
            var half = unit.Count / 2;
            return [.. await UnitAsync(job, [.. unit.Take(half)], before, hint, ct), .. await UnitAsync(job, [.. unit.Skip(half)], before, hint, ct)];
        }

        var piece = unit[0];
        var cut = DocumentCuts.Cut(piece.Text, Math.Max(1, piece.Text.Length / 2));
        if (cut.Count < 2) return Untranslatable(job, unit);

        var parts = cut.Select(text => new Piece(piece.Passage, text)).ToList();
        var answers = new List<string>();
        foreach (var part in parts) answers.AddRange(await UnitAsync(job, [part], before, hint, ct));

        return [Joined(parts, answers)];
    }

    /// <summary>
    /// Pieces no answer could be found for, their passages kept as written (<see cref="Job.Untranslatable"/>): what stands in for
    /// their answers is never put back, and never kept, so asking again tries them again.
    /// </summary>
    private static IReadOnlyList<string> Untranslatable(Job job, IReadOnlyList<Piece> unit)
    {
        foreach (var piece in unit) job.Untranslatable.Add(piece.Passage);
        return [.. unit.Select(piece => piece.Text)];
    }

    /// <summary>
    /// The most an answer to <paramref name="unit"/> may run to: twice what it translates, with its @@ lines and a little over for
    /// a short one. A translation runs longer than what it translates only so far; one past that is a model saying the same over
    /// and over, and is stopped there, to be asked for in halves.
    /// </summary>
    private static int Longest(IReadOnlyList<Piece> unit) => 2 * unit.Sum(piece => piece.Text.Length) + 16 * unit.Count + 256;

    /// <summary>The answers to pieces cut from one text, joined back into one — a space where the cut fell after one.</summary>
    private static string Joined(IReadOnlyList<Piece> pieces, IReadOnlyList<string> answers)
    {
        var joined = new StringBuilder();

        for (var at = 0; at < answers.Count; at++)
        {
            if (at > 0 && pieces[at - 1].Text.Length > 0 && char.IsWhiteSpace(pieces[at - 1].Text[^1])) joined.Append(' ');
            joined.Append(answers[at].Trim());
        }

        return joined.ToString();
    }

    /// <summary>The pieces gathered, in order, into units of at most <paramref name="room"/> characters.</summary>
    private static List<IReadOnlyList<Piece>> Units(IReadOnlyList<Piece> pieces, int room)
    {
        var units = new List<IReadOnlyList<Piece>>();
        var unit = new List<Piece>();
        var length = 0;

        foreach (var piece in pieces)
        {
            if (unit.Count > 0 && length + piece.Text.Length > room)
            {
                units.Add(unit);
                (unit, length) = ([], 0);
            }

            unit.Add(piece);
            length += piece.Text.Length;
        }

        if (unit.Count > 0) units.Add(unit);
        return units;
    }

    /// <summary>
    /// The blocks with words to translate, each its words with everything else held back and the section it stands in — what a
    /// reader reads; a fence, code, raw HTML, a rule, a page break, front matter and link definitions are copied as written.
    /// </summary>
    private static List<Passage> Passages(DocumentReading reading)
    {
        var where = new string[reading.Root.Children.Count];
        Name(reading.Sections.Sections, null);

        var passages = new List<Passage>();

        for (var at = 0; at < reading.Root.Children.Count; at++)
        {
            var block = reading.Root.Children[at];
            if (block.Derived || block.Role == Roles.Trivia) continue;
            if (block.Kind is Kinds.Block or MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter
                or MarkdownKinds.Reference or MarkdownKinds.Rule or MarkdownKinds.Page) continue;

            var text = ProtectedText.Of(block);
            if (text.HasWords) passages.Add(new Passage(block, text, where[at] ?? reading.Title ?? "the opening"));
        }

        return passages;

        void Name(IReadOnlyList<MarkdownSection> sections, string? above)
        {
            foreach (var section in sections)
            {
                var title = DocumentReading.Words(reading.Part(section.Heading));
                var path = above is null ? title : $"{above} › {title}";

                for (var at = section.Heading; at < section.End; at++) where[at] = path;
                Name(section.Sections, path);
            }
        }
    }

    /// <summary>What the model is asked: the passage before and its translation, then each piece after an @@ line of its own.</summary>
    private static string Asked(Job job, IReadOnlyList<Piece> unit, (string Source, string Target)? before, string? hint, string? wrong)
    {
        var asked = new StringBuilder();

        if (before is { } previous)
        {
            var room = job.Budget.Share(0.05);
            asked.Append("The passage before, and how you translated it:\n")
                 .Append(DocumentCuts.Cut(previous.Source, room)[^1]).Append("\n—\n")
                 .Append(DocumentCuts.Cut(previous.Target, room)[^1]).Append("\n\n");
        }

        asked.Append($"Translate each of these into {job.Language}:\n\n");
        for (var at = 0; at < unit.Count; at++) asked.Append("@@ ").Append(at + 1).Append('\n').Append(unit[at].Text.Trim()).Append("\n\n");

        asked.Append("Answer with the same @@ lines, each followed by its translation, and nothing else.");
        if (hint is not null) asked.Append(' ').Append(hint);
        if (wrong is not null) asked.Append($"\n\nYour last answer was wrong: {wrong}. Answer again.");

        return asked.ToString();
    }

    private static string Instructions(Job job, IReadOnlyList<(string Source, string Target)> hits)
    {
        var said = new StringBuilder()
            .Append($"You translate a document into {job.Language} a passage at a time. The whole document is:\n{job.Gist.Said}\n\n")
            .Append($"Translate as its author would have written it in {job.Language}: keep its register, tone, voice and tense, for the ")
            .Append("same audience, so that the passages read as one text. ");

        if (hits.Count > 0) said.Append("Give these words this way wherever they occur:\n").Append(DocumentGlossary.Said(hits)).Append("\n\n");

        return said.Append("Each <n>…</n> pair marks the words between its tags — a link, emphasis: keep every pair round the translation of ")
                   .Append($"those words, wherever they go in natural {job.Language}, one inside another as they were. Each ⟦n …⟧ is code or a ")
                   .Append("formula, shown after its number: do not translate it, and place it where it belongs in the sentence. Each ⟦n⟧ alone ")
                   .Append("stands for something you cannot see — a picture, longer code: keep it with the words it belongs to. Keep every tag ")
                   .Append("and every ⟦n⟧ exactly once, and add none. Translate everything else. Add nothing, explain nothing.")
                   .ToString();
    }

    /// <summary>The answer's pieces, in order — null where it does not have exactly <paramref name="count"/>, numbered from one.</summary>
    private static IReadOnlyList<string>? Split(string reply, int count)
    {
        var marks = Opens().Matches(reply);

        if (marks.Count == 0) return count == 1 && reply.Trim().Length > 0 ? [reply.Trim()] : null;
        if (marks.Count != count) return null;

        var answers = new string[count];

        for (var at = 0; at < count; at++)
        {
            if (!int.TryParse(marks[at].Groups[1].Value, out var numbered) || numbered != at + 1) return null;

            var from = marks[at].Index + marks[at].Length;
            var to = at + 1 < count ? marks[at + 1].Index : reply.Length;
            answers[at] = reply[from..to].Trim();
        }

        return answers;
    }

    [GeneratedRegex(@"^[ \t]*@@[ \t]*(\d+)[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex Opens();

    /// <summary>
    /// What every question of one translation shares, the passages it found no answer for that kept their markup, and how its
    /// replies have gone: whether any was had, and how many have failed since the last that was.
    /// </summary>
    private sealed record Job(DocumentReading Reading, DocumentGist Gist, DocumentGlossary Glossary, string Language, string Shelf, DocumentBudget Budget,
                              HashSet<Passage> Untranslatable)
    {
        public bool Answered { get; set; }

        public int FailedInARow { get; set; }
    }

    /// <summary>A block with words to translate: the block, its words with the rest held back, and the section it stands in.</summary>
    private sealed record Passage(ContentPart Block, ProtectedText Text, string Where);

    /// <summary>
    /// A passage's words, or as much of them as is handed over at once — itself, not what it says: two pieces saying the same
    /// are still two answers.
    /// </summary>
    private sealed class Piece(Passage passage, string text)
    {
        public Passage Passage => passage;

        public string Text => text;
    }
}
