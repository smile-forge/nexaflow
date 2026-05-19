using Nexaflow.Features.Common;

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
}
