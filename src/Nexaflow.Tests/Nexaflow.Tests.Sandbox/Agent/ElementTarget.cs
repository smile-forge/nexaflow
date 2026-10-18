using System.Drawing;
using System.Text.RegularExpressions;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// What a verb points at: an element by AutomationId (the default, as the journeys find one), by name (<c>name:Open</c>),
/// or a point on the sandbox's screen (<c>640,400</c>).
/// </summary>
internal sealed partial record ElementTarget(string? AutomationId, string? Name, Point? At)
{
    private const string NamePrefix = "name:";

    public static ElementTarget Parse(string text)
    {
        if (PointPattern().Match(text) is { Success: true } point)
            return new ElementTarget(null, null, new Point(int.Parse(point.Groups[1].Value), int.Parse(point.Groups[2].Value)));
        if (text.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            return new ElementTarget(null, text[NamePrefix.Length..], null);
        return new ElementTarget(text, null, null);
    }

    public override string ToString() =>
        At is { } at ? $"{at.X},{at.Y}" : Name is not null ? $"the element named '{Name}'" : $"'{AutomationId}'";

    [GeneratedRegex(@"^\s*(-?\d+)\s*,\s*(-?\d+)\s*$")]
    private static partial Regex PointPattern();
}
