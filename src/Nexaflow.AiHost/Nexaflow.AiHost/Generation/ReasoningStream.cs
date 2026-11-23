using System.Text;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// A model's reasoning as the app is sent it: the text between where the model opens its reasoning and where it closes it, without
/// the name some models give the channel they reason in (Gemma 4's <c>thought</c>) or the whitespace before the first word. The
/// start is held until it can be told from that name; from then on each piece is sent as it is written.
/// </summary>
/// <param name="heading">The name the model writes before its reasoning (<see cref="ThinkingSyntax.Heading"/>); null for none.</param>
internal sealed class ReasoningStream(string? heading)
{
    private readonly StringBuilder _held = new();
    private bool _started;

    /// <summary>What of <paramref name="piece"/>, the reasoning's next text, is sent: nothing while its start may still be the
    /// heading, then all that is held at once, then each piece as it comes.</summary>
    public string Take(string piece)
    {
        if (_started) return piece;
        _held.Append(piece);
        string held = _held.ToString().TrimStart();
        if (heading is not null)
        {
            if (held.Length <= heading.Length && heading.StartsWith(held, StringComparison.Ordinal)) return "";
            if (held.StartsWith(heading, StringComparison.Ordinal) && char.IsWhiteSpace(held[heading.Length]))
                held = held[heading.Length..].TrimStart();
        }
        if (held.Length == 0) return "";
        _started = true;
        return held;
    }
}
