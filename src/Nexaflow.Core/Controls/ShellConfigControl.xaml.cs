using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

public partial class ShellConfigControl : UserControl, ICustomConfigApply
{
    private ShellConfigViewModel? _vm;

    public ShellConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ShellConfig cfg)
            {
                _vm = new ShellConfigViewModel(cfg);
                RootPanel.DataContext = _vm;
            }
        };
    }

    public void Apply()
    {
        if (DataContext is ShellConfig cfg && _vm is not null)
        {
            cfg.Theme                      = _vm.Theme;
            cfg.Language                   = _vm.Language;
            cfg.PrestartAtLogin            = _vm.PrestartAtLogin;
            cfg.DisableAnimationsOnBattery = _vm.DisableAnimationsOnBattery;

            // Sync the HKCU Run entry to the toggle (takes effect next login).
            LoginAutoStartService.Set(_vm.PrestartAtLogin);

            // Unlike the theme, this one needs no restart: the guard re-evaluates and every live
            // ThemedRegion drops or rebuilds its scene on the spot.
            BatteryAnimationGuard.SetDisableOnBattery(_vm.DisableAnimationsOnBattery);
        }
    }
}

internal sealed partial class ShellConfigViewModel : ObservableObject
{
    private static readonly string PackBase =
        $"pack://application:,,,/{typeof(ShellConfigViewModel).Assembly.GetName().Name};component/Themes/Colors.";

    private static readonly string[] SwatchKeys =
        ["BgColor", "SurfaceColor", "Surface2Color", "AccentColor", "Accent2Color", "TextColor"];

    public IReadOnlyList<string> ThemeOptions    { get; } = Enum.GetNames<ThemeOption>();
    public IReadOnlyList<string> LanguageOptions { get; } = Enum.GetNames<LanguageOption>();

    [ObservableProperty] private string _selectedTheme;
    [ObservableProperty] private string _selectedLanguage;
    [ObservableProperty] private bool _prestartAtLogin;
    [ObservableProperty] private bool _disableAnimationsOnBattery;
    [ObservableProperty] private IReadOnlyList<Color> _swatches = [];

    public ThemeOption    Theme    => Enum.Parse<ThemeOption>(SelectedTheme);
    public LanguageOption Language => Enum.Parse<LanguageOption>(SelectedLanguage);

    public ShellConfigViewModel(ShellConfig cfg)
    {
        _selectedTheme              = cfg.Theme.ToString();
        _selectedLanguage           = cfg.Language.ToString();
        _prestartAtLogin            = cfg.PrestartAtLogin;
        _disableAnimationsOnBattery = cfg.DisableAnimationsOnBattery;
        LoadSwatches(cfg.Theme);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (Enum.TryParse<ThemeOption>(value, out var t))
            LoadSwatches(t);
    }

    private void LoadSwatches(ThemeOption theme)
    {
        try
        {
            var dict = new ResourceDictionary { Source = new Uri($"{PackBase}{theme}.xaml") };
            Swatches = SwatchKeys
                .Where(k => dict.Contains(k) && dict[k] is Color)
                .Select(k => (Color)dict[k]!)
                .ToList();
        }
        catch
        {
            Swatches = [];
        }
    }
}
