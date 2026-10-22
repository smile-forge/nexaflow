namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// A summary, asked of every section of a document from the deepest up (<see cref="SectionPass"/>): each section said in brief
/// from what it says itself and the summaries of the sections under it, the whole document — its abstract — from what comes
/// before its first heading and the summaries of its outermost sections. Written out, it is a document of its own
/// (<see cref="SummaryMarkdown"/>).
/// </summary>
public sealed class SummaryQuestion : ISectionQuestion
{
    /// <summary>The shelf its notes and its record are kept on.</summary>
    public const string Shelf = "summary";

    /// <summary>Changed whenever what it asks changes, so nothing asked another way is taken for its answer.</summary>
    private const string Version = "summary-1";

    string ISectionQuestion.Shelf => Shelf;

    string ISectionQuestion.Version => Version;

    public DocumentStep Step => DocumentStep.Summary;

    public string Before => "What the part before it says, in brief:";

    public string Instructions(DocumentGist gist) =>
        "You summarise a longer document one part at a time, so that the summaries of all its parts read as one. The whole " +
        $"document is:\n{gist.Said}\n\n" +
        "Write in its language, register, tone and voice, for its audience. Say what the part says, briefly, as the document " +
        "itself would — do not describe the document or the part. Plain prose only: no heading, no list, no preamble, nothing " +
        "but the summary.";

    public string Under(bool whole) => whole ? "What its parts say, in brief:" : "What the parts under it say, in brief:";

    public string Asked(SectionAsk ask) =>
        $"{(ask.Whole ? "Summarise the whole document" : "Summarise this part")} in at most {Words(ask.Characters, ask.Whole)} words, in {ask.Gist.Language}.";

    public string Noted(SectionAsk ask) =>
        $"Write notes on all of it so far, in at most {Words(ask.Characters, whole: false) * 2} words, in {ask.Gist.Language}: " +
        "everything a summary of it would need, in the order it comes.";

    public string? Read(string answer) => answer.Trim();

    /// <summary>About how many words a summary of <paramref name="characters"/> characters runs to.</summary>
    private static int Words(int characters, bool whole) => Math.Clamp(characters / 48, 40, whole ? 400 : 250);
}
