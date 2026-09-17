using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>
/// What an <c>ishikawa</c> (<c>ishikawa-beta</c>) block says beyond the lines every diagram shares: every line is what it says,
/// to its end.
///
/// <para>
/// The rules are Mermaid's. The first line is the event — the problem the diagram is about — and every line after it a cause,
/// under the nearest line before it that is indented less (<see cref="IshikawaChart"/>). The event may follow the keyword on
/// the header line. Nothing on a line is anything but text: a <c>%%</c> starting a line is a comment, and one later on it is
/// part of what it says.
/// </para>
/// </summary>
public sealed class IshikawaGrammar : IMermaidGrammar
{
    /// <inheritdoc/>
    /// <remarks>The event, written on the header line: <c>ishikawa-beta Blurry Photo</c>.</remarks>
    public ContentNode? Header(string arguments) => Cause(arguments);

    /// <inheritdoc/>
    public ContentNode? Statement(string text) => Cause(text);

    /// <summary>A line, as what it says.</summary>
    private static ContentNode Cause(string text)
    {
        var line = MermaidLine.Of(text, comments: false);
        line.Words(IshikawaRoles.Says);
        return line.Read(IshikawaKinds.Cause);
    }
}
