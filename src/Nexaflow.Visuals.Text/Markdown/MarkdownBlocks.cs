namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Pure (WPF-free) block model for the inline markdown editor. A block holds only its
/// <em>content</em>; blocks are separated by blank lines in the markdown. A single
/// newline (or a hard break "  \n") stays inside one block; a blank line separates
/// blocks. Keeping separators OUT of block content means editing a block never shows or
/// doubles the blank-line separator. Unit-testable in isolation.
/// </summary>
public static class MarkdownBlocks
{
    /// <summary>
    /// Splits <paramref name="source"/> into block contents on blank lines. Multi-line
    /// constructs without blank lines (lists, tables, fenced code, hard breaks) stay one
    /// block. Blank-line separators are dropped (they are re-added by <see cref="Join"/>).
    /// Always returns at least one block; empty/whitespace input → <c>[""]</c>.
    /// </summary>
    public static List<string> Split(string? source)
    {
        var s = (source ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        if (s.Length == 0) return [""];

        var blocks    = new List<string>();
        var current   = new List<string>();
        bool prevBlank = false;

        foreach (var line in s.Split('\n'))
        {
            if (line.Trim().Length == 0) { prevBlank = true; continue; }   // blank line = separator
            if (prevBlank && current.Count > 0)
            {
                blocks.Add(string.Join("\n", current));
                current.Clear();
            }
            current.Add(line);
            prevBlank = false;
        }
        if (current.Count > 0) blocks.Add(string.Join("\n", current));
        if (blocks.Count == 0) blocks.Add(string.Empty);
        return blocks;
    }

    /// <summary>Joins block contents back into a markdown string with one blank line between them.</summary>
    public static string Join(IEnumerable<string> blocks) => string.Join("\n\n", blocks);

    /// <summary>Drops empty/whitespace-only blocks; never returns an empty list.</summary>
    public static List<string> Compact(IEnumerable<string> blocks)
    {
        var kept = blocks.Where(b => b.Trim().Length > 0).ToList();
        if (kept.Count == 0) kept.Add(string.Empty);
        return kept;
    }
}
