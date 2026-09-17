using System.Globalization;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// Turns the tree <see cref="WordCloudParser"/> read into the cloud it describes.
///
/// <para>
/// It draws the line a 2D code's reader draws, in the same place and for the same reason. A
/// <em>structural</em> fault — a line that is not a pair, a setting that does not exist, a width that is not a
/// number — means the block cannot be understood at all, and the reader gets the source back with the reason.
/// A weight that will not read is a different thing entirely: the block is perfectly well formed, that word is
/// the part being edited, and the other forty words are still a cloud. It keeps its line and loses its place
/// in the picture.
/// </para>
/// </summary>
public static class WordCloudReader
{
    /// <summary>The cloud the tree describes, or false with the reason it is not a cloud at all.</summary>
    public static bool TryRead(ContentPart root, out WordCloudChart? chart, out string? error)
    {
        chart = null;
        error = null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<WordCloudEntry>();

        foreach (var part in root.SelfAndDescendants())
        {
            if (part.Trouble is { } trouble)
            {
                error = trouble;
                return false;
            }

            if (part.Kind == WordCloudKinds.Setting)
                fields[WordCloudSetting.Plain(part.Part(Roles.Name)?.Text)] =
                    part.Part(WordCloudRoles.Value)?.Text ?? string.Empty;

            else if (part.Kind == WordCloudKinds.Entry)
                words.Add(Entry(part));
        }

        if (!TrySettings(fields, out var settings, out error)) return false;

        chart = new WordCloudChart(settings!, words);
        return true;
    }

    /// <summary>One word-and-weight line, and what is wrong with it where anything is.</summary>
    private static WordCloudEntry Entry(ContentPart entry)
    {
        var word = entry.Part(WordCloudRoles.Word)!;
        var inner = Inner(word);
        var number = entry.Part(WordCloudRoles.Weight);

        if (inner.Text.Trim().Length == 0)
            return new WordCloudEntry(string.Empty, 0, inner, number, "A word of a cloud cannot be blank.");

        if (number is null)
            return new WordCloudEntry(inner.Text, 0, inner, null,
                $"{inner.Text} has no weight. Write how much it counts for after the colon.");

        if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            return new WordCloudEntry(inner.Text, 0, inner, number, WordCloudSetting.Is(inner.Text)
                ? $"`{inner.Text}` is a setting, but the settings are written above the words — below them it is read as a word, and `{number.Text}` is not a weight."
                : $"`{number.Text}` is not a weight, and `{inner.Text}` is not a setting — so this line is neither.");

        if (weight <= 0 || double.IsNaN(weight) || double.IsInfinity(weight))
            return new WordCloudEntry(inner.Text, 0, inner, number,
                $"`{number.Text}` is not a weight. A word counts for more than nothing or it is not in the cloud.");

        return new WordCloudEntry(inner.Text, weight, inner, number);
    }

    /// <summary>
    /// The word itself, without the quotes it may have been written in — which is what is drawn, pressed and
    /// typed into.
    /// </summary>
    private static ContentPart Inner(ContentPart word) =>
        word.Node.IsLeaf
            ? word
            : word.Children.FirstOrDefault(child => child.Kind == WordCloudKinds.Word && child.Node.IsLeaf) ?? word;

    // ── Settings ───────────────────────────────────────────────────────────

    private static bool TrySettings(IReadOnlyDictionary<string, string> fields,
                                    out WordCloudSettings? settings, out string? error)
    {
        settings = null;

        // Every key here is a setting already: the parser only makes a setting of a key it knows, and a key
        // it does not know is a word. So there is no unknown-setting fault to report, and a key that was meant
        // to be one is a word whose weight will not read — which is what says so.
        var it = WordCloudSettings.Default;

        if (!Number(fields, "width", 0, 0, WordCloudSettings.MaxSide, out var width, out error)) return false;
        if (!Number(fields, "height", 0, 0, WordCloudSettings.MaxSide, out var height, out error)) return false;
        if (!Number(fields, "ellipticity", it.Ellipticity, WordCloudSettings.MinEllipticity,
                    WordCloudSettings.MaxEllipticity, out var ellipticity, out error)) return false;
        if (!Number(fields, "minSize", it.MinSize, WordCloudSettings.SmallestSize,
                    WordCloudSettings.LargestSize, out var minSize, out error)) return false;
        if (!Number(fields, "maxSize", it.MaxSize, WordCloudSettings.SmallestSize,
                    WordCloudSettings.LargestSize, out var maxSize, out error)) return false;
        if (!Number(fields, "gridSize", it.GridSize, WordCloudSettings.MinGridSize,
                    WordCloudSettings.MaxGridSize, out var gridSize, out error)) return false;
        if (!Number(fields, "gap", it.Gap, 0, WordCloudSettings.MaxGap, out var gap, out error)) return false;
        if (!Number(fields, "rotate", it.Rotate, 0, 1, out var rotate, out error)) return false;
        if (!Number(fields, "minRotation", it.MinRotation, -WordCloudSettings.RotationLimit,
                    WordCloudSettings.RotationLimit, out var minRotation, out error)) return false;
        if (!Number(fields, "maxRotation", it.MaxRotation, -WordCloudSettings.RotationLimit,
                    WordCloudSettings.RotationLimit, out var maxRotation, out error)) return false;
        if (!Number(fields, "rotationSteps", it.RotationSteps, 0, WordCloudSettings.MaxRotationSteps,
                    out var steps, out error)) return false;
        if (!Number(fields, "seed", it.Seed, 0, int.MaxValue, out var seed, out error)) return false;

        if (!Yes(fields, "bold", it.Bold, out var bold, out error)) return false;
        if (!Yes(fields, "shuffle", it.Shuffle, out var shuffle, out error)) return false;
        if (!Yes(fields, "fit", it.Fit, out var fit, out error)) return false;

        if (!Shape(fields, out var shape, out error)) return false;
        if (!Scale(fields, out var scale, out error)) return false;

        if (minSize > maxSize)
        {
            error = $"`minSize: {Written(minSize)}` is larger than `maxSize: {Written(maxSize)}`.";
            return false;
        }

        if (minRotation > maxRotation)
        {
            error = $"`minRotation: {Written(minRotation)}` is past `maxRotation: {Written(maxRotation)}`.";
            return false;
        }

        settings = it with
        {
            Width = width,
            Height = height,
            Shape = shape,
            Letters = Text(fields, "letters"),
            Mask = Text(fields, "mask"),
            Ellipticity = ellipticity,
            Font = Text(fields, "font") ?? it.Font,
            Bold = bold,
            MinSize = minSize,
            MaxSize = maxSize,
            Scale = scale,
            GridSize = gridSize,
            Gap = gap,
            Rotate = rotate,
            MinRotation = minRotation,
            MaxRotation = maxRotation,
            RotationSteps = (int)steps,
            Colour = Text(fields, "colour") ?? Text(fields, "color") ?? it.Colour,
            Background = Text(fields, "background"),
            Seed = (int)seed,
            Shuffle = shuffle,
            Fit = fit,
        };

        return true;
    }

    private static string? Text(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(WordCloudSetting.Plain(key), out var value) && value.Trim().Length > 0
            ? value.Trim()
            : null;

    private static bool Number(IReadOnlyDictionary<string, string> fields, string key, double fallback,
                               double min, double max, out double result, out string? error)
    {
        result = fallback;
        error = null;

        if (Text(fields, key) is not { } value) return true;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            error = $"`{key}: {value}` is not a number.";
            return false;
        }

        if (parsed < min || parsed > max)
        {
            error = $"`{key}: {value}` is outside the usable range {Written(min)}–{Written(max)}.";
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool Yes(IReadOnlyDictionary<string, string> fields, string key, bool fallback,
                            out bool result, out string? error)
    {
        result = fallback;
        error = null;

        if (Text(fields, key) is not { } value) return true;

        switch (value.ToLowerInvariant())
        {
            case "true" or "yes" or "on" or "1": result = true; return true;
            case "false" or "no" or "off" or "0": result = false; return true;
            default:
                error = $"`{key}: {value}` is not true or false.";
                return false;
        }
    }

    private static bool Shape(IReadOnlyDictionary<string, string> fields, out WordCloudShape shape,
                              out string? error)
    {
        shape = WordCloudSettings.Default.Shape;
        error = null;

        if (Text(fields, "shape") is not { } value) return true;

        if (WordCloudShapes.Of(value) is not { } known)
        {
            error = $"`shape: {value}` is not a shape. It takes {WordCloudShapes.Names}.";
            return false;
        }

        shape = known;
        return true;
    }

    private static bool Scale(IReadOnlyDictionary<string, string> fields, out WordCloudScale scale,
                              out string? error)
    {
        scale = WordCloudSettings.Default.Scale;
        error = null;

        if (Text(fields, "scale") is not { } value) return true;

        switch (value.ToLowerInvariant())
        {
            case "linear": scale = WordCloudScale.Linear; return true;
            case "sqrt" or "root": scale = WordCloudScale.Root; return true;
            case "log" or "logarithmic": scale = WordCloudScale.Logarithmic; return true;
            default:
                error = $"`scale: {value}` is not a scale. It takes linear, sqrt, log.";
                return false;
        }
    }

    /// <summary>A number as a diagnostic says it, so a range reads as 4–32 rather than as 4–32.0000.</summary>
    private static string Written(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
