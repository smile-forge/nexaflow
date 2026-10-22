using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.IO.Documents.Processing;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A model that answers as it is told to, writing down everything it was asked — the tests' stand-in for one that reads a
/// few pages at once.
/// </summary>
internal sealed class ScriptedModel(Func<string, string, DocumentReply> answer, int context = 8192) : IDocumentModel
{
    public List<(string Instructions, string Asked)> Asked { get; } = [];

    public string Key => "scripted";

    public int ContextTokens => context;

    public int CharsPerToken => 3;

    /// <summary>The most each question let its answer run to, in the order asked.</summary>
    public List<int> Longest { get; } = [];

    public Task<DocumentReply> AnswerAsync(string instructions, string asked, int longest, CancellationToken ct)
    {
        Asked.Add((instructions, asked));
        Longest.Add(longest);
        return Task.FromResult(answer(instructions, asked));
    }

    /// <summary>The questions whose instructions start with <paramref name="lead"/>.</summary>
    public List<string> Of(string lead) => [.. Asked.Where(question => question.Instructions.StartsWith(lead, StringComparison.Ordinal)).Select(question => question.Asked)];

    /// <summary>A model that says every document is the same one, summarises a part as "S(where)", lists no names, and translates by shouting.</summary>
    public static ScriptedModel Plain(int context = 8192) => new((instructions, asked) => new(Answer(instructions, asked), DocumentReplyEnd.Finished), context);

    /// <summary>What the plain model answers.</summary>
    public static string Answer(string instructions, string asked) =>
        instructions.StartsWith(Gist, StringComparison.Ordinal) ? Said
      : instructions.StartsWith(Summary, StringComparison.Ordinal) ? $"S({Where(asked)})"
      : instructions.StartsWith(Translation, StringComparison.Ordinal) ? Translated(asked, Shout)
      : string.Empty;

    public const string Gist = "You say what a whole document is";
    public const string Summary = "You summarise";
    public const string Translation = "You translate a document";
    public const string Glossary = "You list the words";
    public const string GlossaryTranslation = "You translate the glossary";
    public const string Choosing = "You find the parts";
    public const string Answering = "You answer a question";

    public const string Said =
        "GENRE: non-fiction\nTOPIC: pets\nAUDIENCE: owners\nINTENT: to inform\nREGISTER: neutral\nTONE: warm\nVOICE: second person, present\nSTYLE: short sentences\nLANGUAGE: English";

    /// <summary>Where a summarised part stands, as the question says it — "whole" for the document itself.</summary>
    public static string Where(string asked) =>
        Regex.Match(asked, "^Where it stands in the document: (.*)$", RegexOptions.Multiline) is { Success: true } where ? where.Groups[1].Value.Trim() : "whole";

    /// <summary>The pieces a translation question hands over, in order.</summary>
    public static List<string> Pieces(string asked)
    {
        var from = asked.IndexOf("Translate each of these into", StringComparison.Ordinal);
        var to = asked.IndexOf("Answer with the same @@ lines", StringComparison.Ordinal);
        var body = asked[asked.IndexOf('\n', from)..to];

        return [.. Regex.Split(body, @"^@@ \d+\r?$", RegexOptions.Multiline).Skip(1).Select(piece => piece.Trim())];
    }

    /// <summary>A translation question answered by <paramref name="each"/> on every piece.</summary>
    public static string Translated(string asked, Func<string, string> each)
    {
        var answer = new StringBuilder();
        var pieces = Pieces(asked);
        for (var at = 0; at < pieces.Count; at++) answer.Append("@@ ").Append(at + 1).Append('\n').Append(each(pieces[at])).Append("\n\n");

        return answer.ToString();
    }

    /// <summary>Every letter outside the held marks in capitals.</summary>
    public static string Shout(string piece) => Regex.Replace(piece, @"⟦\d+⟧|[^⟦]+", match => match.Value.StartsWith('⟦') ? match.Value : match.Value.ToUpperInvariant());
}

/// <summary>Notes kept in memory, for a test to see what was kept and to keep it from one run to the next.</summary>
internal sealed class MemoryNotes : IDocumentNotes
{
    public Dictionary<(string Shelf, string Key), string> Kept { get; } = [];

    public Task<string?> ReadAsync(string shelf, string key, CancellationToken ct) =>
        Task.FromResult(Kept.TryGetValue((shelf, key), out var note) ? note : null);

    public Task WriteAsync(string shelf, string key, string text, CancellationToken ct)
    {
        Kept[(shelf, key)] = text;
        return Task.CompletedTask;
    }

    public bool Holds(string shelf, string key) => Kept.ContainsKey((shelf, key));
}
