using System.Collections.Concurrent;
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
/// passages of a working size, smaller where the model reads less (<see cref="DocumentBudget"/>), a block too long for one cut
/// at its sentences, and never across a chapter — the document's outermost sections, which are translated side by side, each
/// read through in order. Every passage is handed the document's gist (<see cref="DocumentGist"/>), the glossary entries it
/// uses (<see cref="DocumentGlossary"/>) and the passage before it in its chapter with its translation, so the passages read as
/// one text. An answer is held to twice the length of what it translates, and taken piece by piece: what it answers right is
/// kept, and only a piece it leaves out or gets wrong is asked for again, so one piece costs only itself. A piece with a mark
/// doubled or missing has just the sentences round it asked for again and put back (<see cref="ProtectedText.Wrong"/>); one
/// still wrong is asked for again, saying what was wrong, then in halves; one that ran out of room, or ran on past its length,
/// is asked for in halves. A block whose
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
    private const string Version = "translation-6";

    /// <summary>What a kept answer's pieces are kept apart by.</summary>
    private const char Apart = '\u0002';

    /// <summary>The shortest piece cut in two when no answer for it is right; one shorter is kept as written.</summary>
    private const int ShortestCut = 400;

    /// <summary>How often a unit is asked for before it is asked for another way.</summary>
    private const int Attempts = 2;

    /// <summary>
    /// The most replies in a row one chapter may have fail — two passages', each asked twice — before the model is taken for gone.
    /// </summary>
    private const int FailuresInARow = 2 * Attempts;

    /// <summary>
    /// The most a unit is handed at once, in characters, however much more the model reads: a unit is asked for again whole when
    /// its answer will not do, and one this size costs seconds, while the instructions it is handed with stay a small share.
    /// </summary>
    private const int MostTranslated = 4000;

    /// <summary>How many chapters are translated side by side — a model serving several replies at once writes them together.</summary>
    private const int ChaptersAtOnce = 4;

    /// <summary>How many times a unit's answer is mended a stretch at a time before it is asked for whole.</summary>
    private const int MostRepairs = 3;

    /// <summary>The shelf translations into <paramref name="language"/> are kept on.</summary>
    public static string ShelfFor(string language) => "translation-" + DocumentKeys.Of(language.Trim().ToLowerInvariant())[..12];

    /// <summary><paramref name="reading"/>, which is <paramref name="gist"/>, written again in <paramref name="language"/>.</summary>
    /// <param name="note">What the note before a passage kept as written says, in the reader's language.</param>
    public async Task<DocumentTranslation> TranslateAsync(DocumentReading reading, DocumentGist gist, DocumentGlossary glossary, string language,
                                                          string note, CancellationToken ct)
    {
        var job = new Job(reading, gist, glossary, language, ShelfFor(language), DocumentBudget.Of(model), new Shared());
        var passages = Passages(reading);

        // Each block's words cut where they are too long to hand over at once, then gathered into what can be — within a chapter,
        // never across one.
        var room = Math.Min(job.Budget.Share(0.3), MostTranslated);
        var chapters = passages.GroupBy(passage => passage.Chapter)
                               .Select(chapter => Units([.. chapter.SelectMany(passage => passage.Text.Cut(room).Select(text => new Piece(passage, text)))], room))
                               .ToList();
        var pieces = chapters.SelectMany(units => units).SelectMany(unit => unit).ToList();
        var count = chapters.Sum(units => units.Count);

        var answers = new ConcurrentDictionary<Piece, string>();
        var done = 0;
        progress?.Report(new(DocumentStep.Translation, 0, count));

        // Chapters side by side, each read through in order as a reader goes through it, its passages each led into by the one
        // before and its translation; a chapter's failures in a row are its own, so one reply lost by them all is no streak.
        await Parallel.ForEachAsync(chapters, new ParallelOptions { MaxDegreeOfParallelism = ChaptersAtOnce, CancellationToken = ct }, async (units, token) =>
        {
            var run = job with { FailedInARow = 0 };
            (string Source, string Target)? before = null;

            foreach (var unit in units)
            {
                var answered = await UnitAsync(run, unit, before, hint: null, token);
                for (var piece = 0; piece < unit.Count; piece++) answers[unit[piece]] = answered[piece];

                // What a passage kept as written says is no lead for the next.
                if (!job.Shared.IsKeptAsWritten(unit[^1].Passage)) before = (unit[^1].Text, answered[^1]);
                progress?.Report(new(DocumentStep.Translation, Interlocked.Increment(ref done), count));
            }
        });

        // Nothing answered at all, whatever the streaks came to: a model that cannot be asked.
        if (job.Shared.Unanswered is { } unanswered) throw DocumentWorkException.NotAsked($"to translate the passage in “{unanswered.Where}”", unanswered.Reply);

        // Every block written again with its words, and asked for again on its own where its markup did not survive.
        var definitions = string.Join("\n", reading.Root.Children.Where(part => part.Kind == MarkdownKinds.Reference).Select(part => part.Print()));
        var written = new Dictionary<ContentPart, string>();

        foreach (var passage in passages)
        {
            var mine = pieces.Where(piece => piece.Passage == passage).ToList();
            var rebuilt = job.Shared.IsKeptAsWritten(passage) ? null : passage.Text.Rebuilt(Joined(mine, mine.Select(piece => answers[piece]).ToList()));

            if (rebuilt is not null && !ProtectedText.Keeps(passage.Block, rebuilt, definitions))
            {
                var again = await UnitAsync(job, mine, null, "Keep each <n>…</n> pair round the very words it marked, and each ⟦n⟧ beside the words it was beside.", ct, fresh: true);
                rebuilt = job.Shared.IsKeptAsWritten(passage) ? null : passage.Text.Rebuilt(Joined(mine, again));

                if (rebuilt is not null && !ProtectedText.Keeps(passage.Block, rebuilt, definitions))
                {
                    job.Shared.KeepAsWritten(passage);
                    rebuilt = null;
                }
            }

            written[passage.Block] = rebuilt ?? Noted(note, passage);
        }

        var document = new StringBuilder(reading.Source.Length);
        foreach (var part in reading.Root.Children) document.Append(written.TryGetValue(part, out var translated) ? translated : part.Print());

        return new DocumentTranslation(Anchored(reading, document.ToString()),
                                       [.. passages.Where(job.Shared.IsKeptAsWritten).Select(passage => passage.Where).Distinct()]);
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
    /// was asked before. However its answer was come to (<see cref="AnswersAsync"/>), one that translates every piece is kept as
    /// the unit's own, so asking again reads it straight back rather than asking the model the whole unit before its halves.
    /// </summary>
    private async Task<IReadOnlyList<string>> UnitAsync(Job job, IReadOnlyList<Piece> unit, (string Source, string Target)? before, string? hint,
                                                        CancellationToken ct, bool fresh = false)
    {
        // Each piece its own text: run together, the last word of one and the first of the next would read as one word.
        var hits = job.Glossary.In(string.Join('\n', unit.Select(piece => piece.Text)));
        var key = DocumentKeys.Of(Version, model.Key, job.Gist.Key, job.Language, DocumentGlossary.Said(hits), string.Join(Apart, unit.Select(piece => piece.Text)));

        if (!fresh && await notes.ReadAsync(job.Shelf, key, ct) is { } kept
            && kept.Split(Apart) is { } stored && stored.Length == unit.Count
            && unit.Select((piece, at) => ProtectedText.Problem(piece.Text, stored[at])).All(problem => problem is null))
            return stored;

        var answers = await AnswersAsync(job, unit, hits, before, hint, ct, fresh);
        if (!unit.Any(piece => job.Shared.IsKeptAsWritten(piece.Passage))) await notes.WriteAsync(job.Shelf, key, string.Join(Apart, answers), ct);

        return answers;
    }

    /// <summary>
    /// A unit's answers asked of the model. A mark an answer made up is taken out of it; an answer with a mark wrong has the
    /// sentences round it asked for again (<see cref="RepairedAsync"/>), and one still wrong, or missing a piece, is asked for
    /// again saying what was wrong, then in halves; one that ran out of room is asked for in halves too, and one that could not
    /// be had is asked for again, then kept as written.
    /// </summary>
    private async Task<IReadOnlyList<string>> AnswersAsync(Job job, IReadOnlyList<Piece> unit, IReadOnlyList<(string Source, string Target)> hits,
                                                           (string Source, string Target)? before, string? hint, CancellationToken ct, bool fresh)
    {
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
                job.Shared.Failed(unit[0].Passage.Where, reply);
                if (++job.FailedInARow >= FailuresInARow)
                    throw DocumentWorkException.NotAsked($"to translate the passage in “{unit[0].Passage.Where}”", reply);
                continue;
            }

            job.FailedInARow = 0;
            job.Shared.Answered();

            if (reply.RanOut) return await HalvesAsync(job, unit, before, hint, ct);

            // A mark the model made up is taken out rather than its whole answer turned away.
            var answers = AnswersByNumber(reply.Text, unit.Count)
                .Select((answer, at) => answer is null ? null : ProtectedText.Mended(unit[at].Text, answer)).ToList();
            var right = answers.Select((answer, at) => answer is not null && ProtectedText.Problem(unit[at].Text, answer) is null).ToList();

            if (answers.All(answer => answer is not null))
            {
                var whole = answers.Select(answer => answer!).ToList();
                if (right.All(piece => piece)) return whole;

                // A mark or two wrong in an answer otherwise whole: just the sentences round them asked for again.
                if (await RepairedAsync(job, unit, whole, hits, hint, ct) is { } repaired) return repaired;
            }

            // Some of it right: that is kept, and only what is missing or still wrong is asked for again — on its own, with all the
            // asking again a passage gets — so a piece left out, or one the model cannot get right, costs only itself.
            if (right.Any(piece => piece)) return await KeptAndRestAskedAsync(job, unit, answers, right, before, hint, ct, fresh);

            wrong = answers.All(answer => answer is null)
                ? $"answer with exactly {unit.Count} @@ line{(unit.Count == 1 ? "" : "s")}, numbered from 1, each on a line of its own and followed by its translation"
                : answers.Select((answer, at) => answer is not null && ProtectedText.Problem(unit[at].Text, answer) is { } problem ? $"in @@ {at + 1}, {problem}" : null)
                         .FirstOrDefault(problem => problem is not null);
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
    /// The pieces of <paramref name="unit"/> answered <paramref name="right"/> kept as answered, and the rest asked for again as a
    /// passage of their own — fewer, so this always comes to an end — each put back where it stood. A piece answered wrong is asked
    /// for saying what was wrong with it, numbered as it is asked now; one left out is simply asked for.
    /// </summary>
    private async Task<IReadOnlyList<string>> KeptAndRestAskedAsync(Job job, IReadOnlyList<Piece> unit, IReadOnlyList<string?> answers, IReadOnlyList<bool> right,
                                                                     (string Source, string Target)? before, string? hint, CancellationToken ct, bool fresh)
    {
        var again = Enumerable.Range(0, unit.Count).Where(at => !right[at]).ToList();

        var wrong = again.Select((at, asked) => answers[at] is { } answer && ProtectedText.Problem(unit[at].Text, answer) is { } problem
                                                    ? $"in @@ {asked + 1}, {problem}" : null)
                         .OfType<string>().ToList();
        var said = wrong.Count == 0 ? hint : $"{hint} Your last answer was wrong: {string.Join("; ", wrong)}.".Trim();

        var answered = await UnitAsync(job, [.. again.Select(at => unit[at])], before, said, ct, fresh);

        var whole = new string[unit.Count];
        var next = 0;

        for (var at = 0; at < unit.Count; at++) whole[at] = right[at] ? answers[at]! : answered[next++];

        return whole;
    }

    /// <summary>
    /// <paramref name="answers"/> with what is wrong in each asked for again on its own and put back where it stood
    /// (<see cref="ProtectedText.Wrong"/>) — the sentences round a doubled or missing mark, the rest of the answer kept. Null
    /// where what is wrong takes in most of its piece, where it is still wrong asked for again, or where it keeps turning up, for
    /// the unit to be asked for whole.
    /// </summary>
    private async Task<IReadOnlyList<string>?> RepairedAsync(Job job, IReadOnlyList<Piece> unit, List<string> answers,
                                                             IReadOnlyList<(string Source, string Target)> hits, string? hint, CancellationToken ct)
    {
        for (var at = 0; at < unit.Count; at++)
        {
            var source = unit[at].Text;

            for (var mended = 0; ProtectedText.Problem(source, answers[at]) is { } problem; mended++)
            {
                if (mended == MostRepairs || ProtectedText.Wrong(source, answers[at]) is not { } wrong || wrong.SourceLength * 2 > source.Length) return null;

                var answer = answers[at];
                var stretch = new Piece(unit[at].Passage, source[wrong.SourceFrom..wrong.SourceTo]);
                (string Source, string Target)? before = wrong.SourceFrom > 0 ? (source[..wrong.SourceFrom], answer[..wrong.AnswerFrom]) : null;

                var reply = await model.AnswerAsync(Instructions(job, hits), Asked(job, [stretch], before, hint, problem), Longest([stretch]), ct);
                if (reply.Ended == DocumentReplyEnd.Failed || reply.RanOut || AnswersByNumber(reply.Text, 1) is not [{ } given]) return null;

                var again = ProtectedText.Mended(stretch.Text, given);
                if (ProtectedText.Problem(stretch.Text, again) is not null) return null;

                // Put back as the sentences it stands for were: the space after them after it.
                var replaced = answer[wrong.AnswerFrom..wrong.AnswerTo];
                answers[at] = answer[..wrong.AnswerFrom] + again.Trim() + replaced[replaced.TrimEnd().Length..] + answer[wrong.AnswerTo..];
            }
        }

        return answers;
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
    /// Pieces no answer could be found for, their passages kept as written (<see cref="Shared.KeepAsWritten"/>): what stands in for
    /// their answers is never put back, and never kept, so asking again tries them again.
    /// </summary>
    private static IReadOnlyList<string> Untranslatable(Job job, IReadOnlyList<Piece> unit)
    {
        foreach (var piece in unit) job.Shared.KeepAsWritten(piece.Passage);
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

        // Its chapters: the outermost level where it has more than one section — under a title of its own, the sections under it.
        var chapter = new int[reading.Root.Children.Count];
        var outermost = reading.Sections.Sections;
        while (outermost is [{ Sections.Count: > 0 } only]) outermost = only.Sections;
        for (var at = 0; at < outermost.Count; at++)
            for (var block = outermost[at].Heading; block < outermost[at].End; block++) chapter[block] = at + 1;

        var passages = new List<Passage>();

        for (var at = 0; at < reading.Root.Children.Count; at++)
        {
            var block = reading.Root.Children[at];
            if (block.Derived || block.Role == Roles.Trivia) continue;
            if (block.Kind is Kinds.Block or MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter
                or MarkdownKinds.Reference or MarkdownKinds.Rule or MarkdownKinds.Page) continue;

            var text = ProtectedText.Of(block);
            if (text.HasWords) passages.Add(new Passage(block, text, where[at] ?? reading.Title ?? "the opening", chapter[at]));
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

        asked.Append("Answer with the same @@ lines, each on a line of its own and followed by its translation on the lines after it, and nothing else.");
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
                   .Append("and every ⟦n⟧ exactly once, and add none: a tag is only ever a number, so put none of your own round a name, a ")
                   .Append("term or a formula. Translate everything else. Add nothing, explain nothing.")
                   .ToString();
    }

    /// <summary>
    /// Each piece's answer in <paramref name="reply"/>, by the number its @@ line gives it — the answer on the lines after that line,
    /// or on the same line after the number, as a model writes it either way — and null for a piece it gives no answer, or more than
    /// one: what came back is taken piece by piece, so one piece left out costs only itself. Numbers past the pieces asked, and
    /// anything before the first @@ line, are nobody's. A reply to one piece with no @@ line at all is that piece's answer.
    /// </summary>
    private static string?[] AnswersByNumber(string reply, int count)
    {
        var answers = new string?[count];
        var marks = Opens().Matches(reply);

        if (marks.Count == 0)
        {
            if (count == 1 && reply.Trim() is { Length: > 0 } whole) answers[0] = whole;
            return answers;
        }

        var given = new int[count];

        for (var at = 0; at < marks.Count; at++)
        {
            if (!int.TryParse(marks[at].Groups[1].Value, out var numbered) || numbered < 1 || numbered > count) continue;

            var from = marks[at].Index + marks[at].Length;
            var to = at + 1 < marks.Count ? marks[at + 1].Index : reply.Length;

            given[numbered - 1]++;
            answers[numbered - 1] = reply[from..to].Trim() is { Length: > 0 } said ? said : null;
        }

        // Answered twice is not answered: which of the two was meant is not the reader's to guess.
        for (var piece = 0; piece < count; piece++)
            if (given[piece] > 1) answers[piece] = null;

        return answers;
    }

    [GeneratedRegex(@"^[ \t]*@@[ \t]*(\d+)\b[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Opens();

    /// <summary>
    /// What every question of one translation shares, and what one chapter's run of it keeps to itself: how many of its replies
    /// have failed since the last that was had.
    /// </summary>
    private sealed record Job(DocumentReading Reading, DocumentGist Gist, DocumentGlossary Glossary, string Language, string Shelf, DocumentBudget Budget,
                              Shared Shared)
    {
        public int FailedInARow { get; set; }
    }

    /// <summary>
    /// What the chapters translated side by side share: the passages kept as written, those no answer that kept their markup
    /// was had for; and whether the model has answered at all, with the first reply that failed while it has not.
    /// </summary>
    private sealed class Shared
    {
        private readonly Lock _gate = new();
        private readonly HashSet<Passage> _keptAsWritten = [];
        private bool _answered;
        private (string Where, DocumentReply Reply)? _failed;

        public void KeepAsWritten(Passage passage)
        {
            lock (_gate) _keptAsWritten.Add(passage);
        }

        public bool IsKeptAsWritten(Passage passage)
        {
            lock (_gate) return _keptAsWritten.Contains(passage);
        }

        public void Answered()
        {
            lock (_gate) _answered = true;
        }

        public void Failed(string where, DocumentReply reply)
        {
            lock (_gate) _failed ??= (where, reply);
        }

        /// <summary>The first reply that failed, where none was ever had.</summary>
        public (string Where, DocumentReply Reply)? Unanswered
        {
            get { lock (_gate) return _answered ? null : _failed; }
        }
    }

    /// <summary>
    /// A block with words to translate: the block, its words with the rest held back, the section it stands in, and the chapter
    /// — which of the document's outermost sections, 0 for none — it is translated in turn with.
    /// </summary>
    private sealed record Passage(ContentPart Block, ProtectedText Text, string Where, int Chapter);

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
