using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core;

public enum ThemeOption    { Dark }
public enum LanguageOption { English }

/// <summary>
/// Shell-level configuration. Registered manually in App.xaml.cs since it is not
/// part of any feature assembly scanned by FeatureManager.
/// </summary>
public sealed class ShellConfig : IFeatureConfig
{
    public string ConfigName   => "shell";
    public string FriendlyName => "Shell";

    [ConfigDisplayName("Theme")]
    public ThemeOption Theme { get; set; } = ThemeOption.Dark;

    [ConfigDisplayName("Language")]
    public LanguageOption Language { get; set; } = LanguageOption.English;

    [ConfigDisplayName("AI Provider (Basic)")]
    [ListSource(typeof(LlmProviderRegistry), nameof(LlmProviderRegistry.GetProviderNames))]
    public string BasicAiProvider { get; set; } = string.Empty;

    [ConfigDisplayName("AI Provider (Conversation)")]
    [ListSource(typeof(LlmProviderRegistry), nameof(LlmProviderRegistry.GetProviderNames))]
    public string ConversationAiProvider { get; set; } = string.Empty;
}
