using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The shared chrome of a tab-modal form. What a host leans on is small and easy to break without noticing: a
/// closed card is collapsed ΓÇö it takes no layout and blocks nothing behind it ΓÇö an open one shows, and Escape
/// runs the form's own cancel command, whichever one it currently has.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("vcommon-modal-card")]
public class ModalCardTests
{
    /// <summary>A card with its default style (Generic.xaml) applied ΓÇö that is where the IsOpen trigger lives.</summary>
    private static ModalCard Styled(Action<ModalCard>? setup = null)
    {
        var card = new ModalCard();
        card.BeginInit();
        setup?.Invoke(card);
        card.EndInit();

        // No window, so force a layout pass ΓÇö the template applies on measure.
        card.Measure(new Size(400, 300));
        card.Arrange(new Rect(0, 0, 400, 300));
        return card;
    }

    private static KeyBinding Escape(ModalCard card) =>
        card.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.Escape);

    private sealed class Counter : ICommand
    {
        public int Runs;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Runs++;
    }

    [TestMethod]
    public void AClosedCard_IsCollapsed_AndOpeningItShowsIt() => UiThread.Run(() =>
    {
        var card = Styled();
        Assert.AreEqual(Visibility.Collapsed, card.Visibility, "closed by default: no layout, nothing blocked");

        card.IsOpen = true;
        Assert.AreEqual(Visibility.Visible, card.Visibility);

        card.IsOpen = false;
        Assert.AreEqual(Visibility.Collapsed, card.Visibility);
    });

    [TestMethod]
    public void Escape_RunsTheFormsCancelCommand() => UiThread.Run(() =>
    {
        var cancel = new Counter();
        var card = Styled(c => c.CancelCommand = cancel);

        Escape(card).Command.Execute(null);

        Assert.AreEqual(1, cancel.Runs);
    });

    [TestMethod]
    public void ReplacingTheCancelCommand_RebindsEscape() => UiThread.Run(() =>
    {
        var first = new Counter();
        var second = new Counter();
        var card = Styled(c => c.CancelCommand = first);

        card.CancelCommand = second;
        Escape(card).Command.Execute(null);

        Assert.AreEqual(0, first.Runs, "a replaced command must not keep answering Escape");
        Assert.AreEqual(1, second.Runs);
    });
}
