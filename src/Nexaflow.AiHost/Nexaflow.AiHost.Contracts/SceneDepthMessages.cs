namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts estimating a picture's scene depth uses, by role.</summary>
public static class SceneDepthParts
{
    /// <summary>What estimates how near each part of a picture is to the camera: Depth Anything V2 Small.</summary>
    public const string Estimator = "depthEstimator";

    /// <summary>Every part scene depth uses.</summary>
    public static IReadOnlyList<string> All { get; } = [Estimator];
}

/// <summary>
/// Estimates how far each part of a picture lies from the camera — its scene depth, not its colour depth — from the one
/// picture: brought to the size the estimator reads, as its makers' pipeline prepares it, through the estimator at the
/// request's priority, and its relative disparity written to <see cref="Into"/> before one <see cref="SceneDepthEstimated"/>
/// says so. The picture goes by file, as an erasure's does.
/// </summary>
/// <param name="Parts">The parts it uses, by role (<see cref="SceneDepthParts.All"/>).</param>
/// <param name="Pixels">The file holding the picture: <see cref="Width"/> × <see cref="Height"/> interleaved 8-bit RGBA, row by
/// row, straight alpha, nothing else.</param>
/// <param name="Into">The file the disparity is written to; replaced if it is there.</param>
public sealed record EstimateSceneDepth(long RequestId, IReadOnlyDictionary<string, ModelSpec> Parts, string Pixels, int Width, int Height,
                                        string Into, RequestPriority Priority) : HostRequest(RequestId);

/// <summary>
/// The disparity is in the request's file: <see cref="Width"/> × <see cref="Height"/> single-precision floats, row by row —
/// the estimator's relative inverse depth, larger nearer, its scale and offset its own — at the size it read the picture,
/// whose corners are the picture's corners.
/// </summary>
public sealed record SceneDepthEstimated(long RequestId, int Width, int Height) : HostReply(RequestId);
