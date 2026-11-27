using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// How content laid line by line is set on the page — code, text shown as typed: how far apart its tab stops stand, whether its
/// whitespace is shown, and whether a line too long for the page slides sideways rather than being cut off at the page's edge.
/// </summary>
public sealed partial class NexaTextSurface
{
    /// <summary>How many characters' advance apart tab stops stand in content laid line by line. Four, unless the host says otherwise.</summary>
    public static readonly DependencyProperty TabWidthProperty = DependencyProperty.Register(
        nameof(TabWidth), typeof(int), typeof(NexaTextSurface),
        new PropertyMetadata(4, (surface, _) => ((NexaTextSurface)surface).Remake()), width => width is int value && value > 0);

    public int TabWidth
    {
        get => (int)GetValue(TabWidthProperty);
        set => SetValue(TabWidthProperty, value);
    }

    /// <summary>Whether the spaces, tabs and line ends of content laid line by line are drawn as faint marks.</summary>
    public static readonly DependencyProperty ShowsWhitespaceProperty = DependencyProperty.Register(
        nameof(ShowsWhitespace), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, _) => ((NexaTextSurface)surface).Remake()));

    public bool ShowsWhitespace
    {
        get => (bool)GetValue(ShowsWhitespaceProperty);
        set => SetValue(ShowsWhitespaceProperty, value);
    }

    /// <summary>
    /// Whether the page slides sideways — for code and text whose lines are kept whole, a line too long for the page reached by sliding
    /// rather than cut off at its edge. The content is still laid in the page's width; only what reaches past it slides into view.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => _horizontal;
        set
        {
            if (_horizontal == value) return;

            _horizontal = value;
            _engine.ViewSlides = value != ScrollBarVisibility.Disabled;

            Slid();
            Synced();
        }
    }

    /// <summary>
    /// The width content is laid in while the page slides sideways — the page's own, less the room kept round the content — or null where
    /// it does not slide, and the content is laid in whatever it is given.
    /// </summary>
    private double? SlidingRoom()
    {
        if (_horizontal == ScrollBarVisibility.Disabled) return null;

        var page = _page.ActualWidth > 0 ? _page.ActualWidth : ActualWidth;
        return page > 0 ? Math.Max(1, page - ContentPadding.Left - ContentPadding.Right) : null;
    }

    /// <summary>Tells the element the width it is laid in while the page slides sideways.</summary>
    private void Slid()
    {
        var room = SlidingRoom();
        if (_shown.SlidingRoom == room) return;

        _shown.SlidingRoom = room;
        _shown.InvalidateMeasure();
    }

    /// <summary>What the content says can be folded, in order — for what stands beside its lines (<see cref="FoldMargin"/>).</summary>
    internal IReadOnlyList<(int Start, int End)> FoldsOffered() => _engine.Foldable();

    /// <summary>Whether the line the caret is on is washed across the page's width, as a code editor shows where it is writing.</summary>
    public static readonly DependencyProperty HighlightsCaretLineProperty = DependencyProperty.Register(
        nameof(HighlightsCaretLine), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, args) => ((NexaTextSurface)surface)._shown.HighlightsCaretLine = (bool)args.NewValue));

    public bool HighlightsCaretLine
    {
        get => (bool)GetValue(HighlightsCaretLineProperty);
        set => SetValue(HighlightsCaretLineProperty, value);
    }

    /// <summary>
    /// Whether the code shown is too long to be coloured, folded and read as code at the pace of typing, and is shown as written instead — for
    /// a host to say so where the reader would otherwise wonder where its colours went.
    /// </summary>
    public bool ColoursWithheld => Lined && ContentEngine.Language(Named).Syntax is not null && Document.Current.Length > Code.CodeSyntax.ColoursUpTo;
}
