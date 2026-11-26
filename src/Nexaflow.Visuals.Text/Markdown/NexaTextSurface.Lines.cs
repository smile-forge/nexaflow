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
        get => _scroller.HorizontalScrollBarVisibility;
        set
        {
            _scroller.HorizontalScrollBarVisibility = value;
            Slid();
        }
    }

    /// <summary>Takes up the page sliding sideways: laid in its width as it changes, and turned sideways by the wheel with Shift held.</summary>
    private void Sliding()
    {
        _scroller.ScrollChanged += (_, args) =>
        {
            if (args.ViewportWidthChange != 0) Slid();
        };

        _scroller.PreviewMouseWheel += (_, args) =>
        {
            if (args.Handled || Keyboard.Modifiers != ModifierKeys.Shift || _scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled) return;

            _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset - args.Delta);
            args.Handled = true;
        };
    }

    /// <summary>
    /// The width content is laid in while the page slides sideways — the page's own, less the room kept round the content — or null where
    /// it does not slide, and the content is laid in whatever it is given.
    /// </summary>
    private double? SlidingRoom()
    {
        if (_scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled) return null;

        var page = _scroller.ViewportWidth > 0 ? _scroller.ViewportWidth : _scroller.ActualWidth;
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

    /// <summary>Whether the line the caret is on is washed across the page's width, as a code editor shows where it is writing.</summary>
    public static readonly DependencyProperty HighlightsCaretLineProperty = DependencyProperty.Register(
        nameof(HighlightsCaretLine), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, args) => ((NexaTextSurface)surface)._shown.HighlightsCaretLine = (bool)args.NewValue));

    public bool HighlightsCaretLine
    {
        get => (bool)GetValue(HighlightsCaretLineProperty);
        set => SetValue(HighlightsCaretLineProperty, value);
    }
}
