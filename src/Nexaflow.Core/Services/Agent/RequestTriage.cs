using System.Text;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// How a request is weighed before it is answered. Where a request goes when its row holds both a model on this machine
/// and one elsewhere: the machine's own takes the
/// everyday work it does well, at no cost, and the larger model the rest. A request too long for the machine's model to
/// read goes to the larger one unasked. The rest are judged by the Disambiguation model from the request alone, and the
/// reply it follows — never the page's contents, so judging sends nothing anywhere a request would not. And whether a
/// spoken request needs thought before its answer (<see cref="NeedsThoughtAsync"/>). Every judgement is asked without the
/// judge's own reasoning: one word, at once.
/// </summary>
internal static class RequestTriage
{
    /// <summary>How much of a request, and of the reply before it, the judge reads: enough to know what is asked.</summary>
    private const int Shown = 1500;

    /// <summary>Characters to a token, low, so a request is never thought shorter than it is.</summary>
    internal const int CharsPerToken = 3;

    /// <summary>How much of the machine model's context a request and its conversation may fill and leave room for the
    /// page, the tools and the work.</summary>
    private const double Fill = 0.5;

    private const string Judging =
        "You route a user's request to one of two assistants. Reply with one word: small or large.";

    private const string Assistants =
        "small: a fast assistant on the user's computer. It does everyday work well: answering short questions; a few steps " +
        "with files, folders, pages and apps (open, find, create, rename, move, change a setting); short writing; summarising, " +
        "explaining or translating something short.\n" +
        "large: a far more capable assistant in the cloud, which costs money. It is needed for reasoning over several steps, " +
        "planning, writing or changing code, long or careful writing, analysing long or technical material, maths, and anything " +
        "ambiguous or where a mistake would be costly.\n\n";

    private const string Weighing =
        "You decide whether what a user says to a voice assistant needs careful thought before it is answered. The assistant " +
        "does what it is asked through its tools, so thought is only for what it must work out itself. Reply with one word: quick or think.";

    private const string Answers =
        "quick: answered at once — a greeting or small talk, a plain fact, a yes or no, a simple question about what is on " +
        "screen, or something to do or make that the assistant's tools do (open, find, rename, play, set, create, draw or make " +
        "a picture or video), however much it describes what is wanted.\n" +
        "think: the reply itself needs reasoning first — an opinion, assessment or recommendation, a plan over several steps, " +
        "comparing, explaining something involved, writing code, maths, or anything where a careless answer would mislead.\n\n";

    /// <summary>
    /// Whether <paramref name="local"/> should take <paramref name="request"/>: it fits, and <paramref name="judge"/> finds
    /// it light work. Without a judge, nothing is decided here and the row's order stands — null. An answer the judge
    /// gives that names neither model sends the request to the larger one: a doubt costs a little money, not the answer.
    /// </summary>
    public static async Task<bool?> SuitsAsync(ILlmProvider? judge, ILlmProvider local, string request, IReadOnlyList<LlmMessage> history,
                                                CancellationToken ct)
    {
        if (!await FitsAsync(local, request, history, ct)) return false;
        if (judge is null) return null;

        var asked = new StringBuilder(Assistants);
        if (history.LastOrDefault(m => m.Role == LlmRole.Assistant) is { Text.Length: > 0 } before)
            asked.Append("The assistant had just replied: \"").Append(Clip(before.Text)).Append("\"\n");
        asked.Append("The request: \"").Append(Clip(request)).Append("\"\n\nWhich assistant should take it?");

        string said;
        try { said = (await judge.CompleteAsync([new(LlmRole.System, Judging), new(LlmRole.User, asked.ToString())], LlmUrgency.Interactive, LlmThinking.Off, ct))?.RawText ?? ""; }
        catch (OperationCanceledException) { throw; }
        catch { return false; }   // a judge that cannot answer leaves the request to the larger model

        var small = said.IndexOf("small", StringComparison.OrdinalIgnoreCase);
        var large = said.IndexOf("large", StringComparison.OrdinalIgnoreCase);
        return small >= 0 && (large < 0 || small < large);
    }

    /// <summary>
    /// Whether <paramref name="request"/> needs thought before it is answered — an opinion, a judgement, anything over
    /// several steps — or is answered as quickly as it is asked: a greeting, a plain fact, something a tool does however
    /// richly it is described (a picture to draw is the drawing model's work, not the reply's). Judged by
    /// <paramref name="judge"/> without reasoning of its own; without a judge, or with no clear answer, it is given thought:
    /// a slower answer costs seconds, a thoughtless one its worth.
    /// </summary>
    public static async Task<bool> NeedsThoughtAsync(ILlmProvider? judge, string request, IReadOnlyList<LlmMessage> history, CancellationToken ct)
    {
        if (judge is null) return true;

        var asked = new StringBuilder(Answers);
        if (history.LastOrDefault(m => m.Role == LlmRole.Assistant) is { Text.Length: > 0 } before)
            asked.Append("The assistant had just said: \"").Append(Clip(before.Text)).Append("\"\n");
        asked.Append("The user now says: \"").Append(Clip(request)).Append("\"\n\nQuick or think?");

        string said;
        try { said = (await judge.CompleteAsync([new(LlmRole.System, Weighing), new(LlmRole.User, asked.ToString())], LlmUrgency.Interactive, LlmThinking.Off, ct))?.RawText ?? ""; }
        catch (OperationCanceledException) { throw; }
        catch { return true; }

        var quick = said.IndexOf("quick", StringComparison.OrdinalIgnoreCase);
        var think = said.IndexOf("think", StringComparison.OrdinalIgnoreCase);
        return !(quick >= 0 && (think < 0 || quick < think));
    }

    /// <summary>Whether the request and the conversation before it fit in what <paramref name="local"/> can read, with room
    /// left for the rest. A model that does not say how much it reads is taken to fit.</summary>
    private static async Task<bool> FitsAsync(ILlmProvider local, string request, IReadOnlyList<LlmMessage> history, CancellationToken ct)
    {
        ModelInfo? info;
        try { info = await local.GetModelInfoAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch { info = null; }
        if (info is not { ContextWindowTokens: > 0 } known) return true;

        long chars = request.Length + history.Sum(m => (long)m.Text.Length);
        return chars / CharsPerToken <= known.ContextWindowTokens * Fill;
    }

    private static string Clip(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= Shown ? flat : flat[..Shown] + "…";
    }
}

/// <summary>
/// Where a request to a row of reasoning models goes: <paramref name="Model"/> takes it; <paramref name="HandOverTo"/>, the
/// larger model it goes to should the machine's own get stuck on it; <paramref name="Reader"/>, the machine's model a larger
/// one may hand reading to. A row of one kind of model alone has neither.
/// </summary>
internal sealed record ModelRoute(ILlmProvider Model, ILlmProvider? HandOverTo = null, ILlmProvider? Reader = null);
