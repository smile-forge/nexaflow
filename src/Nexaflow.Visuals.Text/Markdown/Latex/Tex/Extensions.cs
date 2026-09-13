using System.IO;
using System.Windows.Media.Imaging;
using WpfMath.Rendering;
using XamlMath;

namespace WpfMath;

public static class Extensions
{
    /// <summary>Default DPI for WPF.</summary>
    private const int DefaultDpi = 96;

    public static byte[] RenderToPng(this TexFormula texForm,
        double scale,
        double x,
        double y,
        string systemTextFontName)
    {
        var environment = WpfTeXEnvironment.Create(scale: scale, systemTextFontName: systemTextFontName);
        return texForm.RenderToPng(environment, scale, x, y);
    }

    /// <summary>Renders the formula to a PNG, in an environment already set up for it.</summary>
    /// <param name="texForm">The formula to render.</param>
    /// <param name="environment">The environment with rendering parameters.</param>
    /// <param name="scale">Formula text scale.</param>
    /// <param name="x">A physical X coordinate of the top left corner in the resulting image.</param>
    /// <param name="y">A physical Y coordinate of the top left corner in the resulting image.</param>
    /// <param name="dpi">
    /// The resulting image DPI. Raising it renders the same formula at more pixels, rather than a
    /// larger formula, which is what a print or a comparison against another renderer wants.
    /// </param>
    public static byte[] RenderToPng(this TexFormula texForm,
        TexEnvironment environment,
        double scale = 20.0,
        double x = 0.0,
        double y = 0.0,
        double dpi = DefaultDpi)
    {
        using var stream = new MemoryStream();
        texForm.WritePng(stream, environment, scale, x, y, dpi);
        return stream.ToArray();
    }

    /// <summary>Renders the formula to a PNG file.</summary>
    /// <param name="path">Where to write the file. An existing one is replaced.</param>
    /// <inheritdoc cref="RenderToPng(TexFormula, TexEnvironment, double, double, double, double)"/>
    public static void SaveAsPng(this TexFormula texForm,
        string path,
        TexEnvironment environment,
        double scale = 20.0,
        double x = 0.0,
        double y = 0.0,
        double dpi = DefaultDpi)
    {
        using var file = File.Create(path);
        texForm.WritePng(file, environment, scale, x, y, dpi);
    }

    /// <summary>Renders the formula as a PNG into <paramref name="stream"/>.</summary>
    /// <inheritdoc cref="RenderToPng(TexFormula, TexEnvironment, double, double, double, double)"/>
    public static void WritePng(this TexFormula texForm,
        Stream stream,
        TexEnvironment environment,
        double scale = 20.0,
        double x = 0.0,
        double y = 0.0,
        double dpi = DefaultDpi)
    {
        BitmapSource image = texForm.RenderToBitmap(environment, scale, x, y, dpi);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }
}
