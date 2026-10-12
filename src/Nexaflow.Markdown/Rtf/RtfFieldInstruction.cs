using System.Text;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// Reading what a field is from its instruction — <c>HYPERLINK "https://…" \l "section" \o "tip"</c> — the arguments
/// quoted or bare, and the switches each followed by the argument it takes.
/// </summary>
internal static class RtfFieldInstruction
{
    /// <summary>
    /// Where a <c>HYPERLINK</c> field leads: its address, a place in the document after <c>\l</c>, or the two together —
    /// null for any other field, or one that leads nowhere.
    /// </summary>
    public static string? Hyperlink(string instruction)
    {
        var words = Arguments(instruction);
        if (words.Count == 0 || !string.Equals(words[0].Text, "HYPERLINK", StringComparison.OrdinalIgnoreCase)) return null;

        string? address = null;
        string? place = null;

        for (var at = 1; at < words.Count; at++)
        {
            var (text, quoted) = words[at];

            if (!quoted && text.StartsWith('\\'))
            {
                // \l names a place in the document, \o a tip and \t a frame; \m and \n take nothing after them.
                var switched = text.ToLowerInvariant();
                var takes = switched is "\\l" or "\\o" or "\\t" && at + 1 < words.Count;

                if (takes && switched == "\\l") place = words[at + 1].Text;
                if (takes) at++;

                continue;
            }

            address ??= text;
        }

        if (place is { Length: > 0 }) return (address ?? string.Empty) + "#" + place;

        return address is { Length: > 0 } ? address : null;
    }

    /// <summary>
    /// The arguments of an instruction, each as written: a quoted one without its quotes and with a doubled backslash read
    /// as the one it stands for, a bare one up to the space after it.
    /// </summary>
    private static List<(string Text, bool Quoted)> Arguments(string instruction)
    {
        var words = new List<(string, bool)>();
        var text = new StringBuilder();

        for (var at = 0; at < instruction.Length;)
        {
            var c = instruction[at];

            if (char.IsWhiteSpace(c))
            {
                at++;

                continue;
            }

            text.Clear();

            if (c == '"')
            {
                at++;

                while (at < instruction.Length && instruction[at] != '"')
                {
                    if (instruction[at] == '\\' && at + 1 < instruction.Length && instruction[at + 1] is '\\' or '"') at++;

                    text.Append(instruction[at++]);
                }

                at++;
                words.Add((text.ToString(), true));

                continue;
            }

            while (at < instruction.Length && !char.IsWhiteSpace(instruction[at])) text.Append(instruction[at++]);

            words.Add((text.ToString(), false));
        }

        return words;
    }
}
