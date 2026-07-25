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
    /// constructs without blank lines (lists, tables, hard breaks) stay one block, and a
    /// fenced code/diagram block (<c>```</c> / <c>~~~</c>) stays whole even when it contains
    /// blank lines — a blank line inside a fence is part of the block, not a separator.
    /// Blank-line separators are dropped (they are re-added by <see cref="Join"/>).
    /// Always returns at least one block; empty/whitespace input → <c>[""]</c>.
    /// </summary>
    public static List<string> Split(string? source)
    {
        var s = (source ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        if (s.Length == 0) return [""];

        var blocks    = new List<string>();
        var current   = new List<string>();
        bool prevBlank = false;
        bool inFence   = false;

        foreach (var line in s.Split('\n'))
        {
            if (inFence)
            {
                current.Add(line);                       // everything (incl. blank lines) up to the close
                if (IsFenceMarker(line)) inFence = false;
                prevBlank = false;
                continue;
            }

            if (line.Trim().Length == 0) { prevBlank = true; continue; }   // blank line = separator
            if (prevBlank && current.Count > 0)
            {
                blocks.Add(string.Join("\n", current));
                current.Clear();
            }
            current.Add(line);
            prevBlank = false;
            if (IsFenceMarker(line)) inFence = true;      // opening fence — hold blank lines until it closes
        }
        if (current.Count > 0) blocks.Add(string.Join("\n", current));
        if (blocks.Count == 0) blocks.Add(string.Empty);
        return blocks;
    }

    private static bool IsFenceMarker(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("```") || t.StartsWith("~~~");
    }

    /// <summary>True when a block is a fenced code/diagram block (its first non-blank line opens a
    /// <c>```</c> / <c>~~~</c> fence) — such a block treats Enter as a literal newline rather than a
    /// block split.</summary>
    public static bool IsFenced(string block)
    {
        foreach (var line in block.Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            return IsFenceMarker(line);
        }
        return false;
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

    /// <summary>
    /// Index of the block whose ATX heading's <em>ancestor path</em> equals <paramref name="titlePath"/>
    /// (case- and whitespace-insensitive), or -1. Matching on the full hierarchy — not a document-wide text
    /// match — is what keeps two "Overview" headings under different parents distinct, so a snaplink deep
    /// link lands on the one it named.
    /// </summary>
    public static int FindHeadingBlock(IReadOnlyList<string> blocks, IReadOnlyList<string>? titlePath)
    {
        if (titlePath is not { Count: > 0 }) return -1;

        var stack = new List<(int Level, string Text)>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var firstLine = blocks[i].Split('\n', 2)[0].TrimStart();
            if (!firstLine.StartsWith('#')) continue;
            int level = firstLine.Length - firstLine.TrimStart('#').Length;
            var text  = firstLine.TrimStart('#').Trim().TrimEnd('#').Trim();
            if (text.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].Level >= level) stack.RemoveAt(stack.Count - 1);
            var path = stack.Select(s => s.Text).Append(text).ToList();
            stack.Add((level, text));
            if (PathEquals(path, titlePath)) return i;
        }
        return -1;
    }

    private static bool PathEquals(List<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].Trim(), b[i].Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
