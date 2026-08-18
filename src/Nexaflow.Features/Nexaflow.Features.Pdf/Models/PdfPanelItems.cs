using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Pdf.Models;

/// <summary>One label/value row in the Properties tab.</summary>
/// <param name="Label">What the value is.</param>
/// <param name="Value">The value itself, already formatted for display.</param>
public sealed record PdfInfoRow(string Label, string Value)
{
    /// <summary>What "copy all properties" writes for this row.</summary>
    public override string ToString() => $"{Label}: {Value}";
}

/// <summary>
/// One row of the Contents tab. Indented by its outline level, and clickable only when it actually points
/// at a page in this document — <see cref="CanJump"/> is what stops the template offering a link that
/// would do nothing.
/// </summary>
public sealed partial class PdfOutlineItem(string title, int level, int? pageNumber, double? offsetFromTop = null)
    : ObservableObject
{
    public string  Title      { get; } = title;
    public int     Level      { get; } = level;
    public int?    PageNumber { get; } = pageNumber;

    /// <summary>How far below the top of its page the destination sits, in points; null for a destination
    /// that names only a page. Measured downwards — see <c>PdfOutlineEntry.OffsetFromTop</c>.</summary>
    public double? OffsetFromTop { get; } = offsetFromTop;

    /// <summary>
    /// Left indent for this row, in device-independent pixels.
    /// <para>
    /// Capped, because nesting depth in a real document is not bounded by anything sensible and an outline a
    /// dozen levels deep would otherwise indent its titles off the edge of a narrow panel. Past the cap the
    /// rows stop moving right; the level is still legible from the ones above.
    /// </para>
    /// </summary>
    public double IndentWidth => Math.Min(Level, MaxIndentLevels) * 14.0;

    private const int MaxIndentLevels = 6;

    /// <summary>The page label shown on the right, or empty when the entry targets no page here.</summary>
    public string PageLabel => PageNumber is int p ? $"p. {p}" : string.Empty;

    /// <summary>
    /// Whether clicking this row can move the rendered view. False either because the entry has no page in
    /// this document, or because the renderer turned out not to honour page navigation on this machine.
    /// </summary>
    [ObservableProperty] private bool _canJump;

    /// <summary>What the row's context menu copies.</summary>
    public string CopyText     => Title;
    public string CopyWithPage => PageNumber is int p ? $"{Title} — p. {p}" : Title;
}
