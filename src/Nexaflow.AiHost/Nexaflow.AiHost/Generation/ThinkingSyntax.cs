namespace Nexaflow.AiHost.Generation;

/// <summary>
/// Where a model reasons before it replies — Qwen's <c>&lt;think&gt;…&lt;/think&gt;</c>, Gemma 4's
/// <c>&lt;|channel&gt;thought…&lt;channel|&gt;</c> — told apart by the special tokens its vocabulary holds. The host asks every
/// model to reason; the reasoning is read, and sent only to an app that asks for it: what an action came to is worth saying only
/// when the model has thought about what it did.
/// </summary>
/// <param name="Heading">The name the model writes after opening its reasoning, before the reasoning itself — Gemma 4 names its
/// channel <c>thought</c>; null for none.</param>
internal sealed record ThinkingSyntax(int Open, int Close, string OpenText, string CloseText, string? Heading = null)
{
    /// <param name="special">The one special token a text is in the vocabulary, or null.</param>
    public static ThinkingSyntax? Of(Func<string, int?> special)
    {
        foreach (var (open, close, heading) in new[] { ("<think>", "</think>", (string?)null), ("<|channel>", "<channel|>", "thought") })
            if (special(open) is { } opening && special(close) is { } closing) return new ThinkingSyntax(opening, closing, open, close, heading);
        return null;
    }

    /// <summary>Whether <paramref name="prompt"/> leaves the model reasoning already — a template that opens the span
    /// itself, as Qwen's does for every reply and Gemma's after a tool's result.</summary>
    public bool Opens(string prompt) => prompt.LastIndexOf(OpenText, StringComparison.Ordinal) > prompt.LastIndexOf(CloseText, StringComparison.Ordinal);
}
