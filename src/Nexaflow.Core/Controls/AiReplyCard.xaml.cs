using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Controls;

/// <summary>The reply card above the AI bar: it stays while the pointer is over it, as it does over the reply.</summary>
public partial class AiReplyCard : UserControl
{
    public AiReplyCard() => InitializeComponent();

    private void Card_MouseEnter(object sender, MouseEventArgs e) => (DataContext as ShellAIResponseHandler)?.Card.PointerOver(true);

    private void Card_MouseLeave(object sender, MouseEventArgs e) => (DataContext as ShellAIResponseHandler)?.Card.PointerOver(false);
}
