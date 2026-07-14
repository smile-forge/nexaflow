using System;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>Which notation language a <c>#%</c> block is written in.</summary>
public enum MusicDialect { Abc, LilyPond }

public static class MusicDialectExtensions
{
    /// <summary>Resolves a dialect from the optional tag on the opening <c>#%</c> line
    /// (e.g. <c>#%abc</c>, <c>#%lilypond</c>, <c>#%ly</c>). Returns null for an unknown tag.</summary>
    public static MusicDialect? FromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        return tag.Trim().ToLowerInvariant() switch
        {
            "abc"                          => MusicDialect.Abc,
            "lilypond" or "ly" or "lily"   => MusicDialect.LilyPond,
            _                              => null,
        };
    }

    /// <summary>
    /// Best-effort dialect detection for an untagged <c>#%</c> block. ABC is line-oriented with
    /// single-letter information fields (<c>X:</c>, <c>T:</c>, <c>K:</c>); LilyPond is dominated by
    /// backslash commands and Scheme <c>#(</c>. Defaults to ABC when neither signal is present.
    /// </summary>
    public static MusicDialect Detect(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return MusicDialect.Abc;

        bool sawAbcField = false;
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Strong LilyPond signals.
            if (line.Contains("#(") || line.StartsWith('\\') || line.Contains(" \\") ||
                line.Contains("\\relative") || line.Contains("\\score") || line.Contains("\\clef"))
                return MusicDialect.LilyPond;

            // ABC information field: a single letter followed by ':' at the start of a line.
            if (line.Length >= 2 && char.IsLetter(line[0]) && line[1] == ':')
                sawAbcField = true;
        }

        return sawAbcField ? MusicDialect.Abc : MusicDialect.Abc;
    }
}
