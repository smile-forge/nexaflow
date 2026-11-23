using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.IO.Common.Video;

namespace Nexaflow.Tests.AiHost;

/// <summary>What the host sends as pixels, made into the files a run's results attach and the tests read back — as the app makes
/// them, the host's part ending at the pixels.</summary>
internal static class HostPictures
{
    /// <summary>A drawn picture as a PNG.</summary>
    public static byte[] AsPng(this Drawn drawn) => PngEncoder.EncodeRgb(drawn.Rgb, drawn.Width, drawn.Height);

    /// <summary>A clip's frames, as the host sent them, as an H.264 MP4.</summary>
    public static byte[] AsMp4(this IEnumerable<FilmedFrames> frames, int width, int height, int framesPerSecond)
    {
        using var mp4 = new MemoryStream();
        using (var encoder = new Mp4Encoder(mp4, width, height, framesPerSecond))
        {
            int frameBytes = width * height * 3;
            foreach (var sent in frames)
                for (int at = 0; at + frameBytes <= sent.Rgb.Length; at += frameBytes) encoder.AddFrame(sent.Rgb.AsSpan(at, frameBytes));
            encoder.Finish();
        }
        return mp4.ToArray();
    }
}
