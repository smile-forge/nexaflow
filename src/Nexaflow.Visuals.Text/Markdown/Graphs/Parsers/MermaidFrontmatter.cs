namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Strips a leading Mermaid YAML front-matter block (a <c>---</c> … <c>---</c> fence carrying
/// <c>title:</c> / <c>config:</c>) from a diagram source, so the dispatcher and the per-diagram
/// parsers see only the diagram body.  A top-level <c>title:</c> is lifted out for the caller to
/// apply; <c>config:</c> (theming, per-diagram options) is recognised but not applied.
/// </summary>
public static class MermaidFrontmatter
{
    /// <summary>Returns the body with any front-matter removed, plus the front-matter title if present.</summary>
    public static (string body, string? title) Strip(string source)
    {
        var lines = source.Split('\n');

        int start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0) start++;
        if (start >= lines.Length || lines[start].Trim() != "---")
            return (source, null);

        int close = -1;
        for (int j = start + 1; j < lines.Length; j++)
            if (lines[j].Trim() == "---") { close = j; break; }
        if (close < 0)
            return (source, null);   // unterminated → treat as ordinary content, not front-matter

        string? title = null;
        for (int j = start + 1; j < close && title is null; j++)
        {
            var raw = lines[j];
            // Top-level key only (no indentation) so a nested config "title:" isn't picked up.
            if (raw.Length > 0 && !char.IsWhiteSpace(raw[0]) &&
                raw.TrimEnd().StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                title = raw.Trim()["title:".Length..].Trim().Trim('"', '\'');
        }

        string body = string.Join('\n', lines[(close + 1)..]);
        return (body, string.IsNullOrWhiteSpace(title) ? null : title);
    }
}
