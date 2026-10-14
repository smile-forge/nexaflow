using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// Whether a request continues a conversation the model holds, and what it must read to: the decisions of the context
/// log, apart from the model that acts on them.
/// </summary>
internal static class ConversationMatch
{
    /// <summary>True when <paramref name="read"/> — what a conversation has read, its last reply included — begins
    /// <paramref name="turns"/>, with more after it.</summary>
    public static bool Continues(IReadOnlyList<ChatTurn> read, IReadOnlyList<ChatTurn> turns)
    {
        if (read.Count == 0 || turns.Count <= read.Count) return false;
        for (int i = 0; i < read.Count; i++)
            if (!Same(read[i], turns[i])) return false;
        return true;
    }

    /// <summary>
    /// What extends a context to <paramref name="turns"/>. The context holds <paramref name="prompt"/> — the prompt it
    /// was last given — then its reply as generated, up to the token the reply stopped on, which it has not read. The
    /// template's rendering of what it read must begin with that prompt and end the reply with that token: the extension
    /// is the history from that token on, then the new turns and the generation prompt. The reply itself stays as the
    /// model wrote it, however the template would now write it (Gemma's orders a call's arguments, Qwen's trims). Null
    /// when the turns do not render as an extension of what was read.
    /// </summary>
    /// <param name="render">The template: turns, and whether to end with the generation prompt.</param>
    public static string? Delta(IReadOnlyList<ChatTurn> read, string prompt, string stoppedAt, IReadOnlyList<ChatTurn> turns,
                                Func<IReadOnlyList<ChatTurn>, bool, string> render)
    {
        if (stoppedAt.Length == 0) return null;
        string history = render(read, false);
        string next    = render(turns, true);
        if (!next.StartsWith(history, StringComparison.Ordinal) || !history.StartsWith(prompt, StringComparison.Ordinal)) return null;

        int at = history.LastIndexOf(stoppedAt, StringComparison.Ordinal);
        return at < prompt.Length ? null : history[at..] + next[history.Length..];
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
