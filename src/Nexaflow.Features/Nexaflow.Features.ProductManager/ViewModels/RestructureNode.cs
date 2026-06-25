using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>A node in the restructure overlay's drag-drop tree. The synthetic top entry (the product itself)
/// has <see cref="IsProductRoot"/> set — it's the drop target for making a node top-level and can't be dragged.</summary>
public sealed class RestructureNode(string id, string title, Brush? statusBrush)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public Brush? StatusBrush { get; } = statusBrush;
    public bool IsProductRoot => Id == ProductViewModel.ProductRootSentinel;
    public ObservableCollection<RestructureNode> Children { get; } = [];
}
