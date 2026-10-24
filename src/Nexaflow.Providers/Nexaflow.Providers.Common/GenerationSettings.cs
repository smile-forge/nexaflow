using System.Globalization;

namespace Nexaflow.Providers.Common;

/// <summary>
/// A picture's or a clip's proportions, width to height, in the terms a model names them ("16:9", "21:9"). Two are equal
/// when their proportions are, whatever their terms; what shape it is follows from it, and the pixels a model makes it
/// at are the model's own.
/// </summary>
public readonly record struct MediaAspect
{
    public MediaAspect(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height), "An aspect is two positive numbers.");
        Width  = width;
        Height = height;
    }

    /// <summary>The proportions of a frame <paramref name="width"/> by <paramref name="height"/> pixels, in lowest terms.</summary>
    public static MediaAspect Of(int width, int height)
    {
        var common = Divisor(width, height);
        return new MediaAspect(width / common, height / common);
    }

    public int Width { get; }

    public int Height { get; }

    public MediaShape Shape => Width == Height ? MediaShape.Square : Width > Height ? MediaShape.Landscape : MediaShape.Portrait;

    public bool Equals(MediaAspect other) => (long)Width * other.Height == (long)other.Width * Height;

    public override int GetHashCode()
    {
        if (Width == 0 || Height == 0) return 0;
        var common = Divisor(Width, Height);
        return HashCode.Combine(Width / common, Height / common);
    }

    public override string ToString() => $"{Width}:{Height}";

    /// <summary>Reads "16:9", or null where <paramref name="text"/> is not two positive numbers either side of a colon.</summary>
    public static MediaAspect? Parse(string? text)
    {
        if (text?.Split(':') is not [var w, var h]) return null;
        return int.TryParse(w.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width > 0
               && int.TryParse(h.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height > 0
            ? new MediaAspect(width, height)
            : null;
    }

    private static int Divisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

/// <summary>How much care an image model takes over a picture — slower and dearer the more — in the words it is asked in.</summary>
public enum ImageDetail
{
    Low,
    Medium,
    High,
}

/// <summary>
/// What an image model lets a picture be asked for with besides its prompt — offered alike to the person making one and
/// to the assistant, and held to by <see cref="Refusal"/> before the model is asked.
/// </summary>
/// <param name="Aspects">The proportions it draws, the first of each shape the one that shape is drawn at
/// (<see cref="AspectFor"/>); a square one among them.</param>
public sealed record ImageSettings(IReadOnlyList<MediaAspect> Aspects)
{
    /// <summary>The sizes it draws at, as it names them ("1K", "2K"), smallest first; empty where it draws at one.</summary>
    public IReadOnlyList<string> Resolutions { get; init; } = [];

    /// <summary>The care it can be asked to take, least first; empty where it is not asked.</summary>
    public IReadOnlyList<ImageDetail> Details { get; init; } = [];

    /// <summary>Whether a seed it is given draws the same picture again from the same prompt.</summary>
    public bool Seeds { get; init; }

    /// <summary>Whether it is told what to keep out of the picture.</summary>
    public bool NegativePrompts { get; init; }

    /// <summary>Whether it draws on a transparent background when asked.</summary>
    public bool TransparentBackgrounds { get; init; }

    /// <summary>The proportions <paramref name="shape"/> is drawn at: the first of that shape it draws.</summary>
    public MediaAspect AspectFor(MediaShape shape) => Aspects.FirstOrDefault(a => a.Shape == shape, Aspects[0]);

    /// <summary>Why <paramref name="request"/> asks for something this model does not do, or null where it asks for nothing it does not.</summary>
    public string? Refusal(ImageGenerationRequest request)
    {
        if (!Aspects.Contains(request.Aspect))
            return $"It draws at {string.Join(", ", Aspects)}, not {request.Aspect}.";
        if (request.Resolution is { } resolution && !Resolutions.Contains(resolution, StringComparer.OrdinalIgnoreCase))
            return Resolutions.Count == 0 ? "It draws at one size." : $"It draws at {string.Join(", ", Resolutions)}, not {resolution}.";
        if (request.Detail is { } detail && !Details.Contains(detail))
            return Details.Count == 0 ? "It takes no level of detail." : $"Its levels of detail are {string.Join(", ", Details)}, not {detail}.";
        if (request.Seed is { } seed && (!Seeds || seed < 0)) return Seeds ? "A seed is 0 or more." : "It takes no seed.";
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt) && !NegativePrompts) return "It is told nothing to keep out.";
        if (request.TransparentBackground && !TransparentBackgrounds) return "It draws no transparent background.";
        return null;
    }
}

/// <summary>A size a video model makes clips at, as it names it ("1080p"), and the lengths it makes at that size where it
/// makes only some there.</summary>
public sealed record VideoResolution(string Name)
{
    /// <summary>The only lengths, in seconds, a clip is made in at this size; empty where it is made in any.</summary>
    public IReadOnlyList<int> OnlyLengths { get; init; } = [];

    public bool Makes(int seconds) => OnlyLengths.Count == 0 || OnlyLengths.Contains(seconds);
}

/// <summary>
/// What a video model lets a clip be asked for with besides its prompt, its length and the picture it starts from —
/// offered alike to the person making one and to the assistant, and held to by <see cref="Refusal"/>.
/// </summary>
/// <param name="Aspects">The proportions it makes clips in, the first of each shape the one that shape is made in
/// (<see cref="AspectFor"/>); none square, and a landscape one among them.</param>
public sealed record VideoSettings(IReadOnlyList<MediaAspect> Aspects)
{
    /// <summary>The sizes it makes clips at, smallest first; empty where it makes them at one.</summary>
    public IReadOnlyList<VideoResolution> Resolutions { get; init; } = [];

    /// <summary>Whether a seed it is given makes much the same clip again from the same prompt.</summary>
    public bool Seeds { get; init; }

    /// <summary>Whether it is told what to keep out of the clip.</summary>
    public bool NegativePrompts { get; init; }

    /// <summary>The proportions <paramref name="shape"/> is made in: the first of that shape it makes, a square one made landscape.</summary>
    public MediaAspect AspectFor(MediaShape shape) =>
        Aspects.FirstOrDefault(a => a.Shape == (shape == MediaShape.Square ? MediaShape.Landscape : shape), Aspects[0]);

    /// <summary>The size named <paramref name="name"/>, or null where it makes none of that name.</summary>
    public VideoResolution? Resolution(string? name) => Resolutions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Why <paramref name="request"/> asks for something this model does not do, or null where it asks for nothing it does not.</summary>
    public string? Refusal(VideoGenerationRequest request)
    {
        if (!Aspects.Contains(request.Aspect))
            return $"It makes clips in {string.Join(", ", Aspects)}, not {request.Aspect}.";
        if (request.Resolution is { } name)
        {
            if (Resolution(name) is not { } resolution)
                return Resolutions.Count == 0 ? "It makes clips at one size." : $"It makes clips at {string.Join(", ", Resolutions.Select(r => r.Name))}, not {name}.";
            if (!resolution.Makes(request.Seconds))
                return $"At {resolution.Name} it makes clips of {string.Join(", ", resolution.OnlyLengths)} seconds only.";
        }
        if (request.Seed is { } seed && (!Seeds || seed < 0)) return Seeds ? "A seed is 0 or more." : "It takes no seed.";
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt) && !NegativePrompts) return "It is told nothing to keep out.";
        return null;
    }
}
