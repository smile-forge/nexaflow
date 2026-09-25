using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Nexaflow.Features.Solver.Views;

/// <summary>
/// Puts the regions an <see cref="OctagonNavigator"/> paints into the automation tree: one invokable child per
/// tile and one for the centre. Without it the navigator is a single opaque rectangle to a screen reader and to a
/// UI journey alike. A child's id is the navigator's own with <c>_Tile{n}</c> (clockwise from the top, as drawn)
/// or <c>_Centre</c>, and a tile's item type says whether it opens a ring (<c>Group</c>) or types one (<c>Symbol</c>).
/// </summary>
internal sealed class OctagonNavigatorAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly OctagonNavigator _owner;
    private readonly RegionPeer _centre;
    private readonly RegionPeer[] _tiles;

    public OctagonNavigatorAutomationPeer(OctagonNavigator owner) : base(owner)
    {
        _owner = owner;
        _centre = new RegionPeer(owner, -1);
        _tiles = [.. Enumerable.Range(0, OctagonNavigator.MaxNodes).Select(i => new RegionPeer(owner, i))];

        // A collapsed navigator offers no regions: pressing one would drive a ring nobody can see.
        owner.IsVisibleChanged += (_, _) => ResetChildrenCache();
    }

    protected override string GetClassNameCore() => nameof(OctagonNavigator);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override List<AutomationPeer> GetChildrenCore()
    {
        if (!_owner.IsVisible) return [];
        var nodes = _owner.Nodes;
        var children = new List<AutomationPeer>(OctagonNavigator.MaxNodes + 1) { _centre };
        for (var i = 0; i < _tiles.Length; i++)
            if (nodes is not null && i < nodes.Count && nodes[i] is not null) children.Add(_tiles[i]);
        return children;
    }

    /// <summary>One painted region. <c>-1</c> is the centre.</summary>
    private sealed class RegionPeer(OctagonNavigator owner, int index) : AutomationPeer, IInvokeProvider
    {
        private OctagonNode? Node =>
            index >= 0 && owner.Nodes is { } nodes && index < nodes.Count ? nodes[index] : null;

        public void Invoke()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            owner.Press(index);
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : null;

        protected override string GetAutomationIdCore()
        {
            var id = AutomationProperties.GetAutomationId(owner);
            return (string.IsNullOrEmpty(id) ? nameof(OctagonNavigator) : id) + (index < 0 ? "_Centre" : $"_Tile{index}");
        }

        protected override string GetNameCore() => index < 0 ? owner.CentreLabel : Node?.Label ?? string.Empty;
        protected override string GetHelpTextCore() => Node?.Tooltip ?? string.Empty;
        protected override string GetItemTypeCore() => Node is { } node ? node.HasChildren ? "Group" : "Symbol" : string.Empty;
        protected override bool IsEnabledCore() => owner.IsVisible && (index < 0 ? owner.CanGoUp : Node is not null);

        protected override Rect GetBoundingRectangleCore()
        {
            if (PresentationSource.FromVisual(owner) is null) return Rect.Empty;
            var local = owner.RegionBounds(index);
            if (local.IsEmpty) return Rect.Empty;
            return new Rect(owner.PointToScreen(local.TopLeft), owner.PointToScreen(local.BottomRight));
        }

        protected override Point GetClickablePointCore()
        {
            var r = GetBoundingRectangleCore();
            return r.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        protected override bool IsOffscreenCore() => !owner.IsVisible;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => nameof(OctagonNavigator) + (index < 0 ? "Centre" : "Tile");
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override bool HasKeyboardFocusCore() => false;
        protected override void SetFocusCore() { }
        protected override string GetAcceleratorKeyCore() => string.Empty;
        protected override string GetAccessKeyCore() => string.Empty;
        protected override string GetItemStatusCore() => string.Empty;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
    }
}
