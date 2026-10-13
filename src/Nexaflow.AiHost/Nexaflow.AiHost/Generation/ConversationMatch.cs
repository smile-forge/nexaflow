using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// Whether a request continues a conversation the model holds, and what it must read to: the decisions of the context
/// log, apart from the model that acts on them.
/// </summary>
internal static class ConversationMatch
{
    /// <summary>
    /// True when <paramref name="read"/> — what a conversation has read, its last reply included — begins
    /// <paramref name="turns"/>, and the turns after it are text: media cannot join a continuing generator
    /// (onnxruntime-genai drops it, or crashes).
    /// </summary>
    public static bool Continues(IReadOnlyList<ChatTurn> read, IReadOnlyList<ChatTurn> turns)
    {
        if (read.Count == 0 || turns.Count <= read.Count) return false;
        for (int i = 0; i < read.Count; i++)
            if (!Same(read[i], turns[i])) return false;
        for (int i = read.Count; i < turns.Count; i++)
            if (turns[i].Media is { Count: > 0 }) return false;
        return true;
    }

    /// <summary>
    /// What extends a context that has read <paramref name="read"/> to <paramref name="turns"/>: the template's closing
    /// for the last reply, then the new turns and the generation prompt. The closing is read whole — onnxruntime-genai
    /// does not keep the end token a reply stopped on, so the model has not read it. A reply that ended in tool calls
    /// stopped where the model waits for what came of them, a token it did read: the extension is the rest. Earlier text
    /// the template would now render differently (Gemma's adds and strips thought blocks, and orders a call's arguments)
    /// stays as it was read. Null when the turns do not render as an extension of what was read.
    /// </summary>
    /// <param name="render">The template: turns, and whether to end with the generation prompt.</param>
    public static string? Delta(IReadOnlyList<ChatTurn> read, IReadOnlyList<ChatTurn> turns, Func<IReadOnlyList<ChatTurn>, bool, string> render)
    {
        string history = render(read, false);
        string prompt  = render(turns, true);
        if (!prompt.StartsWith(history, StringComparison.Ordinal)) return null;
        if (read[^1].ToolCalls is { Count: > 0 }) return prompt[history.Length..];

        string reply = read[^1].Text.Trim();
        int at = reply.Length == 0 ? -1 : history.LastIndexOf(reply, StringComparison.Ordinal);
        if (at < 0) return null;

        return history[(at + reply.Length)..] + prompt[history.Length..];
    }

    /// <summary>When two turns are the same turn — the app's copy of a reply against the one generated, whitespace
    /// at its ends aside — with the same calls, or answering the same call.</summary>
    public static bool Same(ChatTurn a, ChatTurn b)
        => a.Role == b.Role
        && (a.Role == TurnRole.Assistant ? a.Text.Trim() == b.Text.Trim() : a.Text == b.Text)
        && SameMedia(a.Media ?? [], b.Media ?? [])
        && (a.ToolCalls ?? []).SequenceEqual(b.ToolCalls ?? [])
        && a.Answers == b.Answers;

    private static bool SameMedia(IReadOnlyList<ChatMedia> a, IReadOnlyList<ChatMedia> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind
                                                  && p.First.Path == p.Second.Path
                                                  && (p.First.Bytes ?? []).AsSpan().SequenceEqual(p.Second.Bytes ?? []));
}
