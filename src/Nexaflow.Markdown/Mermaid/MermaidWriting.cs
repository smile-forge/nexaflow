using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>What is written in place of a stretch of a block, and where the caret goes after it.</summary>
/// <param name="Start">Where the stretch written over starts.</param>
/// <param name="End">Where it ends: the same as <paramref name="Start"/> where nothing is written over.</param>
/// <param name="Text">What is written there.</param>
/// <param name="Caret">Where the caret goes, in the block as it reads afterwards.</param>
public readonly record struct MermaidWriting(int Start, int End, string Text, int Caret)
{
    /// <summary>
    /// Text written where only some characters may go — a bare id, a class, a number — with the rest dropped, since a name
    /// that cannot be quoted has nowhere to put them. Null where all of it may go in as it is.
    /// </summary>
    public static MermaidWriting? Only(int caret, string text, Func<char, bool> holds)
    {
        if (text.All(holds)) return null;

        var kept = new string([.. text.Where(holds)]);
        return new MermaidWriting(caret, caret, kept, caret + kept.Length);
    }

    /// <summary>
    /// Text written at the caret inside quotes, which hold anything but a quote — or null where there is no quote in it
    /// and it goes in as it is.
    /// </summary>
    public static MermaidWriting? InQuotes(int caret, string text)
    {
        if (!text.Contains('"')) return null;

        var quoted = MermaidText.Quoted(text);
        return new MermaidWriting(caret, caret, quoted, caret + quoted.Length);
    }

    /// <summary>
    /// A stretch written over with what it said and what is typed into it, put in quotes — <paramref name="before"/> the
    /// caret and <paramref name="after"/> it — and the caret between the two.
    /// </summary>
    public static MermaidWriting Quoting(int start, int end, string before, string after)
    {
        var head = "\"" + MermaidText.Quoted(before);
        return new MermaidWriting(start, end, head + MermaidText.Quoted(after) + "\"", start + head.Length);
    }

    /// <summary>
    /// What writing <paramref name="text"/> at <paramref name="caret"/> in <paramref name="part"/> is written as, for a part
    /// <see cref="MermaidLine"/> read — the escaping every diagram's edit handler starts from. Null
    /// where the text goes in as it is.
    ///
    /// <para>
    /// In quotes, anything but a quote goes in as it is, and a quote is written as its entity code. A bare name holds what
    /// <paramref name="bare"/> says a name written without quotes can, and is put in quotes to hold anything else. A label in
    /// brackets holds anything but a quote, a bracket that closes it or a comment, and is put in quotes to hold those.
    /// </para>
    /// </summary>
    /// <param name="bare">Whether a name can go without quotes, given the name it is in and what it would say — or null where any can.</param>
    public static MermaidWriting? Escape(ContentPart part, int caret, string text, Func<ContentPart, string, bool>? bare = null)
    {
        if (part.Parent is not { } holder || part.Kind is not (Kinds.Words or Kinds.Hole)) return null;

        if (holder.Children.Any(child => child.Role == Roles.Open && child.Text == "\"")) return InQuotes(caret, text);

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        switch (holder.Kind)
        {
            case MermaidKinds.Name when bare is not null && !bare(holder, before + after):
                return Quoting(part.Start, part.Start + said.Length, before, after);

            // Inside the brackets, space and all: a label in quotes is written hard against them.
            case MermaidKinds.Label:
                var open = holder.Children.First(child => child.Role == Roles.Open);
                var close = holder.Children.LastOrDefault(child => child.Role == Roles.Close);
                var closing = close?.Text ?? string.Empty;

                return (before + after).Any(character => character is '"' or '%' || closing.Contains(character))
                    ? Quoting(open.End, close?.Start ?? holder.End, before, after)
                    : null;
        }

        return null;
    }
}
