using Nexaflow.Localization;

namespace Nexaflow.Assistant;

/// <summary>
/// How much of the model's context a conversation fills: <paramref name="Tokens"/> of its <paramref name="Window"/>, as
/// the model said after its last step — drawn as a line under the AI bar's line, as long as the share used.
/// </summary>
public sealed record ContextUse(int Tokens, int Window)
{
    /// <summary>From this share on, the line is drawn in the warning colour: the conversation will soon be condensed.</summary>
    internal const double Nearly = 0.8;

    /// <summary>The share used, 0 to 1.</summary>
    public double Fill => Window > 0 ? Math.Clamp((double)Tokens / Window, 0, 1) : 0;

    /// <summary>The share still free.</summary>
    public double Rest => 1 - Fill;

    public bool IsNearlyFull => Fill >= Nearly;

    /// <summary>"34,210 of 262,144 tokens of the model's context (13%)".</summary>
    public string Text => Str.Format("Shell.AiBar.ContextFormat", Tokens.ToString("N0"), Window.ToString("N0"), (int)Math.Round(Fill * 100));
}
