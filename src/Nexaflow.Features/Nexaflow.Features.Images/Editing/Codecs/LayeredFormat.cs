using Nexaflow.Features.Images.Editing.Layers;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>What a layered picture's layers look like as pixels — what a layered format stores beside its own description of
/// them, so any reader draws the picture.</summary>
public interface ILayeredPictureRenderer
{
    /// <summary>
    /// A picture or markup layer drawn at full size by itself — its pixels and where their top-left lies on the canvas — the
    /// caller owning the bitmap; null where it shows nothing.
    /// </summary>
    (SKBitmap Pixels, SKPointI At)? RenderLayer(Layer layer);

    /// <summary>The whole picture composited at <paramref name="scale"/>, its background beneath; the caller owns it.</summary>
    SKImage Composite(double scale);
}

/// <summary>A layered picture as read, and whether it holds a layer no reader can draw — which opens on the look the
/// application that wrote it saved.</summary>
public sealed record LayeredPictureRead(LayeredPicture Picture, bool HoldsUndrawable);

/// <summary>
/// A file format that holds layers — OpenRaster first — read and written through the file system by its path, so opening one
/// reads only the entries the picture needs and a save replaces it whole and at once. Beside <see cref="IImageDecoder"/>,
/// which gives one picture.
/// </summary>
public interface ILayeredPictureFormat
{
    /// <summary>Whether this format reads and writes <paramref name="container"/>.</summary>
    bool Reads(ImageContainer container);

    /// <summary>The usual file extension, with its dot.</summary>
    string Extension { get; }

    /// <summary>Whether a picture held in <paramref name="colour"/> can be written as this format without losing what it holds.</summary>
    bool HoldsColour(PictureColour colour);

    /// <summary>The file the viewer decodes to show the picture at <paramref name="path"/> as the application that wrote it
    /// drew it — its saved look, which a picture holding layers no reader draws opens on.</summary>
    string SavedLookOf(string path);

    /// <exception cref="System.IO.InvalidDataException">It is not a picture of this format.</exception>
    Task<LayeredPictureRead> ReadAsync(string path, CancellationToken ct = default);

    /// <summary>Writes <paramref name="picture"/> to <paramref name="path"/>, whole, its layers drawn by <paramref name="renderer"/>.</summary>
    Task WriteAsync(LayeredPicture picture, string path, ILayeredPictureRenderer renderer, CancellationToken ct = default);
}
