using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Svg.ViewModels;

namespace Nexaflow.Features.Svg.ClientTools;

/// <summary>
/// The AI tools the SVG viewer exposes. Both are read-only (they only read the loaded drawing), so they
/// auto-run. <c>render_svg_image</c> rasterizes the frozen drawing to a PNG so the model can actually see
/// the vector art — no live control needed, and tool invocations run on the UI sync context so
/// <see cref="RenderTargetBitmap"/> is safe to use here.
/// </summary>
internal static class SvgTools
{
    private const int MaxDimension = 1024;

    public static IReadOnlyList<IClientTool> For(SvgViewModel vm) =>
    [
        new DelegateClientTool(
            "render_svg_image",
            "Render this SVG to a raster image and return it so you can see the artwork.",
            [],
            ToolSafety.ReadOnly,
            (_, ct) => Render(vm, ct),
            exemptFromRepeatGuard: true),

        new DelegateClientTool(
            "get_svg_info",
            "Get the SVG's dimensions, viewBox and drawable-element count.",
            [],
            ToolSafety.ReadOnly,
            (_, _) => Task.FromResult(ToolResult.Ok(
                $"{vm.FileName}: {vm.DimensionsText}, {vm.ElementCount:N0} elements.", vm.DescribeInfo())),
            parallelizable: true),
    ];

    private static Task<ToolResult> Render(SvgViewModel vm, CancellationToken ct)
    {
        if (vm.Artifact is null)
            return Task.FromResult(ToolResult.Error("The SVG isn't loaded yet."));

        var png = RasterizeToPng(vm.Artifact);
        if (png is not { Length: > 0 })
            return Task.FromResult(ToolResult.Error("Couldn't render the SVG."));

        return Task.FromResult(ToolResult.Ok(
                $"Rendered {vm.FileName}.", "A raster render of the SVG is attached for you to look at.")
            with { Images = [new ContextImage(png, "image/png", vm.FileName)] });
    }

    /// <summary>Rasterizes the frozen drawing onto a white background (so transparent art is visible),
    /// scaled so its long side is at most <see cref="MaxDimension"/>px.</summary>
    private static byte[]? RasterizeToPng(DrawingImage image)
    {
        try
        {
            var drawing = image.Drawing;
            if (drawing is null) return null;

            var bounds = drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;

            double scale = MaxDimension / Math.Max(bounds.Width, bounds.Height);
            int pw = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            int ph = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pw, ph));
                dc.DrawImage(image, new Rect(0, 0, pw, ph)); // DrawingImage maps its bounds into the rect
            }

            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
