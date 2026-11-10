using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Hosts the selected Options section's own editor (a <c>[CustomControl]</c>). A plain <see cref="ContentControl"/>
/// creates no automation peer, so an AutomationId set on one never reaches UI Automation and the editor surfaces as a
/// nameless child of the panel. This host creates one, so <c>Options_CustomSection</c> can be found, and a journey can
/// tell a custom editor is showing.
/// </summary>
public sealed class OptionsSectionHost : ContentControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
