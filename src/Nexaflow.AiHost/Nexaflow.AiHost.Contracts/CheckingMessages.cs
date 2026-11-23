namespace Nexaflow.AiHost.Contracts;

/// <summary>The parts checking a picture uses, by role.</summary>
public static class CheckingParts
{
    /// <summary>What judges whether a picture is fit for a general audience: an image safety classifier, on the CPU.</summary>
    public const string SafetyClassifier = "safetyClassifier";
}

/// <summary>
/// How fit a picture is for a general audience — an image safety classifier a few milliseconds on the CPU, so it can be
/// asked of every picture made. The picture goes as its pixels, decoded by whoever holds it; the host lays them out as the
/// classifier takes them; one <see cref="PictureChecked"/> answers.
/// </summary>
/// <param name="Classifier">The <see cref="CheckingParts.SafetyClassifier"/>.</param>
/// <param name="Rgb">The picture, <paramref name="Width"/> × <paramref name="Height"/>, interleaved 8-bit RGB row by row.</param>
public sealed record CheckPicture(long RequestId, ModelSpec Classifier, int Width, int Height, SharedBytes Rgb) : HostRequest(RequestId);

/// <summary>The classifier's verdict as chances that sum to 1: that the picture shows gore, sexual content, or neither.</summary>
public sealed record PictureChecked(long RequestId, float Gore, float Sexual, float Neither) : HostReply(RequestId);
