using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>An open tab: the session's page, the view it is drawn with, and how the tab strip shows it.</summary>
internal sealed partial class ShellTab : ObservableObject
{
    /// <summary>A title longer than this is cut, its last character an ellipsis, as the WPF tab strip cuts it.</summary>
    private const int CaptionLength = 15;

    private readonly Action<ShellTab> _activate;
    private readonly Action<ShellTab> _close;

    public ShellTab(Page page, Control view, Action<ShellTab> activate, Action<ShellTab> close)
    {
        Page      = page;
        View      = view;
        _activate = activate;
        _close    = close;
        page.PropertyChanged += OnPageChanged;
    }

    public Page Page { get; }

    public Control View { get; }

    /// <summary>The title as the strip shows it.</summary>
    public string Caption => Page.Title.Length > CaptionLength ? Page.Title[..(CaptionLength - 1)] + "…" : Page.Title;

    /// <summary>The full title, as a tooltip, only where the caption cut it.</summary>
    public string? FullTitle => Page.Title.Length > CaptionLength ? Page.Title : null;

    [ObservableProperty] private bool _isActive;

    /// <summary>The page lets go of the tab as the tab closes.</summary>
    public void Detach() => Page.PropertyChanged -= OnPageChanged;

    [RelayCommand]
    private void Activate() => _activate(this);

    [RelayCommand]
    private void Close() => _close(this);

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Page.Title)) return;
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(FullTitle));
    }
}

/// <summary>One of the workspace's ribbon buttons, opening its page.</summary>
internal sealed partial class RibbonButton
{
    /// <summary>A label longer than this is cut, its last character an ellipsis, as the WPF ribbon cuts it.</summary>
    private const int CaptionLength = 14;

    /// <summary>The bundled Fluent icon font: the WPF app's own file, linked into this app's resources.</summary>
    private static readonly FontFamily Fluent = new($"avares://Nexaflow.AvaloniaShell/Assets/Fonts#{FluentGlyphs.Family}");

    public RibbonButton(string label, IconRef icon, Action open)
    {
        Label = label;
        Open  = open;
        (Glyph, GlyphFont) = icon.IsFluent ? (FluentGlyphs.Of(icon) ?? "▢", Fluent) : (icon.Value, MonoGlyph.Emoji);
    }

    public string Label { get; }

    public string Caption => Label.Length > CaptionLength ? Label[..(CaptionLength - 1)] + "…" : Label;

    /// <summary>The text that draws the icon, in <see cref="GlyphFont"/>: an emoji, or a Fluent glyph.</summary>
    public string Glyph { get; }

    public FontFamily GlyphFont { get; }

    /// <summary>The ribbon's way to the page, also offered to the quick-open list.</summary>
    public Action Open { get; }

    [RelayCommand]
    private void Press() => Open();
}

/// <summary>The line between two groups of ribbon buttons.</summary>
internal sealed class RibbonSeparator;

/// <summary>One step of the active page's breadcrumb trail.</summary>
internal sealed partial class Crumb(BreadcrumbSegment segment, bool isLast)
{
    public string Label { get; } = segment.Label;

    /// <summary>The last step is where the page is: drawn in the text colour, the steps before it muted.</summary>
    public bool IsLast { get; } = isLast;

    public bool HasSeparator => !IsLast;

    [RelayCommand]
    private void Navigate() => segment.Navigate?.Invoke();
}

/// <summary>A question the window asks over its content — a confirmation, or a prompt for a value — until it is answered.</summary>
internal sealed partial class ShellPrompt(string title, string message, bool asksValue, string value, string confirmLabel, string cancelLabel,
                                          Action<string> confirmed, Action? cancelled, Action<ShellPrompt> done) : ObservableObject
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    /// <summary>A value is typed in, not only a yes or no.</summary>
    public bool AsksValue { get; } = asksValue;

    [ObservableProperty] private string _value = value;

    public string ConfirmLabel { get; } = confirmLabel;

    public string CancelLabel { get; } = cancelLabel;

    [RelayCommand]
    private void Confirm()
    {
        done(this);
        confirmed(Value);
    }

    [RelayCommand]
    private void Cancel()
    {
        done(this);
        cancelled?.Invoke();
    }
}
