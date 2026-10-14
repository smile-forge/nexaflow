namespace Nexaflow.AiHost.Generation;

/// <summary>
/// Where a model reasons before it replies — Qwen's <c>&lt;think&gt;…&lt;/think&gt;</c>, Gemma 4's
/// <c>&lt;|channel&gt;…&lt;channel|&gt;</c> — told apart by the special tokens its vocabulary holds. The host asks for
/// replies without reasoning; a span the model writes all the same is read but never sent as text.
/// </summary>
internal sealed record ThinkingSyntax(int Open, int Close)
{
    /// <param name="special">The one special token a text is in the vocabulary, or null.</param>
    public static ThinkingSyntax? Of(Func<string, int?> special)
    {
        if (special("<think>") is { } open && special("</think>") is { } close) return new ThinkingSyntax(open, close);
        if (special("<|channel>") is { } channel && special("<channel|>") is { } closed) return new ThinkingSyntax(channel, closed);
        return null;
    }
}
