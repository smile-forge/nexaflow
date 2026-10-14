using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// Whether the reply card — the whole of what the AI bar's line holds, rendered, just above the bar — is shown. It shows while
/// the pointer is over the reply or the card, with a moment's grace to cross from one to the other; while pinned by a click on
/// the reply, until closed, Esc or a click elsewhere; and while a plan waits to be approved. Only ever with something to show,
/// and never modal: the page beside it stays in use.
/// </summary>
public sealed partial class ReplyCard : ObservableObject
{
    /// <summary>How long the card stays once the pointer leaves, so it can cross from the reply to the card.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _leaving;

    public ReplyCard() : this(Grace) { }

    public ReplyCard(TimeSpan grace)
    {
        _leaving = new DispatcherTimer { Interval = grace };
        _leaving.Tick += (_, _) =>
        {
            _leaving.Stop();
            IsHovered = false;
        };
    }

    /// <summary>Whether there is anything for the card to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private bool _hasContent;

    /// <summary>Whether the pointer is over the reply or the card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private bool _isHovered;

    /// <summary>Whether a click has pinned it open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private bool _isPinned;

    /// <summary>Whether what it shows must be seen to be answered: a plan waiting to be approved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private bool _isRequired;

    /// <summary>Whether the card is shown.</summary>
    public bool IsOpen => HasContent && (IsHovered || IsPinned || IsRequired);

    /// <summary>The pointer came over the reply or the card, or left it.</summary>
    public void PointerOver(bool over)
    {
        if (over)
        {
            _leaving.Stop();
            IsHovered = true;
        }
        else _leaving.Start();
    }

    /// <summary>A click on the reply: pins the card open, or unpins it.</summary>
    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    /// <summary>Puts the card away: closed, Esc, a click elsewhere, or the reply gone — a plan shown unasked too, its Allow and
    /// Deny staying in the bar's line.</summary>
    [RelayCommand]
    public void Close()
    {
        _leaving.Stop();
        IsPinned   = false;
        IsHovered  = false;
        IsRequired = false;
    }
}
