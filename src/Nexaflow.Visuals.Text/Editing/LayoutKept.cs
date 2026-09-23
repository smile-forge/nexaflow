using System;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A piece's picture, recorded the first time it is painted and drawn whole from then on — for a piece that holds a great
/// many marks and does not change for as long as it lasts: a block of a document.
///
/// <para>
/// A run of text re-formats its lines every time it is drawn, whatever it is drawn in, so painting a tree mark by mark
/// costs as much the hundredth time as the first — and a caret blinking, a selection growing and a key typed elsewhere on
/// the page are all a paint. Recorded, the text is glyphs already placed, and drawing it again is handing the recording
/// over.
/// </para>
/// <para>
/// Carried with the piece wherever its tree is put down, because a tree set into another keeps what each piece is painted
/// with (<see cref="LayoutPaint"/>). So a block laid once and set down in the next layout of its document brings the
/// picture it was painted as, and nothing of it is painted again.
/// </para>
/// </summary>
public sealed class LayoutKept
{
    private (Brush Ink, Drawing Drawing)[] _kept = [];

    /// <summary>
    /// The picture painted with <paramref name="ink"/> for whatever draws in no colour of its own — recorded by
    /// <paramref name="paint"/> the first time it is asked for.
    /// </summary>
    public Drawing For(Brush ink, Action<DrawingContext> paint)
    {
        foreach (var (kept, drawing) in _kept)
            if (ReferenceEquals(kept, ink)) return drawing;

        var recorded = new DrawingGroup();
        using (var dc = recorded.Open()) paint(dc);
        if (recorded.CanFreeze) recorded.Freeze();

        // Hardly ever more than one: the page's own ink, and the accent a piece being carried is drawn in.
        _kept = _kept.Length < 4 ? [.. _kept, (ink, recorded)] : [(ink, recorded)];
        return recorded;
    }
}
