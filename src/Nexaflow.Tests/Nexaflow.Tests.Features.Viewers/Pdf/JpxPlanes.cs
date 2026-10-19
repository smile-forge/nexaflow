using Nexaflow.Media.Jpeg2000;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>One decoded component, as either decoder hands it back before any colour work.</summary>
internal sealed record JpxPlane(int Width, int Height, int Precision, bool Signed, int[] Samples);

/// <summary>Every component of one decoded image.</summary>
internal sealed record JpxPlanes(IReadOnlyList<JpxPlane> Planes)
{
    /// <summary>
    /// How these samples stand beside another decoder's: <c>identical</c>, or the first thing that differs.
    /// </summary>
    public string Against(JpxPlanes other)
    {
        if (Planes.Count != other.Planes.Count) return $"components {Planes.Count} vs {other.Planes.Count}";

        for (var at = 0; at < Planes.Count; at++)
        {
            var (mine, theirs) = (Planes[at], other.Planes[at]);
            if (mine.Width != theirs.Width || mine.Height != theirs.Height)
                return $"component {at} is {mine.Width}x{mine.Height} vs {theirs.Width}x{theirs.Height}";
            if (mine.Precision != theirs.Precision || mine.Signed != theirs.Signed)
                return $"component {at} precision {mine.Precision}{(mine.Signed ? "s" : "u")} vs {theirs.Precision}{(theirs.Signed ? "s" : "u")}";
            if (mine.Samples.Length < mine.Width * mine.Height || theirs.Samples.Length < theirs.Width * theirs.Height)
                return $"component {at} holds {mine.Samples.Length} vs {theirs.Samples.Length} samples for {mine.Width}x{mine.Height}";

            var differing = 0;
            var furthest = 0L;
            var count = mine.Width * mine.Height;
            for (var sample = 0; sample < count; sample++)
            {
                var gap = Math.Abs((long)mine.Samples[sample] - theirs.Samples[sample]);
                if (gap == 0) continue;
                differing++;
                furthest = Math.Max(furthest, gap);
            }

            if (differing > 0) return $"component {at}: {differing} of {count} samples differ, by up to {furthest}";
        }

        return "identical";
    }

    /// <summary>A managed decode's components, copied out to set beside another decoder's.</summary>
    public static JpxPlanes Of(Jpeg2000Image image) =>
        new(image.Components.Select(c => new JpxPlane(c.Width, c.Height, c.Precision, c.Signed, c.Samples.ToArray())).ToList());
}

/// <summary>What a JPEG 2000 image's bytes and dictionary say, beside the image itself.</summary>
internal static class JpxStreams
{
    /// <summary>A bare codestream (SOC marker first) rather than a JP2 file of boxes.</summary>
    public static bool IsRawCodestream(byte[] data) => data.Length >= 4 && data[0] == 0xFF && data[1] == 0x4F && data[2] == 0xFF && data[3] == 0x51;

    extension(PdfImageStream stream)
    {
        /// <summary>The PDF declares its own colour space, so the palette in the JPX data is not applied, as <c>PdfJpxFilter</c> decides it.</summary>
        public bool IgnorePalette => stream.Dictionary.ContainsKey(NameToken.ColorSpace);

        /// <summary>A bare codestream (SOC marker first) rather than a JP2 file of boxes.</summary>
        public bool IsRawCodestream => IsRawCodestream(stream.Data);
    }
}
