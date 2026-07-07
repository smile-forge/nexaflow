using Nexaflow.Features.Common;

namespace Nexaflow.Core;

/// <summary>How freely the AI may use tools that look at windows outside the current page.</summary>
public enum WindowAccessOption
{
    /// <summary>Always allow.</summary>
    Yes,
    /// <summary>Never allow — the gated tools aren't offered to the AI at all.</summary>
    No,
    /// <summary>Ask the user (confirmation overlay) each time before the tool runs.</summary>
    Prompt,
}

/// <summary>
/// Security/privacy settings. Global (one instance, shared by all workspaces). Registered in
/// App.xaml.cs alongside <see cref="ShellConfig"/>; surfaced as a "Security" section in Options.
/// </summary>
public sealed class SecurityConfig : IFeatureConfig
{
    public string ConfigName   => "security";
    public string FriendlyName => "Security";

    /// <summary>
    /// Whether the AI may enumerate / view windows outside Nexaflow (the <c>GetOpenWindows</c> tool,
    /// and later whole-screen capture). Defaults to <see cref="WindowAccessOption.Prompt"/>.
    /// </summary>
    [ConfigDisplayName("Allow AI to view other windows")]
    public WindowAccessOption AllowWindowAccess { get; set; } = WindowAccessOption.Prompt;
}
