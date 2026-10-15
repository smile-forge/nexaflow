using System.Windows;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// How much of the model's context a conversation fills: <paramref name="Tokens"/> of its <paramref name="Window"/>, as
/// the model said after its last step — drawn as a line under the AI bar's line, as long as the share used.
/// </summary>
public sealed record ContextUse(int Tokens, int Window)
{
    /// <summary>From this share on, the line is drawn in the warning colour: the conversation will soon be condensed.</summary>
    internal const double Nearly = 0.8;

    public double Fill => Window > 0 ? Math.Clamp((double)Tokens / Window, 0, 1) : 0;

    public bool IsNearlyFull => Fill >= Nearly;

    /// <summary>The used share and the rest, as the line's two columns.</summary>
    public GridLength Used => new(Fill, GridUnitType.Star);

    public GridLength Left => new(1 - Fill, GridUnitType.Star);

    /// <summary>"34,210 of 262,144 tokens of the model's context (13%)".</summary>
    public string Text => Str.Format("Shell.AiBar.ContextFormat", Tokens.ToString("N0"), Window.ToString("N0"), (int)Math.Round(Fill * 100));
}
