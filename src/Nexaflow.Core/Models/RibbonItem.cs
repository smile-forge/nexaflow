using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace Nexaflow.Core.Models;

public enum RibbonItemKind { Button, HalfGroup, Separator }

/// <summary>
/// Carries a tab drag-and-drop pin request, optionally with an insert index.
/// </summary>
/// <param name="Tab">The tab being pinned.</param>
/// <param name="InsertIndex">
/// The desired insertion position in <c>RibbonItems</c>, or <c>-1</c> to append.
/// </param>
public record TabPinRequest(TabEntry Tab, int InsertIndex = -1);

/// <summary>
/// Represents one item in the customisable ribbon.
/// </summary>
public partial class RibbonItem : ObservableObject
{
    public RibbonItemKind Kind { get; set; } = RibbonItemKind.Button;

    [ObservableProperty] private string _label  = string.Empty;
    [ObservableProperty] private string _icon   = string.Empty;
    [ObservableProperty] private bool   _isActive;

    /// <summary>When true the button renders compact (icon + label side-by-side) instead of full-height stacked.</summary>
    [ObservableProperty] private bool _isHalf;

    /// <summary>Optional hex colour override for the icon/label (e.g. "#FF4F8EF7"). Null = default theme colour.</summary>
    public string? AccentColor { get; set; }

    /// <summary>For Kind==HalfGroup: the two stacked sub-buttons.</summary>
    public List<RibbonItem>? HalfItems { get; set; }

    /// <summary>Command executed when the ribbon button is clicked.</summary>
    public ICommand? Command { get; set; }

    /// <summary>Runtime-only tab factory, re-attached after deserialisation by <see cref="ShellViewModel"/>.</summary>
    public Func<TabEntry>? TabFactory { get; set; }

    /// <summary>
    /// Identifies the page type this button opens (see <see cref="PageKinds"/>).
    /// Persisted to ribbon.json; used by <c>ShellViewModel.ReattachTabFactory</c>
    /// to restore the correct runtime factory after a restart.
    /// </summary>
    public string? PageKind { get; set; }

    /// <summary>
    /// Page-type-specific initialisation parameters (e.g. {"Path":"C:\\Users\\..."}
    /// for a FileSystem page). Persisted alongside <see cref="PageKind"/>.
    /// </summary>
    public Dictionary<string, string>? PageParams { get; set; }
}
