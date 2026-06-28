using System.Collections.Generic;

namespace Nexaflow.Features.Video.Models;

/// <summary>One labelled fact in the info panel (e.g. "Resolution" → "1920 × 1080").</summary>
public sealed record MediaInfoRow(string Label, string Value);

/// <summary>A selectable subtitle track in the CC menu. <c>Id</c> null means "Off".</summary>
public sealed record SubtitleTrackOption(string? Id, string Label);

/// <summary>A titled group of <see cref="MediaInfoRow"/>s (a track, or the file header).</summary>
public sealed class MediaInfoSection
{
    public required string Header { get; init; }
    public List<MediaInfoRow> Rows { get; } = [];
}

/// <summary>
/// Parsed container/codec/track facts for the open video, built from libvlc's parsed media + tracks.
/// Drives the toggleable info panel; <see cref="Summary"/> is the one-line form fed to the AI context.
/// </summary>
public sealed class MediaInfo
{
    public required string FileName { get; init; }
    public IReadOnlyList<MediaInfoSection> Sections { get; init; } = [];

    /// <summary>Compact single-line description for AI context, e.g. "1920×1080 H264, 00:03:21, AAC stereo".</summary>
    public required string Summary { get; init; }
}
