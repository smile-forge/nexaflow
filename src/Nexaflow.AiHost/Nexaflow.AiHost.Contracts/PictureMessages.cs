namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts a picture's restoration may use, by role.</summary>
public static class RestoringParts
{
    /// <summary>What makes a picture twice the size, and cleans one kept at its size.</summary>
    public const string UpscalerX2 = "upscalerX2";

    /// <summary>What makes a picture four times the size.</summary>
    public const string UpscalerX4 = "upscalerX4";

    /// <summary>What finds the faces in a picture, with five points on each.</summary>
    public const string FaceDetector = "faceDetector";

    /// <summary>What remakes a face aligned to its template.</summary>
    public const string FaceRestorer = "faceRestorer";

    /// <summary>What marks, on a remade face, the part pasted back over the picture.</summary>
    public const string FaceParser = "faceParser";

    /// <summary>The upscaler a picture is restored with at <paramref name="scale"/>: four times by its own, else twice —
    /// at its own size the picture is made twice the size and brought back.</summary>
    public static string UpscalerFor(int scale) => scale == 4 ? UpscalerX4 : UpscalerX2;

    /// <summary>The parts restoring at <paramref name="scale"/> uses, faces repaired or not.</summary>
    public static IReadOnlyList<string> Of(int scale, bool faces)
        => faces ? [UpscalerFor(scale), FaceDetector, FaceRestorer, FaceParser] : [UpscalerFor(scale)];
}

/// <summary>The stages a restoration's <see cref="Progress"/> names, each counting done of total.</summary>
public static class RestoringStages
{
    /// <summary>Tiles of the picture through the upscaler.</summary>
    public const string Upscaling = "upscaling";

    /// <summary>Faces found, remade and pasted back.</summary>
    public const string Faces = "faces";
}

/// <summary>
/// Restores a picture: through an upscaler a tile at a time, at twice or four times its size or kept at its own and
/// cleaned, and — where <see cref="Faces"/> — each face found remade and pasted back over it. Each call takes the GPU at
/// the request's priority; each tile and face is reported as a <see cref="Progress"/>, and the picture is written to
/// <see cref="Into"/> before one <see cref="PictureRestored"/> says so. The pixels go by file both ways: the app and
/// the host share a machine, and a restored picture runs to hundreds of megabytes the pipe would carry as text.
/// </summary>
/// <param name="Parts">The parts it uses, by role (<see cref="RestoringParts.Of"/>).</param>
/// <param name="Pixels">The file holding the picture: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit RGBA,
/// row by row, straight alpha, nothing else.</param>
/// <param name="Scale">1, 2 or 4: how many times the size the picture comes back.</param>
/// <param name="Into">The file the restored picture is written to, in the same form; replaced if it is there.</param>
public sealed record RestorePicture(long RequestId, IReadOnlyDictionary<string, ModelSpec> Parts, string Pixels, int Width, int Height,
                                    int Scale, bool Faces, string Into, RequestPriority Priority) : HostRequest(RequestId)
                                    {
                                        /// <summary>The most pixels the upscaler's result may hold — at its own factor, before a picture kept at its size is
                                        /// brought back: 128 megapixels, half a gigabyte of RGBA, so a 32-megapixel photo is cleaned or doubled, an 8-megapixel
                                        /// one made four times the size.</summary>
                                        public const long MostUpscaledPixels = 128L * 1000 * 1000;

                                        /// <summary>Whether a <paramref name="width"/> × <paramref name="height"/> picture is restored at <paramref name="scale"/>.</summary>
                                        public static bool Restores(int width, int height, int scale)
                                        {
                                            if (scale is not (1 or 2 or 4) || width <= 0 || height <= 0) return false;
                                            long factor = scale == 4 ? 4 : 2;
                                            return (long)width * height * factor * factor <= MostUpscaledPixels;
                                        }
                                    }

/// <summary>The restored picture is in the request's file: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit
/// RGBA, with <see cref="FacesRestored"/> faces remade in it.</summary>
public sealed record PictureRestored(long RequestId, int Width, int Height, int FacesRestored) : HostReply(RequestId);
