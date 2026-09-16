using System.Globalization;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>How Mermaid writes a number, wherever a diagram or its front matter writes one.</summary>
public static class MermaidNumber
{
    /// <summary>What <paramref name="text"/> says as a number, or null where it is none.</summary>
    public static double? Read(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>A number of pixels, written with <c>px</c> after it or without, or null where it is not one.</summary>
    public static double? Pixels(string? text) =>
        text is null ? null : Read(text.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? text[..^2].TrimEnd() : text);

    /// <summary>
    /// What is wrong with a number that has to be greater than nought — for <see cref="MermaidLine.Amount"/> — where anything
    /// is: that it is not a number, or <paramref name="nought"/>, the diagram's words for what one of nought or less is.
    /// </summary>
    public static Func<string, string?> Positive(string nought) =>
        text => Read(text) is not { } number ? $"'{text}' is not a number." : number > 0 ? null : nought;
}
