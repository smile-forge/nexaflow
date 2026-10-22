using System.Collections.Generic;

namespace Nexaflow.Features.Video.Models;

/// <summary>
/// One labelled fact in the info panel (e.g. "Resolution" → "1920 × 1080"), as the panel shows it in the app's
/// language. <see cref="ReportLabel"/> and <see cref="ReportValue"/> state the same fact in the English media-info
/// report for the assistant; they are set wherever the shown text is translated, and repeat it when left unset.
/// </summary>
public sealed record MediaInfoRow(string Label, string Value)
{
    /// <summary>The fact's name in the English report for the assistant.</summary>
    public string ReportLabel { get; init; } = Label;

    /// <summary>The fact's value in the English report — the shown value unless that is itself a word (a channel layout).</summary>
    public string ReportValue { get; init; } = Value;
}

/// <summary>A selectable subtitle track in the CC menu. <c>Id</c> null means "Off".</summary>
public sealed record SubtitleTrackOption(string? Id, string Label);

/// <summary>A titled group of <see cref="MediaInfoRow"/>s (a track, or the file header).</summary>
public sealed class MediaInfoSection
{
    /// <summary>The section's title as the info panel shows it.</summary>
    public required string Header { get; init; }

    /// <summary>The section's title in the English report for the assistant; the shown title unless set.</summary>
    public string ReportHeader
    {
        get => _reportHeader ?? Header;
        init => _reportHeader = value;
    }
    private readonly string? _reportHeader;

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
