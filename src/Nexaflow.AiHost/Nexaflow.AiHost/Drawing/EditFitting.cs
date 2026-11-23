using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Imaging;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// An edit's pictures as its model reads them. Each picture to work from is resized — Lanczos over eight taps — to the size
/// its model reads a picture of its proportions at, stretched by the few pixels its sides are cut by rather than cropped, so
/// the picture drawn lies back over the first exactly. The edit is drawn at the first picture's size as read, and the mask
/// over the first is brought to that size with it. An app sends its pictures as they are; what a model reads is the host's.
/// </summary>
internal static class EditFitting
{
    /// <summary><paramref name="request"/> with its pictures and mask at the sizes <paramref name="readSize"/> gives, drawn at
    /// the first one's.</summary>
    /// <exception cref="HostRequestException">A picture or the mask is not the size it says, or a picture is too narrow to
    /// read.</exception>
    public static Draw Fitted(Draw request, Func<int, int, (int Width, int Height)> readSize)
    {
        if (request.Sources.Count == 0) throw new ArgumentException("An edit works from at least one picture.", nameof(request));

        var sources = new DrawingSource[request.Sources.Count];
        for (int i = 0; i < sources.Length; i++)
        {
            var source = request.Sources[i];
            long bytes = (long)source.Width * source.Height * 3;
            if (source.Width <= 0 || source.Height <= 0 || source.Rgb.Length != bytes)
                throw new HostRequestException(HostErrorKind.Invalid, $"A {source.Width}x{source.Height} picture is {bytes} bytes of RGB, not {source.Rgb.Length}.");
            var (width, height) = readSize(source.Width, source.Height);
            if (width <= 0 || height <= 0)
                throw new HostRequestException(HostErrorKind.Invalid, $"A {source.Width}x{source.Height} picture is too narrow for this model to read.");
            sources[i] = (width, height) == (source.Width, source.Height)
                ? source
                : new DrawingSource(width, height, ReferenceResampling.ResizeLanczos(source.Rgb, source.Width, source.Height, 3, width, height));
        }

        var (first, drawn) = (request.Sources[0], sources[0]);
        var mask = request.Mask;
        if (mask is not null)
        {
            if (mask.Length != (long)first.Width * first.Height)
                throw new HostRequestException(HostErrorKind.Invalid, $"A mask over a {first.Width}x{first.Height} picture is {(long)first.Width * first.Height} bytes, not {mask.Length}.");
            if ((drawn.Width, drawn.Height) != (first.Width, first.Height))
                mask = ReferenceResampling.ResizeLanczos(mask, first.Width, first.Height, 1, drawn.Width, drawn.Height);
        }
        return request with { Width = drawn.Width, Height = drawn.Height, Sources = sources, Mask = mask };
    }
}
