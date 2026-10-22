using System.Text;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// How a translation gives a document's names and terms — the same way every time, whichever passage they turn up in.
/// </summary>
/// <param name="Entries">Each name or term as the document writes it, and as the translation gives it.</param>
public sealed record DocumentGlossary(IReadOnlyList<(string Source, string Target)> Entries)
{
    /// <summary>A glossary with nothing in it, for a document with no name or term used more than once.</summary>
    public static DocumentGlossary None { get; } = new([]);

    /// <summary>The entries <paramref name="text"/> uses.</summary>
    public IReadOnlyList<(string Source, string Target)> In(string text) =>
        [.. Entries.Where(entry => text.Contains(entry.Source, StringComparison.OrdinalIgnoreCase))];

    /// <summary><paramref name="entries"/>, a line each, as a model is handed them.</summary>
    public static string Said(IEnumerable<(string Source, string Target)> entries) =>
        string.Join('\n', entries.Select(entry => $"{entry.Source} => {entry.Target}"));
}

/// <summary>
/// Finds the names and terms a translation of a document has to give the same way every time, and how it gives them: read
/// from the whole document a stretch at a time, each stretch sized to the model, and kept to those used more than once —
/// as many as fit beside what each translated passage is handed. Kept, both what was found and how it was translated, so
/// translating again asks only about what changed.
/// </summary>
public sealed class DocumentGlossaryPass(IDocumentModel model, IDocumentNotes notes, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>The shelf its notes are kept on.</summary>
    public const string Shelf = "glossary";

    /// <summary>The most names and terms asked for from one stretch.</summary>
    private const int MostPerStretch = 30;

    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    private const string Version = "glossary-1";

    /// <summary>The glossary for translating <paramref name="reading"/>, which is <paramref name="gist"/>, into <paramref name="language"/>.</summary>
    public async Task<DocumentGlossary> GatherAsync(DocumentReading reading, DocumentGist gist, string language, CancellationToken ct)
    {
        var budget = DocumentBudget.Of(model);
        var stretches = reading.Stretches(budget.Share(0.5));
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var at = 0; at < stretches.Count; at++)
        {
            progress?.Report(new(DocumentStep.Glossary, at, stretches.Count + 1));
            foreach (var term in await FoundAsync(gist, stretches[at], ct)) found.TryAdd(term, term);
        }

        // Only what is used more than once needs giving the same way twice; the most used first, as many as fit.
        var room = budget.Share(0.12);
        var kept = new List<string>();
        var length = 0;

        foreach (var (term, _) in found.Values.Select(term => (term, Used: Occurrences(reading.Source, term)))
                                         .Where(entry => entry.Used > 1)
                                         .OrderByDescending(entry => entry.Used).ThenBy(entry => entry.term, StringComparer.Ordinal))
        {
            if (length + (term.Length * 2) + 5 > room) break;
            kept.Add(term);
            length += (term.Length * 2) + 5;
        }

        var glossary = kept.Count == 0 ? DocumentGlossary.None : await TranslatedAsync(gist, kept, language, ct);
        progress?.Report(new(DocumentStep.Glossary, stretches.Count + 1, stretches.Count + 1));
        return glossary;
    }

    /// <summary>The names and terms one stretch uses, as written in it — anything the model gave that the stretch does not hold left out.</summary>
    private async Task<IReadOnlyList<string>> FoundAsync(DocumentGist gist, string stretch, CancellationToken ct)
    {
        var key = DocumentKeys.Of(Version, "found", model.Key, gist.Key, stretch);
        var answer = await notes.ReadAsync(Shelf, key, ct);

        if (answer is null)
        {
            var reply = await model.AnswerAsync(
                "You list the words a translation of a document has to give the same way every time: the names of people, places " +
                "and things, and the terms the document uses in a sense of its own. List only what the passage holds, written as it " +
                "writes them, one a line, and nothing else.",
                $"The whole document is:\n{gist.Said}\n\nThe passage:\n\n{stretch}\n\nList at most {MostPerStretch} of them, one a line.",
                ct);

            if (reply.Ended == DocumentReplyEnd.Failed) throw new DocumentWorkException("The model could not be asked for the document's names and terms.");

            answer = reply.Text;
            await notes.WriteAsync(Shelf, key, answer, ct);
        }

        return [.. answer.Split('\n')
                         .Select(Term)
                         .Where(term => term.Length is >= 2 and <= 60 && stretch.Contains(term, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(MostPerStretch)];
    }

    /// <summary>How each of <paramref name="terms"/> is given in <paramref name="language"/>.</summary>
    private async Task<DocumentGlossary> TranslatedAsync(DocumentGist gist, IReadOnlyList<string> terms, string language, CancellationToken ct)
    {
        var key = DocumentKeys.Of(Version, "translated", model.Key, gist.Key, language, string.Join('\n', terms));
        var answer = await notes.ReadAsync(Shelf, key, ct);

        if (answer is null)
        {
            var reply = await model.AnswerAsync(
                $"You translate the glossary of a document being translated into {language}, so its names and terms are given the " +
                $"same way all through it. A name stays as it is unless {language} writes it another way.",
                $"The whole document is:\n{gist.Said}\n\nIts names and terms:\n{string.Join('\n', terms)}\n\n" +
                "Answer with one line for each, as `term => translation`, and nothing else.",
                ct);

            if (reply.Ended == DocumentReplyEnd.Failed) throw new DocumentWorkException("The model could not be asked how to translate the document's names and terms.");

            answer = reply.Text;
            await notes.WriteAsync(Shelf, key, answer, ct);
        }

        var asked = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Source, string Target)>();

        foreach (var line in answer.Split('\n'))
        {
            var arrow = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrow <= 0) continue;

            var source = Term(line[..arrow]);
            var target = line[(arrow + 2)..].Trim().Trim('`', '"', '\'').Trim();
            if (target.Length > 0 && asked.Remove(source)) entries.Add((source, target));
        }

        return new DocumentGlossary(entries);
    }

    /// <summary>A name or term as a model listed it, without what it listed it with.</summary>
    private static string Term(string line)
    {
        var term = line.Trim().TrimStart('-', '*', '•', ' ');
        while (term.Length > 0 && (char.IsDigit(term[0]) || term[0] is '.' or ')')) term = term[1..].TrimStart();

        foreach (var label in (string[])["NAME:", "TERM:"])
            if (term.StartsWith(label, StringComparison.OrdinalIgnoreCase)) term = term[label.Length..];

        return term.Trim().Trim('`', '"', '\'', '*').Trim();
    }

    /// <summary>How many times <paramref name="term"/> is written in <paramref name="text"/>.</summary>
    private static int Occurrences(string text, string term)
    {
        var count = 0;
        for (var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(term, at + term.Length, StringComparison.OrdinalIgnoreCase))
            count++;

        return count;
    }
}
