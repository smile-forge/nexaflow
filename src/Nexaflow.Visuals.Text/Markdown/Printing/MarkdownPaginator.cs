using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Printing;

/// <summary>
/// A markdown document on paper. It is laid out as the app draws it, the whole of it at the width a sheet leaves inside its
/// margins, cut into sheets where nothing is cut through (<see cref="PaperPages"/>), and each sheet is painted by the painter
/// the screen is painted by, with its number under it — so what is printed is what is shown, a diagram as much as a word.
/// The print system asks for a sheet at a time, and a sheet is painted only when it is asked for.
/// </summary>
public sealed class MarkdownPaginator : DocumentPaginator
{
    /// <summary>The margin round what a sheet shows, inside the area the printer can print on: half an inch.</summary>
    public const double Margin = 48;

    private readonly string _markdown;
    private readonly StyleFormat _style;
    private readonly ContentInputs _inputs;
    private readonly string? _language;

    private Size _sheet;
    private Laid _laid = Laid.Nothing;
    private IReadOnlyList<PaperPage> _pages = [];

    /// <param name="markdown">What is printed.</param>
    /// <param name="sheet">The area of a sheet the printer can print on, in device-independent pixels.</param>
    /// <param name="style">The palette and faces it is printed in.</param>
    /// <param name="inputs">Where its pictures, links and data come from.</param>
    /// <param name="language">The one language it is written in, where it is content in one language rather than a document.</param>
    public MarkdownPaginator(string markdown, Size sheet, StyleFormat style, ContentInputs? inputs = null, string? language = null)
    {
        _markdown = markdown;
        _style = style;
        _inputs = inputs ?? ContentInputs.None;
        _language = language;

        PageSize = sheet;
    }

    /// <summary>The sheets, as stretches of the document laid out as one long page.</summary>
    public IReadOnlyList<PaperPage> Pages => _pages;

    /// <summary>What a sheet holds the document in: the sheet, less its margins.</summary>
    public Size Room => new(Math.Max(1, _sheet.Width - (2 * Margin)), Math.Max(1, _sheet.Height - (2 * Margin)));

    public override bool IsPageCountValid => true;

    public override int PageCount => _pages.Count;

    /// <summary>The area of a sheet the printer can print on. Another size lays the document out again, at the width it leaves.</summary>
    public override Size PageSize
    {
        get => _sheet;
        set
        {
            _sheet = value;

            var room = Room;
            _laid = new ContentEngine(_inputs).Lay(_language, EditState.For(_markdown), _style, room.Width, readOnly: true);
            _pages = PaperPages.Cut(_laid, room);
        }
    }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pages.Count) return DocumentPage.Missing;

        var room = Room;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            var down = Margin;

            foreach (var band in _pages[pageNumber].Bands)
            {
                dc.PushTransform(new TranslateTransform(Margin, down));
                dc.PushTransform(new ScaleTransform(band.Scale, band.Scale));
                dc.PushTransform(new TranslateTransform(0, -band.Top));

                // Its own stretch of the page and no more, inside the margins: nothing is printed in them but the sheet's number.
                var showing = new Rect(0, band.Top, room.Width / band.Scale, band.Height);
                dc.PushClip(new RectangleGeometry(showing));
                LayoutPainter.Paint(dc, _laid.Root, _style.Text, showing);

                for (var pushed = 0; pushed < 4; pushed++) dc.Pop();

                down += band.Drawn;
            }

            Numbered(dc, pageNumber);
        }

        return new DocumentPage(visual, _sheet, new Rect(_sheet), new Rect(new Point(Margin, Margin), room));
    }

    /// <summary>The sheet's number, and how many there are, under the middle of what it shows.</summary>
    private void Numbered(DrawingContext dc, int pageNumber)
    {
        var number = new FormattedText(
            string.Create(CultureInfo.CurrentCulture, $"{pageNumber + 1} / {_pages.Count}"),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _style.Face(), _style.TextSize * 0.75, _style.TextMuted,
            pixelsPerDip: 1);

        dc.DrawText(number, new Point((_sheet.Width - number.Width) / 2, _sheet.Height - ((Margin + number.Height) / 2)));
    }
}
