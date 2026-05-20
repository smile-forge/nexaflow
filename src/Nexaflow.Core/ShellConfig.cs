using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

public enum ThemeOption    { Dark, Light, Sunny, Ocean, Nature, Sandstone }
public enum LanguageOption { English }

/// <summary>
/// Shell-level configuration. Registered manually in App.xaml.cs since it is not
/// part of any feature assembly scanned by FeatureManager.
/// </summary>
[CustomControl(typeof(ShellConfigControl))]
public sealed class ShellConfig : IFeatureConfig
{
    public string ConfigName   => "shell";
    public string FriendlyName => "Shell";

    [ConfigDisplayName("Theme")]
    public ThemeOption Theme { get; set; } = ThemeOption.Dark;

    [ConfigDisplayName("Language")]
    public LanguageOption Language { get; set; } = LanguageOption.English;
}
