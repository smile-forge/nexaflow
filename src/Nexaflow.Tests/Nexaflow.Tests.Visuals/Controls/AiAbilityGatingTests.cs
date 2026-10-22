using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// A control behind an AI ability gate: disabled while the gate is closed, its tooltip the gate's saying why, and its own back
/// once the gate opens — bound as it was, so a tooltip that follows its page goes on following it.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("opt-ai-ability-grid")]
public partial class AiAbilityGatingTests
{
    /// <summary>A gate as a page's is: whether it is open, and why not.</summary>
    private sealed partial class Gate : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private string? _disabledTip = "No model";
    }

    /// <summary>What a page says on its button.</summary>
    private sealed partial class Page : ObservableObject
    {
        [ObservableProperty] private string _tip = "Translate it";
    }

    [TestMethod]
    public void AClosedGate_SaysWhy_AndOpenAgain_TheControlsOwnTipIsBoundAsItWas() => UiThread.Run(() =>
    {
        var gate = new Gate();
        var page = new Page();
        var button = new Button { DataContext = page };
        button.SetBinding(System.Windows.FrameworkElement.ToolTipProperty, new Binding(nameof(Page.Tip)));
        AiAbilityGating.SetGate(button, gate);

        Assert.AreEqual("Translate it", button.ToolTip);

        gate.IsEnabled = false;
        Assert.IsFalse(button.IsEnabled);
        Assert.AreEqual("No model", button.ToolTip, "closed, the gate says why");

        gate.IsEnabled = true;
        Assert.IsTrue(button.IsEnabled);
        page.Tip = "Translating into French";
        Assert.AreEqual("Translating into French", button.ToolTip, "open again, the button's own tip is bound as it was");

        gate.IsEnabled = false;
        gate.IsEnabled = true;
        page.Tip = "Translate it";
        Assert.AreEqual("Translate it", button.ToolTip, "however often the gate closes and opens");
    });
}
