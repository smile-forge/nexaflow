namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts erasing from a picture uses, by role.</summary>
public static class ErasingParts
{
    /// <summary>What fills a picture where a mask says, from what is round it.</summary>
    public const string Inpainter = "inpainter";

    /// <summary>Every part erasing uses.</summary>
    public static IReadOnlyList<string> All { get; } = [Inpainter];
}

/// <summary>The stages an erasure's <see cref="Progress"/> names, each counting done of total.</summary>
public static class ErasingStages
{
    /// <summary>Regions of the picture filled (<c>InpaintRegions</c>).</summary>
    public const string Filling = "filling";
}

/// <summary>
/// Erases from a picture what a mask covers, filling it from what is round it: each part of the mask worked in a region of
/// its surroundings, through the inpainter, smaller where a region is large and its fill brought back up. Only the mask's
/// pixels change. Each region takes the GPU at the request's priority and is reported as a <see cref="Progress"/>; the
/// picture is written to <see cref="Into"/> before one <see cref="PictureErased"/> says so. Pictures and mask go by file,
/// as a restoration's do.
/// </summary>
/// <param name="Parts">The parts it uses, by role (<see cref="ErasingParts.All"/>).</param>
/// <param name="Pixels">The file holding the picture: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit RGBA,
/// row by row, straight alpha, nothing else.</param>
/// <param name="Mask">The file holding the mask: one byte a pixel, row by row, filled where it is not 0.</param>
/// <param name="Into">The file the erased picture is written to, in the picture's form; replaced if it is there.</param>
public sealed record ErasePicture(long RequestId, IReadOnlyDictionary<string, ModelSpec> Parts, string Pixels, string Mask, int Width, int Height,
                                  string Into, RequestPriority Priority) : HostRequest(RequestId);

/// <summary>The erased picture is in the request's file, filled in <see cref="Regions"/> regions.</summary>
public sealed record PictureErased(long RequestId, int Regions) : HostReply(RequestId);
