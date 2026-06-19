using System;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>A formatting action requested from the <see cref="MarkdownEditToolbar"/>.</summary>
public enum MarkdownEditAction
{
    Cut, Copy, Paste,
    H1, H2, H3,
    Bold, Italic, Strikethrough, InlineCode,
    Quote, CodeBlock,
}

/// <summary>
/// The icons-only mini-toolbar shown (in a popup) on right-click while a block is being edited in
/// <see cref="InlineMarkdownEditor"/>. Two rows: clipboard + headings, then inline + block formatting.
/// Raises <see cref="ActionInvoked"/>; the editor applies it to the active block.
/// </summary>
public partial class MarkdownEditToolbar : UserControl
{
    public event Action<MarkdownEditAction>? ActionInvoked;

    public MarkdownEditToolbar() => InitializeComponent();

    /// <summary>Enables/disables the clipboard buttons for the current selection / clipboard contents.</summary>
    public void SetClipboardState(bool canCut, bool canPaste)
    {
        CutBtn.IsEnabled   = canCut;
        PasteBtn.IsEnabled = canPaste;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse<MarkdownEditAction>(tag, out var action))
            ActionInvoked?.Invoke(action);
    }
}
