namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// The throw every choice in a cloud is made from — which angle a word takes, and which of the places at one
/// radius is tried first.
///
/// <para>
/// A cloud is laid out again from nothing on every keystroke, on every change of theme and on every change of
/// column width, so a throw that came from the clock would give a different cloud each time and the picture
/// would boil while somebody typed into it. This one comes from the block: the same source always gives the
/// same cloud, and a reader adding a word watches the rest hold still.
/// </para>
/// <para>
/// Its own generator rather than <see cref="System.Random"/>, because the same block has to give the same
/// picture on every machine and every runtime, and a shared library is free to change how it draws. This is
/// xorshift32, which is four lines and will not.
/// </para>
/// </summary>
public sealed class WordCloudRandom(int seed)
{
    // Odd, and so never nought — which is the one state xorshift cannot leave.
    private uint _state = ((uint)seed * 2654435761u) | 1u;

    /// <summary>The next throw, nought to one.</summary>
    public double Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;

        return _state / 4294967296.0;
    }

    /// <summary>A whole number below <paramref name="below"/>.</summary>
    public int Below(int below) => below <= 0 ? 0 : (int)(Next() * below) % below;

    /// <summary>
    /// How far the next word is turned, in degrees. Most are level — only
    /// <see cref="WordCloudSettings.Rotate"/> of them are turned at all — and a turned one takes one of
    /// <see cref="WordCloudSettings.RotationSteps"/> angles between the two the settings allow, or any angle
    /// between them where no number of steps was asked for.
    /// </summary>
    public double Angle(WordCloudSettings settings)
    {
        if (settings.Rotate <= 0 || Next() >= settings.Rotate) return 0;

        var range = settings.MaxRotation - settings.MinRotation;
        if (range <= 0) return settings.MinRotation;

        if (settings.RotationSteps <= 1) return settings.MinRotation + Next() * range;

        return settings.MinRotation + Below(settings.RotationSteps) * range / (settings.RotationSteps - 1);
    }

    /// <summary>Shuffles in place, so the places at one radius are tried in no particular order.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var at = items.Count - 1; at > 0; at--)
        {
            var other = Below(at + 1);
            var kept = items[at];
            items[at] = items[other];
            items[other] = kept;
        }
    }
}
