using System;
using System.Globalization;
using System.Text;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// Words with <c>{{…}}</c> written in them, and what they come to.
///
/// <para>
/// Read at the moment the words are set, never at parse time: the tree keeps <c>{{Total}}</c> exactly as it was
/// written, so the block still prints as its author wrote it, and the value is what is drawn over the top. That is
/// also what lets a press reveal the binding and put the caret in it — the characters are still there.
/// </para>
/// </summary>
public static class BoundText
{
    /// <summary>What opens a binding.</summary>
    public const string Opens = "{{";

    /// <summary>What shuts one.</summary>
    public const string Shuts = "}}";

    /// <summary>Whether anything in it is bound — the cheap question asked of every run of words a diagram draws.</summary>
    public static bool Binds(string? text) => text is { Length: > 3 } && text.Contains(Opens, StringComparison.Ordinal);

    /// <summary>
    /// What it says with each binding replaced by what it comes to.
    ///
    /// <para>
    /// With no data at all it says exactly what was written, so a document nobody has bound to shows its bindings as
    /// the text they are. With data, a path that comes to nothing leaves a gap: a diagram that falls over on a typo
    /// is worse than one with a hole in it, and the hole is what sends somebody to look at the path.
    /// </para>
    /// </summary>
    public static string Bound(string text, IDataContext? data)
    {
        if (data is null || !Binds(text)) return text;

        var said = new StringBuilder(text.Length);
        var at = 0;

        while (at < text.Length)
        {
            var opens = text.IndexOf(Opens, at, StringComparison.Ordinal);
            if (opens < 0) break;

            var shuts = text.IndexOf(Shuts, opens + Opens.Length, StringComparison.Ordinal);
            if (shuts < 0) break;

            said.Append(text, at, opens - at);
            said.Append(Says(text[(opens + Opens.Length)..shuts].Trim(), data));
            at = shuts + Shuts.Length;
        }

        return at == 0 ? text : said.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>What one path says, drawn the way a reader expects to see it.</summary>
    private static string Says(string path, IDataContext data) =>
        path.Length > 0 && data.TryGet(path, out var value) ? Said(value) : string.Empty;

    /// <summary>
    /// A value as words: the invariant form for a number or a date, so a diagram reads the same wherever it is
    /// drawn, and whatever the object says for itself otherwise.
    /// </summary>
    private static string Said(object? value) => value switch
    {
        null => string.Empty,
        string said => said,
        bool flag => flag ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
