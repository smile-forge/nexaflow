using System;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>Which notation language a fence is written in.</summary>
public enum MusicDialect { Abc, LilyPond }

public static class MusicDialectExtensions
{
    /// <summary>
    /// Which notation a fence calling itself <paramref name="tag"/> is written in — <c>abc</c>,
    /// <c>lilypond</c>, <c>ly</c> or <c>lily</c> — or null where it is written in neither.
    /// </summary>
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
}
