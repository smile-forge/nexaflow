using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>
/// One text surface's zoom: a percentage over the shell's <see cref="TextTypography.BaseFontSize"/>,
/// plus the commands and presets every viewer offers for it.
/// <para>
/// A view-model rather than a control, because zoom is page state — it belongs to the tab, survives the
/// view being unloaded and re-realised, and is what a test or an AI tool would set. The chrome that
/// drives it is <see cref="Controls.ZoomChip"/>; a host binds one to the other.
/// </para>
/// <para>
/// <see cref="FontSize"/> is the only number a surface should read: it already folds in the shell
/// setting, and it re-raises when either input moves — so a viewer that binds it tracks a live change to
/// the Options text size without knowing the setting exists. Nothing needs disposing: the registration
/// on the shell setting is weak and goes when this object does.
/// </para>
/// </summary>
public sealed partial class TextZoom : ObservableObject
{
    /// <summary>Smallest zoom the commands and clamping allow.</summary>
    public const int MinPercent = 50;

    /// <summary>Largest zoom the commands and clamping allow.</summary>
    public const int MaxPercent = 400;

    /// <summary>One step of Ctrl+wheel, Ctrl+plus or Ctrl+minus.</summary>
    public const int Step = 10;

    // Held in a field because TextTypography keeps only a weak reference to it: this field is what
    // decides the registration lives exactly as long as this zoom does.
    private readonly Action _onBaseFontSizeChanged;

    public TextZoom()
    {
        _onBaseFontSizeChanged = () => OnPropertyChanged(nameof(FontSize));
        TextTypography.AddChangeListener(_onBaseFontSizeChanged);
    }

    /// <summary>Zoom percentage. Assignments outside [<see cref="MinPercent"/>, <see cref="MaxPercent"/>]
    /// are clamped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontSize))]
    private int _percent = 100;

    partial void OnPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, MinPercent, MaxPercent);
        if (clamped != value) Percent = clamped; // re-enters with the clamped value; hosts read that
    }

    /// <summary>The percentages the chip offers as one click. Not the full range — the ends are reached
    /// by stepping, and a menu long enough to hold them would stop being a quick pick.</summary>
    public IReadOnlyList<int> Presets { get; } = [80, 90, 100, 110, 120, 130];

    /// <summary>Point size this surface should render text at: the shell's base size at this zoom.</summary>
    public double FontSize => TextTypography.BaseFontSize * Percent / 100.0;

    [RelayCommand] private void ZoomIn()    => Percent = Math.Min(MaxPercent, Percent + Step);
    [RelayCommand] private void ZoomOut()   => Percent = Math.Max(MinPercent, Percent - Step);
    [RelayCommand] private void ResetZoom() => Percent = 100;

    /// <summary>
    /// Applies a Ctrl+wheel gesture, returning true when it was one (the caller then marks the event
    /// handled). Lives here so the viewers share the gesture rather than each deciding what counts as a
    /// zoom scroll — and so a plain scroll still reaches the content.
    /// </summary>
    public bool TryWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control || e.Delta == 0)
            return false;

        if (e.Delta > 0) ZoomIn();
        else             ZoomOut();
        return true;
    }
}
