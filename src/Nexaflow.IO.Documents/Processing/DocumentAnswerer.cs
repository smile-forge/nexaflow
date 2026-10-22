using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>A part of a document a question was answered from.</summary>
/// <param name="Number">What the answer cites it by, as <c>[n]</c>.</param>
/// <param name="Where">Where it stands among the headings; empty for what comes before the first.</param>
/// <param name="Section">Its section, or null for what comes before the first heading.</param>
/// <param name="Cited">Whether the answer cites it.</param>
public sealed record DocumentAnswerPart(int Number, string Where, MarkdownSection? Section, bool Cited);

/// <summary>What a question about a whole document was answered with, and the parts of it the answer was read from.</summary>
/// <param name="Said">The answer, citing the parts it comes from as <c>[n]</c>; empty where no part bears on the question.</param>
/// <param name="Parts">The parts read, in the order the document has them.</param>
/// <param name="InBrief">Whether the parts were read from what was said of them in brief, being too much to read in full.</param>
public sealed record DocumentAnswer(string Said, IReadOnlyList<DocumentAnswerPart> Parts, bool InBrief);

/// <summary>
/// Answers a question about a whole document a model cannot read at once — which chapters deal with something, what it says
/// of something all through it — by going down what a section pass said of its sections (<see cref="DocumentRecord"/>) to the
/// parts that bear on the question, then reading those.
///
/// <para>
/// Going down, it shows the model what was said in brief of the parts at one level — what a section says before the sections
/// under it, and each of those — and asks which bear on the question; a part chosen is read whole where it is short, gone down
/// into where it is not. So a question costs a few short questions a level, however long the document, and a part is never read
/// unless it was chosen. A level with one part to choose from is gone down without asking.
/// </para>
///
/// <para>
/// Reading, it hands the model the parts chosen, numbered, in the order the document has them, a reading at a time sized to it
/// (<see cref="DocumentBudget"/>), the answer so far carried into the next; what is too much to read in a few readings is read
/// from what was said of each part in brief instead. The answer cites the parts it comes from by number, so each can be led
/// back to. Every question is handed what the document is (<see cref="DocumentGist"/>), so it is answered as the one it is.
/// </para>
/// </summary>
public sealed partial class DocumentAnswerer(IDocumentModel model, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>How many readings the parts chosen are read in, at most, before they are read from what was said of them in brief.</summary>
    public const int MostReadings = 4;

    /// <summary>How many times a question that filled what the model reads is asked again with half as much.</summary>
    private const int Narrowings = 3;

    /// <summary>The most of what a section says before the sections under it that is shown to choose it by.</summary>
    private const int MostShown = 600;

    /// <summary>The words an answer naming parts may put between their numbers.</summary>
    private static readonly HashSet<string> Joining = new(["and", "or", "part", "parts", "number", "numbers"], StringComparer.OrdinalIgnoreCase);

    private int _asked;

    /// <summary>What <paramref name="question"/> is answered with from <paramref name="reading"/>, which <paramref name="record"/> was made of.</summary>
    public async Task<DocumentAnswer> AnswerAsync(DocumentReading reading, DocumentRecord record, string question, CancellationToken ct)
    {
        _asked = 0;

        var chosen = await ChooseAsync(reading, record, question, ct);
        if (chosen.Count == 0)
        {
            progress?.Report(new(DocumentStep.Question, _asked, _asked));
            return new DocumentAnswer(string.Empty, [], InBrief: false);
        }

        var (said, inBrief) = await ReadAsync(reading, record.Gist, chosen, question, ct);
        progress?.Report(new(DocumentStep.Question, _asked, _asked));

        var cited = Cited(said);
        return new DocumentAnswer(said, [.. chosen.Select((part, at) => new DocumentAnswerPart(at + 1, part.Where, part.Node.Section, cited.Contains(at + 1)))], inBrief);
    }

    /// <summary>The parts that bear on <paramref name="question"/>, in the order the document has them, found going down from the whole.</summary>
    private async Task<List<Chosen>> ChooseAsync(DocumentReading reading, DocumentRecord record, string question, CancellationToken ct)
    {
        var whole = DocumentBudget.Of(model).Share(0.25);
        var chosen = new List<Chosen>();
        var waiting = new Queue<(SectionResult Node, string Where)>([(record.Said, string.Empty)]);

        while (waiting.Count > 0)
        {
            var (node, where) = waiting.Dequeue();
            var options = Options(reading, node, where);
            if (options.Count == 0) continue;

            progress?.Report(new(DocumentStep.Question, _asked, _asked + waiting.Count + 2));
            var picked = options.Count == 1 ? options : await PickAsync(record.Gist, question, where, options, ct);

            foreach (var option in picked)
            {
                if (option.Own) chosen.Add(new Chosen(node, Own: true, where));
                else if (option.Node.Under.Count == 0 || Text(reading, option.Node, own: false).Length <= whole) chosen.Add(new Chosen(option.Node, Own: false, option.Where));
                else waiting.Enqueue((option.Node, option.Where));
            }
        }

        return [.. chosen.OrderBy(part => part.Node.Section?.Heading ?? -1).ThenBy(part => part.Own ? 0 : 1)];
    }

    /// <summary>What there is to choose from at <paramref name="node"/>: what it says before the sections under it, and each of those that says anything.</summary>
    private static List<Option> Options(DocumentReading reading, SectionResult node, string where)
    {
        var options = new List<Option>();

        if (Text(reading, node, own: true) is { Length: > 0 } own)
        {
            var shown = DocumentCuts.Cut(own, MostShown)[0];
            options.Add(new Option(node, Own: true, where, node.Section is null ? "Before its first heading" : "Before the sections under it",
                                   shown.Length < own.Length ? shown + " …" : shown));
        }

        foreach (var under in node.Under)
            if (under.Said.Length > 0)
                options.Add(new Option(under, Own: false, where.Length > 0 ? $"{where} › {under.Title}" : under.Title, under.Title, under.Said));

        return options;
    }

    /// <summary>Which of <paramref name="options"/> bear on <paramref name="question"/>, asked as many at a time as fit.</summary>
    private async Task<List<Option>> PickAsync(DocumentGist gist, string question, string where, List<Option> options, CancellationToken ct)
    {
        var room = DocumentBudget.Of(model).Share(0.5);

        for (var narrowed = 0; ; narrowed++)
        {
            var picked = new List<Option>();
            var full = false;

            foreach (var batch in Batches(options, room))
            {
                var asked = new StringBuilder().Append("The question: ").Append(question).Append("\n\n");
                if (where.Length > 0) asked.Append("Where these parts stand in the document: ").Append(where).Append("\n\n");
                asked.Append("The parts, each said in brief:\n\n");
                for (var at = 0; at < batch.Count; at++) asked.Append(at + 1).Append(". ").Append(batch[at].Label).Append(":\n").Append(batch[at].Brief).Append("\n\n");
                asked.Append("Which of these parts would answer the question, or help to? Answer with their numbers, one a line, or NONE where none would.");

                var instructions = Choosing(gist);
                var reply = await model.AnswerAsync(instructions, asked.ToString(), ct);
                _asked++;

                if (reply.Ended == DocumentReplyEnd.ContextFull && narrowed < Narrowings)
                {
                    full = true;
                    break;
                }

                var numbers = Picked(reply, batch.Count);
                if (numbers is null)
                {
                    reply = await model.AnswerAsync(instructions, asked + "\n\nAnswer with the numbers only, one a line, or NONE.", ct);
                    _asked++;
                    numbers = Picked(reply, batch.Count);
                }

                if (numbers is null)
                    throw new DocumentWorkException(reply.Ended == DocumentReplyEnd.Failed
                        ? "The model could not be asked which parts of the document answer the question."
                        : "The model did not say which parts of the document answer the question.");

                picked.AddRange(numbers.Select(number => batch[number - 1]));
            }

            if (!full) return picked;
            room /= 2;
        }
    }

    /// <summary><paramref name="options"/> gathered, in order, into batches of at most <paramref name="room"/> characters — one at least to a batch.</summary>
    private static IEnumerable<List<Option>> Batches(List<Option> options, int room)
    {
        var batch = new List<Option>();
        var length = 0;

        foreach (var option in options)
        {
            var size = option.Label.Length + option.Brief.Length + 8;
            if (batch.Count > 0 && length + size > room)
            {
                yield return batch;
                batch = [];
                length = 0;
            }

            batch.Add(option);
            length += size;
        }

        if (batch.Count > 0) yield return batch;
    }

    /// <summary>
    /// The parts' numbers an answer names, from 1 to <paramref name="of"/>: on each line, the numbers it opens with — what
    /// follows a word other than one joining them, such as a part's title, is not read. Empty where it says NONE; null where it
    /// says neither.
    /// </summary>
    private static List<int>? Picked(DocumentReply reply, int of)
    {
        if (reply.Ended == DocumentReplyEnd.Failed) return null;

        var picked = new List<int>();
        var none = false;

        foreach (var line in reply.Text.Split('\n'))
            foreach (Match token in Token().Matches(line))
            {
                if (int.TryParse(token.Value, out var number))
                {
                    if (number >= 1 && number <= of && !picked.Contains(number)) picked.Add(number);
                    continue;
                }

                if (token.Value.Equals("NONE", StringComparison.OrdinalIgnoreCase)) none = true;
                if (!Joining.Contains(token.Value)) break;
            }

        return picked.Count > 0 || none ? picked : null;
    }

    /// <summary>
    /// The answer to <paramref name="question"/> from <paramref name="chosen"/>, read a reading at a time, and whether it was read
    /// from what was said of them in brief.
    /// </summary>
    private async Task<(string Said, bool InBrief)> ReadAsync(DocumentReading reading, DocumentGist gist, List<Chosen> chosen, string question, CancellationToken ct)
    {
        var room = DocumentBudget.Of(model).Share(0.55);
        var texts = chosen.Select(part => Text(reading, part.Node, part.Own)).ToList();
        var inBrief = texts.Sum(text => text.Length) > room * MostReadings;
        // A part read in brief is what was said of it — of a section's own opening, the start of what it says.
        if (inBrief) texts = [.. chosen.Select((part, at) => part.Own ? DocumentCuts.Cut(texts[at], MostShown)[0] : part.Node.Said)];

        for (var narrowed = 0; ; narrowed++)
        {
            string? sofar = null;
            var full = false;
            var readings = Readings(chosen, texts, room);

            for (var at = 0; at < readings.Count; at++)
            {
                var asked = new StringBuilder().Append("The question: ").Append(question).Append("\n\n");
                if (sofar is not null) asked.Append("Your answer so far, from the parts before these:\n").Append(sofar).Append("\n\nMore parts of the document:\n\n");
                else asked.Append(inBrief ? "The parts of the document that bear on it, each said in brief:\n\n" : "The parts of the document that bear on it:\n\n");
                asked.Append(readings[at]).Append("\n\n");
                asked.Append(sofar is null ? "Answer the question from these parts." : "Answer the question again, from your answer so far and these parts, keeping all of it that still holds.")
                     .Append(" Cite each part you use by its number, as [2].");

                var instructions = Answering(gist);
                var reply = await model.AnswerAsync(instructions, asked.ToString(), ct);
                _asked++;
                progress?.Report(new(DocumentStep.Question, _asked, _asked + readings.Count - at));

                if (reply.Ended == DocumentReplyEnd.ContextFull && narrowed < Narrowings)
                {
                    full = true;
                    break;
                }

                if (reply.Ended != DocumentReplyEnd.Failed && reply.Text.Trim().Length == 0)
                {
                    reply = await model.AnswerAsync(instructions, asked.ToString(), ct);
                    _asked++;
                }

                if (reply.Ended == DocumentReplyEnd.Failed) throw new DocumentWorkException("The model could not be asked the question.");
                if (reply.Text.Trim() is not { Length: > 0 } said) throw new DocumentWorkException("The model did not answer the question.");

                sofar = said;
            }

            if (!full) return (sofar ?? string.Empty, inBrief);
            room /= 2;
        }
    }

    /// <summary>
    /// The parts, numbered, gathered in order into readings of at most <paramref name="room"/> characters; a part longer than
    /// that is read a stretch at a time, each stretch under its number.
    /// </summary>
    private static List<string> Readings(List<Chosen> chosen, List<string> texts, int room)
    {
        var pieces = new List<string>();

        for (var at = 0; at < chosen.Count; at++)
        {
            var named = $"[{at + 1}] {(chosen[at].Where.Length > 0 ? chosen[at].Where : "Before its first heading")}" +
                        (chosen[at].Own && chosen[at].Node.Under.Count > 0 && chosen[at].Node.Section is not null ? ", before the sections under it" : string.Empty) + ":\n";

            foreach (var stretch in DocumentCuts.Cut(texts[at], Math.Max(room / 4, room - named.Length - 2)))
                pieces.Add(named + stretch);
        }

        return [.. DocumentCuts.Pack(pieces, room)];
    }

    /// <summary>The parts an answer cites, as <c>[n]</c> or <c>[n, m]</c>.</summary>
    private static HashSet<int> Cited(string said) =>
        [.. Citation().Matches(said).SelectMany(match => match.Groups[1].Value.Split([',', ';'], StringSplitOptions.TrimEntries))
                      .Select(number => int.TryParse(number, out var cited) ? cited : 0)];

    /// <summary>
    /// What <paramref name="node"/> says: what stands before the sections under it, where <paramref name="own"/>, or all of it —
    /// the whole document's, for its root, being what comes before its first heading.
    /// </summary>
    private static string Text(DocumentReading reading, SectionResult node, bool own) => node.Section is { } section
        ? reading.Text(section.Heading + 1, own ? section.Body : section.End)
        : reading.Text(0, reading.Preamble);

    private static string Choosing(DocumentGist gist) =>
        "You find the parts of a long document that answer a question, from what each part says in brief, so that only those " +
        $"are read. The whole document is:\n{gist.Said}\n\n" +
        "Judge each part by what it says, and choose every part that bears on the question — a part may answer only some of it. " +
        "Answer with the numbers asked for, one a line, and nothing else.";

    private static string Answering(DocumentGist gist) =>
        "You answer a question about a long document from the parts of it that bear on the question. The whole document " +
        $"is:\n{gist.Said}\n\n" +
        "Answer from what the parts say, and nothing else; where they do not answer it, say so. After each thing you say, cite " +
        "the parts it comes from by their numbers in square brackets, as [2] or [2, 5]. Answer in the language the question is " +
        "asked in, plainly: no preamble.";

    /// <summary>A number, or a word, as an answer naming parts is read.</summary>
    [GeneratedRegex(@"\d+|\p{L}+")]
    private static partial Regex Token();

    [GeneratedRegex(@"\[\s*(\d+(?:\s*[,;]\s*\d+)*)\s*\]")]
    private static partial Regex Citation();

    /// <summary>One thing to choose from: a section, or what one says before the sections under it.</summary>
    private sealed record Option(SectionResult Node, bool Own, string Where, string Label, string Brief);

    /// <summary>A part chosen to be read: a section whole, or what it says before the sections under it.</summary>
    private sealed record Chosen(SectionResult Node, bool Own, string Where);
}
