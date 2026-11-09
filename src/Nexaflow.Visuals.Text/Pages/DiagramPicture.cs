using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Pages;

/// <summary>
/// A Mermaid diagram drawn again from a figure, as Nexaflow draws it — the words it draws, and how it looks beside the figure —
/// for the drawing to be checked against what it was drawn from (<see cref="DiagramDrawing"/>). Laid out and painted on a thread
/// of its own, away from every window, so a long conversion neither waits on the screen nor holds it up.
/// </summary>
public static class DiagramPicture
{
    /// <summary>How wide a diagram is laid out, in device-independent pixels.</summary>
    private const double Room = 900;

    /// <summary>How tall each of the two pictures is drawn side by side, so neither looks the lesser.</summary>
    private const double Height = 640;

    /// <summary>The widest either is drawn, a very wide one drawn shorter instead.</summary>
    private const double Widest = 1000;

    /// <summary>The space round and between the two pictures, and the band their letters stand in.</summary>
    private const double Gap = 24, Band = 48;

    /// <summary>Every run of words the diagram draws, in the order it draws them.</summary>
    public static Task<IReadOnlyList<string>> WordsAsync(string mermaid) => OnThreadOfItsOwn(() => Words(Laid(mermaid)));

    /// <summary>
    /// The figure in <paramref name="figure"/> on the left under an A and the diagram drawn on the right under a B, as one PNG on
    /// white — one picture, so a model that is shown one at a time sees both.
    /// </summary>
    public static Task<byte[]> SideBySideAsync(byte[] figure, string mermaid) => OnThreadOfItsOwn(() => SideBySide(figure, Laid(mermaid)));

    private static Laid Laid(string mermaid) => new ContentEngine().Lay("mermaid", EditState.For(mermaid), WpfStyles.Light, Room, readOnly: true);

    private static IReadOnlyList<string> Words(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Words && piece.Words is not null).Select(piece => piece.Words!.Glyphs.Text)];

    private static byte[] SideBySide(byte[] figure, Laid laid)
    {
        BitmapSource original;
        using (var stream = new MemoryStream(figure))
            original = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];

        var (left, right) = (Fitted(original.PixelWidth, original.PixelHeight), Fitted(laid.Size.Width, laid.Size.Height));
        var size = new Size(Gap + left.Width + Gap + right.Width + Gap, Band + Math.Max(left.Height, right.Height) + Gap);
        var ink = WpfStyles.Light.Text.Brush();

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(size));
            Letter(dc, "A", Gap, ink);
            Letter(dc, "B", Gap + left.Width + Gap, ink);
            dc.DrawImage(original, new Rect(Gap, Band, left.Width, left.Height));

            dc.PushTransform(new TranslateTransform(Gap + left.Width + Gap, Band));
            dc.PushTransform(new ScaleTransform(right.Width / Math.Max(1, laid.Size.Width), right.Height / Math.Max(1, laid.Size.Height)));
            WpfPainter.Paint(dc, laid.Root, ink);
            dc.Pop();
            dc.Pop();
        }

        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        picture.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    /// <summary>A picture <paramref name="width"/> by <paramref name="height"/> drawn <see cref="Height"/> tall, or <see cref="Widest"/> wide where that is less.</summary>
    private static Size Fitted(double width, double height)
    {
        var scale = Math.Min(Height / Math.Max(1, height), Widest / Math.Max(1, width));
        return new Size(Math.Max(1, width * scale), Math.Max(1, height * scale));
    }

    private static void Letter(DrawingContext dc, string letter, double x, Brush ink) =>
        dc.DrawText(new FormattedText(letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 28, ink, 1.0), new Point(x, 6));

    /// <summary>
    /// <paramref name="work"/> done on a thread of its own that WPF can draw on, its dispatcher shut down when it is done so
    /// nothing of it is left behind.
    /// </summary>
    private static Task<T> OnThreadOfItsOwn<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
            finally { Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown(); }
        })
        { IsBackground = true, Name = "Diagram picture" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
