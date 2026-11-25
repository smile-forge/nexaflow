using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.Localization;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Localization;
using System.ComponentModel;
using Nexaflow.Core.Services.Logging;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The Shell section of Options and the setup wizard. Bound to a <see cref="ShellConfigViewModel"/> the settings panel makes
/// (<see cref="ViewModels.ConfigPageModels"/>), which carries the session's language for the picker; it saves into the config
/// that view-model was made over.
/// </summary>
public partial class ShellConfigControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private ShellConfigViewModel? _vm;

    public ShellConfigControl()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ShellConfigViewModel vm)
            {
                if (_vm is not null) _vm.PropertyChanged -= OnChoicesChanged;
                _vm = vm;
                _vm.PropertyChanged += OnChoicesChanged;
                HasChangesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    // ── IConfigChangeTracker: enables the panel's Apply button once a setting moves off the one saved ──
    public bool HasChanges => _vm?.HasChanges ?? false;
    public event EventHandler? HasChangesChanged;

    private void OnChoicesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellConfigViewModel.HasChanges))
            HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply()
    {
        if (_vm is not null)
        {
            var cfg = _vm.Config;
            cfg.Theme                      = _vm.Theme;
            cfg.Language                   = _vm.Language;
            cfg.TextFontSize               = _vm.TextFontSize;
            cfg.PrestartAtLogin            = _vm.PrestartAtLogin;
            cfg.KeepWindowReady            = _vm.KeepWindowReady;
            cfg.DormantWorkspaceMinutes    = _vm.DormantWorkspaceMinutes;
            cfg.DisableAnimationsOnBattery = _vm.DisableAnimationsOnBattery;
            cfg.EnhancedAiProtection       = _vm.EnhancedAiProtection;
            cfg.SafetyLogging              = _vm.SafetyLogging;

            // Sync the HKCU Run entry to the toggle (takes effect next login).
            LoginAutoStartService.Set(_vm.PrestartAtLogin);

            // Unlike the theme, this one needs no restart: the guard re-evaluates and every live
            // ThemedRegion drops or rebuilds its scene on the spot.
            BatteryAnimationGuard.SetDisableOnBattery(_vm.DisableAnimationsOnBattery);

            // Also live: every text surface binds the size through TextTypography, so open tabs re-lay out
            // on the spot rather than waiting to be reopened.
            TextTypography.BaseFontSize = _vm.TextFontSize;

            // Live as well: the session's log opens or closes now; the AI host's follows the next time the host starts.
            _vm.ApplySafetyLogging();

            _vm.AcceptAsSaved();
        }
    }
}

internal sealed partial class ShellConfigViewModel : ObservableObject
{
    private static readonly string PackBase =
        $"pack://application:,,,/{typeof(ShellConfigViewModel).Assembly.GetName().Name};component/Themes/Colors.";

    private static readonly string[] SwatchKeys =
        ["BgColor", "SurfaceColor", "Surface2Color", "AccentColor", "Accent2Color", "TextColor"];

    /// <summary>Every theme: the invariant name the config stores, beside the name it is shown by.</summary>
    public IReadOnlyList<ThemeChoice> ThemeOptions { get; } =
        [.. Enum.GetValues<ThemeOption>().Select(t => new ThemeChoice(t.ToString(), ThemeChoice.Shown(t)))];

    /// <summary>The installed language packs, read from their file names (nothing is loaded to list them).</summary>
    public IReadOnlyList<LanguageInfo> LanguageOptions { get; }

    /// <summary>The config the page shows, which <see cref="ShellConfigControl.Apply"/> saves the choices into.</summary>
    public ShellConfig Config { get; }

    /// <summary>Sizes offered for <see cref="TextFontSize"/>. A fixed list rather than a spinner over the
    /// whole clamped range: every step here is one a reader would actually pick, and the large end is
    /// coarse because a point either way stops mattering once the text is that big.</summary>
    public IReadOnlyList<double> TextSizeOptions { get; } = [9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 24, 28, 32];

    /// <summary>How long a workspace left may stay ready, offered for <see cref="DormantWorkspaceMinutes"/>: steps a person moving
    /// between tasks would pick, from closing it at once to an hour.</summary>
    public IReadOnlyList<DormancyOption> DormancyOptions { get; } =
    [
        new(0, Str.Get("Shell.Options.Shell.DormantEndAtOnce")),
        new(1, Str.Get("Shell.Options.Shell.DormantOneMinute")),
        .. new[] { 5, 15, 30, 60 }.Select(minutes => new DormancyOption(minutes, Str.Format("Shell.Options.Shell.DormantMinutesFormat", minutes))),
    ];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private string _selectedTheme;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private string _selectedLanguage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private double _textFontSize;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private bool _prestartAtLogin;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private bool _keepWindowReady;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private int _dormantWorkspaceMinutes;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private bool _disableAnimationsOnBattery;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private bool _enhancedAiProtection;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private bool _safetyLogging;
    [ObservableProperty] private IReadOnlyList<Color> _swatches = [];

    public ThemeOption Theme    => Enum.Parse<ThemeOption>(SelectedTheme);
    public string      Language => SelectedLanguage;

    /// <summary>Every choice the page saves, as one value, so the page and its saved state compare whole.</summary>
    private readonly record struct Choices(
        string Theme, string Language, double TextFontSize, bool PrestartAtLogin, bool KeepWindowReady, int DormantWorkspaceMinutes,
        bool DisableAnimationsOnBattery, bool EnhancedAiProtection, bool SafetyLogging);

    private Choices Current => new(SelectedTheme, SelectedLanguage, TextFontSize, PrestartAtLogin, KeepWindowReady, DormantWorkspaceMinutes,
                                   DisableAnimationsOnBattery, EnhancedAiProtection, SafetyLogging);

    // What the page last saved, or opened on — taken after the language and size were snapped onto offered values,
    // since a config the page had to normalise is not an edit the user made.
    private Choices _saved;

    /// <summary>True while any choice differs from the one last saved.</summary>
    public bool HasChanges => Current != _saved;

    /// <summary>Takes the choices on screen as the saved ones, once they have been written to the config.</summary>
    public void AcceptAsSaved()
    {
        _saved = Current;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>The session's log, opened or closed to match <see cref="SafetyLogging"/> when the page is applied.</summary>
    private readonly SessionLogProvider _sessionLog;

    /// <summary>Opens or closes the session's log to match the choice — live; the AI host's log follows the next time the host
    /// starts.</summary>
    public void ApplySafetyLogging() => _sessionLog.Apply(SafetyLogging);

    /// <param name="cfg">The config the page shows and saves into.</param>
    /// <param name="language">The session's UI language, whose installed packs the picker offers.</param>
    /// <param name="sessionLog">The session's log, which the safety-logging choice opens and closes as it is applied.</param>
    public ShellConfigViewModel(ShellConfig cfg, LanguageManager language, SessionLogProvider sessionLog)
    {
        Config                      = cfg;
        _sessionLog                 = sessionLog;
        LanguageOptions             = language.Available;
        _selectedTheme              = cfg.Theme.ToString();
        // As a code the list offers, so the combo shows a selection: the "English" an older build stored, or a
        // language whose pack has since been removed, both open on English — which is what the app is running in.
        var configured              = LanguageManager.Normalize(cfg.Language);
        _selectedLanguage           = LanguageOptions.Any(l => l.Code == configured) ? configured : LanguageManager.FallbackCode;
        // Snapped onto an offered size so the combo shows a selection: a config carrying 13.5 (or anything
        // off-list, including a value a future build offered) would otherwise open blank and, once touched,
        // silently become whichever size the user happened to click past.
        _textFontSize               = NearestOfferedSize(cfg.TextFontSize);
        _prestartAtLogin            = cfg.PrestartAtLogin;
        _keepWindowReady            = cfg.KeepWindowReady;
        // Snapped onto an offered time, as the size is, so the combo shows a selection.
        _dormantWorkspaceMinutes    = DormancyOptions.MinBy(o => Math.Abs(o.Minutes - cfg.DormantWorkspaceMinutes))!.Minutes;
        _disableAnimationsOnBattery = cfg.DisableAnimationsOnBattery;
        _enhancedAiProtection       = cfg.EnhancedAiProtection;
        _safetyLogging              = cfg.SafetyLogging;
        _saved                      = Current;
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

    /// <summary>The offered size closest to <paramref name="size"/>, after clamping it into the range
    /// <see cref="TextTypography"/> will accept.</summary>
    private double NearestOfferedSize(double size)
    {
        var clamped = TextTypography.Clamp(size);
        var nearest = TextSizeOptions[0];
        foreach (var option in TextSizeOptions)
            if (Math.Abs(option - clamped) < Math.Abs(nearest - clamped))
                nearest = option;
        return nearest;
    }
}

/// <summary>A time a workspace left may stay ready, as the Shell page offers it. Read aloud — by a screen reader, by UI
/// Automation — as its label.</summary>
public sealed record DormancyOption(int Minutes, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One theme the picker offers: <paramref name="Value"/> is what is saved, <paramref name="Display"/> what is read.</summary>
public sealed record ThemeChoice(string Value, string Display)
{
    /// <summary>A theme's name in the window's language.</summary>
    public static string Shown(ThemeOption theme) => theme switch
    {
    ThemeOption.Dark => Str.Get("Shell.Theme.Dark"),
    ThemeOption.Light => Str.Get("Shell.Theme.Light"),
    ThemeOption.Ocean => Str.Get("Shell.Theme.Ocean"),
    ThemeOption.Sunny => Str.Get("Shell.Theme.Sunny"),
    ThemeOption.Arctic => Str.Get("Shell.Theme.Arctic"),
    ThemeOption.Desert => Str.Get("Shell.Theme.Desert"),
    ThemeOption.Gothic => Str.Get("Shell.Theme.Gothic"),
    ThemeOption.Nature => Str.Get("Shell.Theme.Nature"),
    ThemeOption.Flowers => Str.Get("Shell.Theme.Flowers"),
    ThemeOption.Sandstone => Str.Get("Shell.Theme.Sandstone"),
        _ => theme.ToString(),
    };
}
