using System;
using System.Globalization;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// What <c>{{…}}</c> comes to, and how one is written.
///
/// <para>
/// Which stretches of a run are bindings is settled while the run is read, by
/// <see cref="Nexaflow.Markdown.Ast.ContentWords"/>; what each one stands for is worked out when the content is
/// laid. So the tree keeps <c>{{Total}}</c> exactly as it was written, the block prints as its author wrote it, and
/// the value is only what is drawn over the top — which is also what lets a press reveal the binding and put the
/// caret in it.
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
    /// The path a binding names, where <paramref name="text"/> is nothing but one binding — <c>{{Dependencies}}</c> on a line
    /// of its own — and null otherwise.
    /// </summary>
    public static string? Path(string? text)
    {
        var said = text?.Trim();
        if (said is not { Length: > 4 } || !said.StartsWith(Opens, StringComparison.Ordinal) || !said.EndsWith(Shuts, StringComparison.Ordinal))
            return null;

        var path = said[Opens.Length..^Shuts.Length].Trim();
        return path.Length > 0 && !path.Contains(Opens, StringComparison.Ordinal) && !path.Contains(Shuts, StringComparison.Ordinal) ? path : null;
    }

    /// <summary>What one path says, drawn the way a reader expects to see it.</summary>
    public static string Says(string path, IDataContext data) =>
        path.Length > 0 && data.TryGet(path, out var value) ? Said(value) : string.Empty;

    /// <summary>
    /// A value as words: the invariant form for a number or a date, so a diagram reads the same wherever it is
    /// drawn, and whatever the object says for itself otherwise.
    /// </summary>
    internal static string Said(object? value) => value switch
    {
        null => string.Empty,
        string said => said,
        bool flag => flag ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
