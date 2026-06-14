using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

public enum ThemeOption    { Dark, Light, Sunny, Ocean, Nature, Sandstone, Gothic }
public enum LanguageOption { English }

/// <summary>
/// Shell-level configuration. Registered manually in App.xaml.cs since it is not
/// part of any feature assembly scanned by FeatureManager.
/// </summary>
[CustomControl(typeof(ShellConfigControl))]
[Nexaflow.Features.Common.MandatorySetup]   // shown in the setup wizard (theme/language/start-with-windows)
public sealed class ShellConfig : IFeatureConfig
{
    public string ConfigName   => "shell";
    public string FriendlyName => "Shell";

    [ConfigDisplayName("Theme")]
    public ThemeOption Theme { get; set; } = ThemeOption.Dark;

    [ConfigDisplayName("Language")]
    public LanguageOption Language { get; set; } = LanguageOption.English;

    /// <summary>
    /// When true, Nexaflow registers itself to launch at login with <c>--prestart</c> as a windowless
    /// daemon so windows open instantly. Synced to the HKCU Run key on save; takes effect next login.
    /// </summary>
    [ConfigDisplayName("Start with Windows")]
    public bool PrestartAtLogin { get; set; }

    /// <summary>
    /// Assembly version of the last run that reached the main window. Compared to the current version
    /// to decide whether to show the "What's New" wizard step after an update. Not user-editable
    /// (ShellConfig renders via <c>ShellConfigControl</c>, so this property never appears in the grid);
    /// written by App on launch. Null until the first run completes.
    /// </summary>
    public string? LastRunVersion { get; set; }
}
