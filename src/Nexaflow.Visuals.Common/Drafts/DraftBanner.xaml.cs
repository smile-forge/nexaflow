using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Drafts;

/// <summary>
/// The strip a viewer shows over a draft — a file made for the user and not yet saved — with its Save as. Its DataContext
/// is the page's <see cref="DraftSaving"/>; with none, it is not shown.
/// </summary>
public partial class DraftBanner : UserControl
{
    public DraftBanner() => InitializeComponent();
}
