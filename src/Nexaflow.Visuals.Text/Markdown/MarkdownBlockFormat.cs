using System;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The block-level markdown rewrites behind the editing mini-toolbar (headings, inline markers, quote,
/// code fence), as pure string transforms: each takes the active block's source and returns the rewritten
/// block plus where the caret should land.
/// <para>
/// <see cref="InlineMarkdownEditor"/> owns the caret/selection and the document rebuild; the *text* rule
/// for each button lives here, so it is stated once and can be asserted without a rendered editor.
/// </para>
/// </summary>
public static class MarkdownBlockFormat
{
    /// <summary>
    /// Inserts a matched pair of <paramref name="marker"/>s at <paramref name="caret"/> (the no-selection
    /// form of bold/italic/strike/code) and parks the caret between them, ready to type.
    /// </summary>
    public static (string Text, int Caret) InsertMarkers(string block, int caret, string marker)
    {
        int at = Math.Clamp(caret, 0, block.Length);
        return (block[..at] + marker + marker + block[at..], at + marker.Length);
    }

    /// <summary>Wraps <paramref name="selected"/> in <paramref name="marker"/> — the with-selection form.</summary>
    public static string WrapSelection(string selected, string marker) => marker + selected + marker;

    /// <summary>
    /// Sets the block's first line to an ATX heading of <paramref name="level"/>, or strips the heading when
    /// it is already exactly that level (the buttons toggle). Lines after the first are untouched.
    /// </summary>
    public static (string Text, int Caret) SetHeading(string block, int level)
    {
        int nl = block.IndexOf('\n');
        string first = nl < 0 ? block : block[..nl];
        string rest  = nl < 0 ? string.Empty : block[nl..];
        string body  = first.TrimStart('#').TrimStart(' ');
        string prefix = new string('#', level) + " ";
        string newFirst = first.StartsWith(prefix, StringComparison.Ordinal) ? body : prefix + body;
        return (newFirst + rest, newFirst.Length);
    }

    /// <summary>
    /// Adds <paramref name="prefix"/> to every line, or strips it when every line already carries it
    /// (the quote button toggles). All-or-nothing, so a partially-quoted block quotes fully first.
    /// </summary>
    public static (string Text, int Caret) ToggleLinePrefix(string block, string prefix)
    {
        var lines = block.Split('\n');
        bool allPrefixed = lines.All(l => l.StartsWith(prefix, StringComparison.Ordinal));
        for (int i = 0; i < lines.Length; i++)
            lines[i] = allPrefixed ? lines[i][prefix.Length..] : prefix + lines[i];
        var joined = string.Join("\n", lines);
        return (joined, joined.Length);
    }

    /// <summary>Wraps the block in a <c>```</c> fence, or unwraps it when it is already fenced (toggle).</summary>
    public static (string Text, int Caret) ToggleCodeFence(string block)
    {
        var lines = block.Split('\n');
        bool fenced = lines.Length >= 2
            && lines[0].StartsWith("```", StringComparison.Ordinal)
            && lines[^1].StartsWith("```", StringComparison.Ordinal);
        string result = fenced ? string.Join("\n", lines[1..^1]) : "```\n" + block + "\n```";
        return (result, result.Length);
    }
}
