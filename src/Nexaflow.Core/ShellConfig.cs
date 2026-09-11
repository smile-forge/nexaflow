using Nexaflow.Core.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Core;

public enum ThemeOption { Dark, Light, Flowers, Sunny, Ocean, Nature, Sandstone, Gothic, Arctic }

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

    /// <summary>
    /// The UI language, as the culture code of an installed language pack (<c>Languages\Nexaflow.Language.&lt;code&gt;.dll</c>):
    /// "en", "fr", "pt-BR". A code with no pack runs in English — as does the "English" an earlier build stored here,
    /// back when this was an enum. Applied like the theme: saving a change restarts the window.
    /// </summary>
    [ConfigDisplayName("Language")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// Point size text content is read at across the shell's text surfaces — the text/code editor, the
    /// markdown document, the hex grid. A viewer's own zoom multiplies this rather than replacing it, so
    /// changing it moves them all together and each keeps its own proportions (markdown's heading ladder,
    /// the hex grid's column metrics). Applies live, no restart.
    /// </summary>
    [ConfigDisplayName("Text size")]
    public double TextFontSize { get; set; } = TextTypography.DefaultBaseFontSize;

    /// <summary>
    /// When true, Nexaflow registers itself to launch at login with <c>--prestart</c> as a windowless
    /// daemon so windows open instantly. Synced to the HKCU Run key on save; takes effect next login.
    /// </summary>
    [ConfigDisplayName("Start with Windows")]
    public bool PrestartAtLogin { get; set; }


    /// <summary>
    /// When true, a theme's animated backdrop (the <c>Scene.{Region}</c> layer a <c>ThemedRegion</c>
    /// renders behind the shell) is not rendered while the machine is running on battery. Applies
    /// live - unplugging drops the scene, plugging back in restores it - and is a no-op on a machine
    /// with no battery. Themes that ship no scene (Dark/Light) are unaffected either way.
    /// </summary>
    [ConfigDisplayName("Disable background animations on battery")]
    public bool DisableAnimationsOnBattery { get; set; } = true;

    /// <summary>
    /// Assembly version of the last run that reached the main window. Compared to the current version
    /// to decide whether to show the "What's New" wizard step after an update. Not user-editable
    /// (ShellConfig renders via <c>ShellConfigControl</c>, so this property never appears in the grid);
    /// written by App on launch. Null until the first run completes.
    /// </summary>
    public string? LastRunVersion { get; set; }
}
