using System.Windows.Controls;
using System.Windows;

namespace Nexaflow.Visuals.Common.Drafts;

/// <summary>
/// The strip a viewer shows over a draft — a file made for the user and not yet saved — with its Save as. Its DataContext
/// is the page's <see cref="DraftSaving"/>; with none, it is not shown.
/// </summary>
public partial class DraftBanner : UserControl
{
    public DraftBanner() => InitializeComponent();

    /// <summary>Whether the strip carries its own Save as: false on a page whose Save already asks where a draft goes.</summary>
    public static readonly DependencyProperty OffersSaveAsProperty =
        DependencyProperty.Register(nameof(OffersSaveAs), typeof(bool), typeof(DraftBanner), new PropertyMetadata(true));

    public bool OffersSaveAs
    {
        get => (bool)GetValue(OffersSaveAsProperty);
        set => SetValue(OffersSaveAsProperty, value);
    }
}
