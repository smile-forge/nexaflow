namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// One row of the details panel. A <see cref="IsHeader"/> row renders as a group heading
/// (its <see cref="Label"/> is the group name, <see cref="Value"/> empty); otherwise it's a
/// label → value pair. The view-model emits these already filtered (empty values dropped) so the
/// XAML stays a single flat, grouped <c>ItemsControl</c>.
/// </summary>
public sealed record DetailRow(string Label, string Value, bool IsHeader = false)
{
    public static DetailRow Header(string title) => new(title, string.Empty, IsHeader: true);
}
