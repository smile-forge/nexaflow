using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>An open tab: the session's page, and the view it is drawn with.</summary>
internal sealed partial class ShellTab(Page page, Control view, Action<ShellTab> close) : ObservableObject
{
    public Page Page { get; } = page;

    public Control View { get; } = view;

    /// <summary>Closes the tab, through the session.</summary>
    [RelayCommand]
    private void Close() => close(this);
}

/// <summary>One of the workspace's ribbon buttons, opening its page.</summary>
internal sealed partial class RibbonButton(string label, string icon, Action open)
{
    public string Label { get; } = label;

    /// <summary>The button's glyph where its icon is an emoji; the icon sets need the WPF icon catalogue to draw.</summary>
    public string Icon { get; } = icon;

    /// <summary>The ribbon's way to the page, also offered to the quick-open list.</summary>
    public Action Open { get; } = open;

    [RelayCommand]
    private void Press() => Open();
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
