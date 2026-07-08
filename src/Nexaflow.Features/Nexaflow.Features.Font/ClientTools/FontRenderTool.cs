using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Font.ViewModels;

namespace Nexaflow.Features.Font.ClientTools;

/// <summary>
/// Renders a font from the compare list to a PNG image so the AI can actually see the glyph shapes —
/// the sample text (big) plus the alphabet/digits/symbols specimen (small), matching the on-screen row,
/// in whatever font the user picks. Read-only. The harness keeps the UI sync context, so it renders on
/// the UI thread. Reference a font by its 1-based index or name; text and size can be overridden.
/// </summary>
public sealed class FontRenderTool(FontViewModel vm) : IClientTool
{
    public string Name => "render_font_preview";

    public string Description =>
        "Render a font from the compare list to an image you can look at (the sample text plus a small " +
        "alphabet/digits/symbols specimen, in that font). Use it to judge or describe how a font looks. " +
        "Reference the font by its 1-based index or name; omit for the selected font. Optionally override " +
        "the text and point size.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("font", "The font to render: its 1-based index in the compare list, or its name. " +
                    "Omit for the currently selected font.", Required: false),
        new("text", "Text to render. Omit to use the viewer's current preview text.", Required: false),
        new("size", "Point size for the large line. Omit to use the viewer's current size.",
            Required: false, Type: "number"),
    ];

    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => false;   // renders on the UI thread

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var item = vm.ResolveFont(FontToolArgs.GetString(arguments, "font"));
        if (item is null)
            return Task.FromResult(ToolResult.Error(
                "No matching font.",
                "No font matched that reference. Reference a font by its 1-based index or name."));
        if (!item.CanRender)
            return Task.FromResult(ToolResult.Error(
                $"{item.DisplayName} can't be rendered.",
                $"\"{item.DisplayName}\" can't be rendered: {item.LoadError}"));

        item.EnsureFacesLoaded();
        var text = FontToolArgs.GetString(arguments, "text") is { Length: > 0 } t ? t : vm.Options.PreviewText;
        double sizePt = double.TryParse(FontToolArgs.GetString(arguments, "size"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 6, 200) : vm.Options.PreviewSizePt;

        try
        {
            var png = Render(item, text, sizePt);
            return Task.FromResult(ToolResult.Ok(
                    $"Rendered \"{item.DisplayName}\".",
                    $"An image of \"{text}\" rendered in {item.DisplayName} at {sizePt:0}pt " +
                    "(with a small alphabet/digits/symbols specimen) is attached for you to look at.")
                with { Images = [new ContextImage(png, "image/png", item.DisplayName)] });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Couldn't render {item.DisplayName}.", ex.Message));
        }
    }

    private byte[] Render(FontItemViewModel item, string text, double sizePt)
    {
        // Theme brushes at paint time, with literal fallbacks (per docs/theming.md for code-drawn surfaces).
        var fg = Application.Current?.Resources["TextBrush"] as Brush ?? Brushes.Black;
        var bg = Application.Current?.Resources["BgBrush"] as Brush ?? Brushes.White;

        var typeface = new Typeface(item.FontFamily, item.EffectiveStyle, item.EffectiveWeight, item.EffectiveStretch);
        var specimenFace = new Typeface(item.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        const double pixelsPerDip = 1.0;
        var big = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, sizePt * 4.0 / 3.0, fg, pixelsPerDip) { MaxTextWidth = 1200 };
        if (item.EffectiveDecorations is { } dec) big.SetTextDecorations(dec);

        var small = new FormattedText(vm.Options.SpecimenText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            specimenFace, vm.Options.SpecimenSizeDip, fg, pixelsPerDip) { MaxTextWidth = 1200 };

        const double pad = 20, gap = 14;
        double width = Math.Max(big.WidthIncludingTrailingWhitespace, small.WidthIncludingTrailingWhitespace) + pad * 2;
        double height = big.Height + gap + small.Height + pad * 2;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawText(big, new Point(pad, pad));
            dc.DrawText(small, new Point(pad, pad + big.Height + gap));
        }

        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
