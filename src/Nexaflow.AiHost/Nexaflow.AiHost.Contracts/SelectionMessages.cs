namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts picking things out of a picture uses, by role.</summary>
public static class SelectingParts
{
    /// <summary>What reads the picture, once, for clicks and phrases alike.</summary>
    public const string ImageEncoder = "imageEncoder";

    /// <summary>What turns clicks and a box into a mask.</summary>
    public const string ClickDecoder = "clickDecoder";

    /// <summary>What reads a phrase.</summary>
    public const string TextEncoder = "textEncoder";

    /// <summary>What reads example boxes: like this, not like this.</summary>
    public const string ExemplarEncoder = "exemplarEncoder";

    /// <summary>What finds everything a phrase and its examples name.</summary>
    public const string Finder = "finder";

    /// <summary>Every part a selection may use.</summary>
    public static IReadOnlyList<string> All { get; } = [ImageEncoder, ClickDecoder, TextEncoder, ExemplarEncoder, Finder];
}

/// <summary>What a point given to a selection says.</summary>
public enum SelectionPointKind
{
    /// <summary>A click on the thing.</summary>
    Include,

    /// <summary>A click off it.</summary>
    Exclude,

    /// <summary>The top-left corner of a box drawn round it.</summary>
    BoxTopLeft,

    /// <summary>The bottom-right corner of a box drawn round it.</summary>
    BoxBottomRight,
}

/// <summary>A point on the picture, in its pixels.</summary>
public sealed record SelectionPoint(float X, float Y, SelectionPointKind Kind);

/// <summary>A box on the picture, in its pixels.</summary>
public sealed record SelectionBox(float Left, float Top, float Right, float Bottom);

/// <summary>An example box for a phrase: something <see cref="Like"/> what is meant, or something it does not mean.</summary>
public sealed record SelectionExample(SelectionBox Box, bool Like);

/// <summary>Where and at what size masks are written: <see cref="Width"/> × <see cref="Height"/> bytes each, row by row,
/// 255 over what is picked and 0 elsewhere, several one after another; the file is replaced if it is there.</summary>
public sealed record MaskTarget(string Into, int Width, int Height);

/// <summary>Something a phrase found: how surely, from 0.5 to 1, and the box round it in the picture's pixels.</summary>
public sealed record FoundInPicture(float Score, SelectionBox Box);

/// <summary>
/// Picks things out of a picture with SAM 3.1. The picture is read once, as the request arrives —
/// <see cref="PictureEncoded"/> says when — and each <see cref="SelectByPoints"/> and <see cref="SelectByPhrase"/>
/// written to the request is then answered from what was read, in turn, so a click costs the decoder alone. The request
/// lasts as long as its caller selects from that picture — a page holds one while it is open on the picture, and ends
/// it when it closes or moves to another — and what was read goes with it. Each answer's masks are written to the file
/// it names before its reply says so: masks of a large picture run to megabytes the pipe would carry as text.
/// </summary>
/// <param name="Parts">The parts it may use, by role (<see cref="SelectingParts"/>); each loads when first asked of.</param>
/// <param name="Pixels">The file holding the picture: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit RGBA,
/// row by row, nothing else. Its alpha is not read.</param>
public sealed record SelectInPicture(long RequestId, IReadOnlyDictionary<string, ModelSpec> Parts, string Pixels, int Width, int Height,
                                     RequestPriority Priority) : HostRequest(RequestId)
{
    /// <summary>The most pixels a picture may hold: 256 megapixels, a gigabyte of RGBA.</summary>
    public const long MostPixels = 256L * 1000 * 1000;

    /// <summary>The longest side a mask may be written at.</summary>
    public const int MostMaskSide = 32768;
}

/// <summary>The picture is read: selections are answered from here on.</summary>
public sealed record PictureEncoded(long RequestId) : HostReply(RequestId);

/// <summary>Picks the one thing clicks on it and off it, or a box round it, mean.</summary>
/// <param name="Turn">The caller's number for this ask, which its answer carries.</param>
public sealed record SelectByPoints(long RequestId, int Turn, IReadOnlyList<SelectionPoint> Points, MaskTarget Mask) : RequestInput(RequestId);

/// <summary>The points' mask is in the target's file. <see cref="Bounds"/> is the box round it in the picture's pixels;
/// null where nothing is picked there, its mask all 0.</summary>
public sealed record PointsSelected(long RequestId, int Turn, SelectionBox? Bounds) : HostReply(RequestId);

/// <summary>Finds everything a phrase names — "wheel", "child in a red vest" — helped by example boxes of what it does
/// and does not mean; with no phrase, everything like the examples.</summary>
public sealed record SelectByPhrase(long RequestId, int Turn, string Phrase, IReadOnlyList<SelectionExample> Examples, MaskTarget Masks)
    : RequestInput(RequestId);

/// <summary>What the phrase found, surest first; their masks are in the target's file in the same order.</summary>
public sealed record PhraseFound(long RequestId, int Turn, IReadOnlyList<FoundInPicture> Found) : HostReply(RequestId);
