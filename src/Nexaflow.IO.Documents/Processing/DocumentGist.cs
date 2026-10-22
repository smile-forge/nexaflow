namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// What a document is, said before any part of it is worked on: what kind of writing, what about, for whom, to what end,
/// in what register, tone and voice, written how, and in what language. Every part summarised or translated afterwards is
/// handed it, so a document worked through a piece at a time still reads as the one it was.
/// </summary>
public sealed record DocumentGist(
    string Genre, string Topic, string Audience, string Intent, string Register, string Tone, string Voice, string Style, string Language)
{
    /// <summary>The names it is said under, one a line, in order.</summary>
    private static readonly string[] Names = ["GENRE", "TOPIC", "AUDIENCE", "INTENT", "REGISTER", "TONE", "VOICE", "STYLE", "LANGUAGE"];

    /// <summary>What a model is asked to answer with: each line, and what goes on it.</summary>
    internal const string Lines =
        "GENRE: fiction, non-fiction, technical, reference, or another single word\n" +
        "TOPIC: what it is about\n" +
        "AUDIENCE: who it is written for\n" +
        "INTENT: what it sets out to do\n" +
        "REGISTER: formal, neutral, informal or another\n" +
        "TONE: the feeling it is written with\n" +
        "VOICE: who tells it, and in which tense\n" +
        "STYLE: how its sentences are written\n" +
        "LANGUAGE: the language it is written in, named in English";

    /// <summary>It, a line each, as it is handed to whatever works on a part.</summary>
    public string Said => string.Join('\n', Names.Zip(Values, (name, value) => $"{name}: {value}"));

    /// <summary>What notes worked out with it are kept under: a different gist is a different reading of every part.</summary>
    public string Key => DocumentKeys.Of(Said);

    private string[] Values => [Genre, Topic, Audience, Intent, Register, Tone, Voice, Style, Language];

    /// <summary>
    /// What a model said, read from its answer: a line for each name, however it marked them up; null where it said too little
    /// to work from — no topic, no language, or fewer than five of them.
    /// </summary>
    public static DocumentGist? Read(string answer)
    {
        var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var written in answer.Split('\n'))
        {
            var line = written.Replace("**", string.Empty).Trim().TrimStart('-', '*', '#', ' ');
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var name = line[..colon].Trim().ToUpperInvariant();
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0 || Array.IndexOf(Names, name) < 0 || said.ContainsKey(name)) continue;

            said[name] = value;
        }

        if (!said.ContainsKey("TOPIC") || !said.ContainsKey("LANGUAGE") || said.Count < 5) return null;

        string Of(string name) => said.GetValueOrDefault(name) ?? "not said";

        return new(Of("GENRE"), Of("TOPIC"), Of("AUDIENCE"), Of("INTENT"), Of("REGISTER"), Of("TONE"), Of("VOICE"), Of("STYLE"), Of("LANGUAGE"));
    }
}

/// <summary>
/// Says what a document is (<see cref="DocumentGist"/>), first, before any part of it is summarised or translated.
///
/// <para>
/// A model that reads a few pages at once cannot read a book to say it, so it is said from what fits: the outline of the
/// headings, the front matter, and up to <see cref="MostPassages"/> passages spread through the whole document, each sized to
/// the model (<see cref="DocumentBudget"/>), with what was said so far carried into each next reading. So it costs the same few
/// questions however long the document is — and it is kept, keyed by what was read, so it is said again only when that changes.
/// </para>
/// </summary>
public sealed class DocumentGistPass(IDocumentModel model, IDocumentNotes notes, IProgress<DocumentProgress>? progress = null)
{
    /// <summary>The shelf its notes are kept on.</summary>
    public const string Shelf = "gist";

    /// <summary>How many passages it reads at most.</summary>
    public const int MostPassages = 5;

    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    private const string Version = "gist-1";

    private const string Instructions =
        "You say what a whole document is, from its outline and from passages spread through it, so that whoever later " +
        "summarises or translates it a piece at a time keeps to the way it was written. Judge the document as a whole: each " +
        "passage is part of something larger. Answer with the lines asked for, each a short phrase, and nothing else.";

    /// <summary>What <paramref name="reading"/> is.</summary>
    public async Task<DocumentGist> SayAsync(DocumentReading reading, CancellationToken ct)
    {
        var budget = DocumentBudget.Of(model);
        var outline = reading.Outline(budget.Share(0.15));
        var front = reading.FrontMatter is { } said ? DocumentCuts.Cut(said, budget.Share(0.05))[0] : null;
        var stretches = reading.Stretches(budget.Share(0.4));
        var read = DocumentCuts.Spread(stretches.Count, MostPassages);

        DocumentGist? gist = null;

        for (var at = 0; at < read.Count; at++)
        {
            progress?.Report(new(DocumentStep.Gist, at, read.Count));
            gist = await ReadAsync(gist, outline, front, stretches[read[at]], at, read.Count, ct);
        }

        // A document with nothing in it to read is said from what little there is.
        gist ??= await ReadAsync(null, outline, front, string.Empty, 0, 1, ct);

        progress?.Report(new(DocumentStep.Gist, read.Count, read.Count));
        return gist;
    }

    /// <summary>What the document is, from one more passage and what was said so far.</summary>
    private async Task<DocumentGist> ReadAsync(DocumentGist? sofar, string outline, string? front, string passage, int at, int of, CancellationToken ct)
    {
        var key = DocumentKeys.Of(Version, model.Key, sofar?.Said ?? string.Empty, outline, front ?? string.Empty, passage);
        if (await notes.ReadAsync(Shelf, key, ct) is { } kept && DocumentGist.Read(kept) is { } known) return known;

        var asked = new System.Text.StringBuilder();
        if (sofar is not null) asked.Append("What you have said of it so far, from earlier passages:\n").Append(sofar.Said).Append("\n\n");
        if (outline.Length > 0) asked.Append("Its headings:\n").Append(outline).Append("\n\n");
        if (front is not null) asked.Append("What it says about itself:\n").Append(front).Append("\n\n");
        if (passage.Length > 0) asked.Append($"Passage {at + 1} of the {of} read from all through it:\n\n").Append(passage).Append("\n\n");
        asked.Append("Say what the whole document is, in these lines:\n").Append(DocumentGist.Lines);

        var reply = await model.AnswerAsync(Instructions, asked.ToString(), ct);
        var gist = DocumentGist.Read(reply.Text);

        if (gist is null && reply.Ended != DocumentReplyEnd.Failed)
        {
            reply = await model.AnswerAsync(Instructions, asked + "\n\nAnswer with exactly those lines, each filled in, and nothing else.", ct);
            gist = DocumentGist.Read(reply.Text);
        }

        if (gist is null)
            throw reply.Ended == DocumentReplyEnd.Failed
                ? DocumentWorkException.NotAsked("what the document is", reply)
                : new DocumentWorkException("The model did not say what the document is in the lines it was asked for.");

        await notes.WriteAsync(Shelf, key, gist.Said, ct);
        return gist;
    }
}
