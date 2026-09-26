using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Settings;
using static Nexaflow.Markdown.Settings.SettingValues;

namespace Nexaflow.Markdown.WordCloud.Stages;

/// <summary>
/// Says what the settings written above a cloud's words come to (<see cref="WordCloudBlockNode"/>): its size and shape, its type,
/// the scale its weights are sized on, how its words turn, and what they are drawn in — or, where a setting is given something it
/// cannot take, marks that setting's value with why.
///
/// <para>
/// Every key read here is a setting already: the parser only makes a setting of a key it knows, and a key it does not know is a
/// word. So there is no unknown-setting fault to report, and a key that was meant to be one is a word whose weight will not read —
/// which is what says so (<see cref="ResolveWords"/>).
/// </para>
/// </summary>
public sealed class ResolveCloud : IAstStage
{
    public string Name => "wordcloud:settings";

    public ContentNode Run(ContentNode tree)
    {
        var fields = SettingKeys.Written(tree, WordCloudKinds.Setting, WordCloudRoles.Value);

        if (!TrySettings(fields, out var settings, out var error, out var key)
            || !TryColours(fields, settings!, out var colours, out error, out key))
            return SettingKeys.Marked(tree, WordCloudKinds.Setting, WordCloudRoles.Value, key, error!);

        return new WordCloudBlockNode(tree, settings!, colours!);
    }

    private static bool TrySettings(IReadOnlyDictionary<string, string> fields,
                                    out WordCloudSettings? settings, out string? error, out string? key)
    {
        settings = null;
        key = null;

        var it = WordCloudSettings.Default;

        if (!Number(fields, "width", 0, 0, WordCloudSettings.MaxSide, out var width, out error)) return Stop("width", out key);
        if (!Number(fields, "height", 0, 0, WordCloudSettings.MaxSide, out var height, out error)) return Stop("height", out key);
        if (!Number(fields, "ellipticity", it.Ellipticity, WordCloudSettings.MinEllipticity,
                    WordCloudSettings.MaxEllipticity, out var ellipticity, out error)) return Stop("ellipticity", out key);
        if (!Number(fields, "minSize", it.MinSize, WordCloudSettings.SmallestSize,
                    WordCloudSettings.LargestSize, out var minSize, out error)) return Stop("minSize", out key);
        if (!Number(fields, "maxSize", it.MaxSize, WordCloudSettings.SmallestSize,
                    WordCloudSettings.LargestSize, out var maxSize, out error)) return Stop("maxSize", out key);
        if (!Number(fields, "gridSize", it.GridSize, WordCloudSettings.MinGridSize,
                    WordCloudSettings.MaxGridSize, out var gridSize, out error)) return Stop("gridSize", out key);
        if (!Number(fields, "gap", it.Gap, 0, WordCloudSettings.MaxGap, out var gap, out error)) return Stop("gap", out key);
        if (!Number(fields, "rotate", it.Rotate, 0, 1, out var rotate, out error)) return Stop("rotate", out key);
        if (!Number(fields, "minRotation", it.MinRotation, -WordCloudSettings.RotationLimit,
                    WordCloudSettings.RotationLimit, out var minRotation, out error)) return Stop("minRotation", out key);
        if (!Number(fields, "maxRotation", it.MaxRotation, -WordCloudSettings.RotationLimit,
                    WordCloudSettings.RotationLimit, out var maxRotation, out error)) return Stop("maxRotation", out key);
        if (!Number(fields, "rotationSteps", it.RotationSteps, 0, WordCloudSettings.MaxRotationSteps,
                    out var steps, out error)) return Stop("rotationSteps", out key);
        if (!Number(fields, "seed", it.Seed, 0, int.MaxValue, out var seed, out error)) return Stop("seed", out key);

        if (!Yes(fields, "bold", it.Bold, out var bold, out error)) return Stop("bold", out key);
        if (!Yes(fields, "shuffle", it.Shuffle, out var shuffle, out error)) return Stop("shuffle", out key);
        if (!Yes(fields, "fit", it.Fit, out var fit, out error)) return Stop("fit", out key);

        if (!Shape(fields, out var shape, out error)) return Stop("shape", out key);
        if (!Scale(fields, out var scale, out error)) return Stop("scale", out key);

        if (minSize > maxSize)
        {
            error = $"`minSize: {Written(minSize)}` is larger than `maxSize: {Written(maxSize)}`.";
            return Stop("minSize", out key);
        }

        if (minRotation > maxRotation)
        {
            error = $"`minRotation: {Written(minRotation)}` is past `maxRotation: {Written(maxRotation)}`.";
            return Stop("minRotation", out key);
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

    /// <summary>
    /// What the words are drawn in, and on. A colour written that is not one is a fault in the block rather than in a word, so it
    /// stops the whole thing being a cloud.
    /// </summary>
    private static bool TryColours(IReadOnlyDictionary<string, string> fields, WordCloudSettings settings,
                                   out WordCloudColours? colours, out string? error, out string? key)
    {
        colours = null;
        error = null;
        key = Text(fields, "colour") is not null ? "colour" : "color";

        HexColor? background = null;
        var asked = settings.Colour.Trim();
        var colouring = asked.ToLowerInvariant() switch
        {
            WordCloudSettings.Themed => WordCloudColouring.Themed,
            WordCloudSettings.RandomDark => WordCloudColouring.RandomDark,
            WordCloudSettings.RandomLight => WordCloudColouring.RandomLight,
            _ => WordCloudColouring.Written,
        };

        var written = new List<HexColor>();

        if (colouring == WordCloudColouring.Written)
        {
            foreach (var one in asked.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!HexColor.TryParse(one, out var colour))
                {
                    error = $"`color: {one}` is not a colour. Use {WordCloudSettings.Themed}, "
                          + $"{WordCloudSettings.RandomDark}, {WordCloudSettings.RandomLight}, "
                          + "or #RGB / #RRGGBB / #AARRGGBB colours to take in turn.";
                    return false;
                }

                written.Add(colour);
            }

            if (written.Count == 0)
            {
                error = "A `color:` line with no colour on it.";
                return false;
            }
        }

        if (settings.Background is { Length: > 0 } ground)
        {
            if (!HexColor.TryParse(ground, out var colour))
            {
                error = $"`background: {ground}` is not a hex colour. Use #RGB, #RRGGBB or #AARRGGBB.";
                key = "background";
                return false;
            }

            background = colour;
        }

        key = null;
        colours = new WordCloudColours(colouring, written, background);
        return true;
    }

    /// <summary>Says which setting stopped the block reading, and that it stopped.</summary>
    private static bool Stop(string setting, out string? key)
    {
        key = setting;
        return false;
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

    private static bool Shape(IReadOnlyDictionary<string, string> fields, out WordCloudShape shape, out string? error)
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

    private static bool Scale(IReadOnlyDictionary<string, string> fields, out WordCloudScale scale, out string? error)
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
}
