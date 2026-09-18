using System.Globalization;

namespace Nexaflow.Markdown.Settings;

/// <summary>
/// What a setting was set to, read as what its key takes — and, where it cannot be, the reason a reader
/// would want to see.
///
/// <para>
/// A value nobody can read stops the block being what it says it is, because it is a question about the
/// whole picture: a width that is not a number leaves nothing to draw at all. That is a different thing
/// from a cell of the data that will not read, which costs one mark and no more, and it is why these
/// hand back a reason rather than a fallback.
/// </para>
/// </summary>
public static class SettingValues
{
    /// <summary>What a key was set to, or null where it was not written or was left blank.</summary>
    public static string? Text(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Trim().Length > 0 ? value.Trim() : null;

    /// <summary>
    /// The number a value says, or null where it says none. Strict on purpose: blank is not nought, and
    /// neither <c>NaN</c> nor <c>Infinity</c> is a place anything can be drawn at.
    /// </summary>
    public static double? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var read)
               && !double.IsNaN(read) && !double.IsInfinity(read)
            ? read
            : null;
    }

    /// <summary>A number within what its key takes, or false with the reason.</summary>
    public static bool Number(IReadOnlyDictionary<string, string> fields, string key, double fallback,
                              double min, double max, out double result, out string? error)
    {
        result = fallback;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        if (Read(written) is not { } read)
        {
            error = $"`{key}: {written}` is not a number.";
            return false;
        }

        if (read < min || read > max)
        {
            error = $"`{key}: {written}` is outside {Written(min)} to {Written(max)}.";
            return false;
        }

        result = read;
        return true;
    }

    /// <summary>
    /// A flag that may simply not have been written, which is not the same as being false — so a key
    /// whose default is true can tell "nothing said" from "said no".
    /// </summary>
    public static bool Maybe(IReadOnlyDictionary<string, string> fields, string key,
                             out bool? result, out string? error)
    {
        result = null;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        if (Yes.Contains(written, StringComparer.OrdinalIgnoreCase)) result = true;
        else if (No.Contains(written, StringComparer.OrdinalIgnoreCase)) result = false;
        else
        {
            error = $"`{key}: {written}` is not true or false.";
            return false;
        }

        return true;
    }

    /// <summary>The same, taking what it was given where nothing was written.</summary>
    public static bool Flag(IReadOnlyDictionary<string, string> fields, string key, bool fallback,
                            out bool result, out string? error)
    {
        if (!Maybe(fields, key, out var said, out error))
        {
            result = fallback;
            return false;
        }

        result = said ?? fallback;
        return true;
    }

    private static readonly string[] Yes = ["true", "yes", "on", "1"];
    private static readonly string[] No = ["false", "no", "off", "0"];

    /// <summary>
    /// One of the few things a key takes, named as it is written — and, where it is none of them, a
    /// reason naming every one it could have been.
    /// </summary>
    public static bool Choice<TChoice>(IReadOnlyDictionary<string, string> fields, string key,
                                       TChoice fallback, out TChoice result, out string? error)
        where TChoice : struct, Enum
    {
        result = fallback;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        if (!Enum.TryParse(written.Replace("-", string.Empty), ignoreCase: true, out result)
            || !Enum.IsDefined(result))
        {
            result = fallback;
            error = $"`{key}: {written}` is not one of {string.Join(", ", Named<TChoice>())}.";
            return false;
        }

        return true;
    }

    /// <summary>What a choice's values are called, lower case, which is how they are written.</summary>
    public static IEnumerable<string> Named<TChoice>() where TChoice : struct, Enum =>
        Enum.GetNames<TChoice>().Select(name => name.ToLowerInvariant());

    /// <summary>A value written as several, separated by space or commas alike.</summary>
    public static IReadOnlyList<string> Split(string written) =>
        written.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A number as a reason writes one: as many decimals as it needs and no more.</summary>
    public static string Written(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
