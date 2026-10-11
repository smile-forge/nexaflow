using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Puts the links of what a <see cref="ContentElement"/> shows into the automation tree: one invokable
/// <see cref="AutomationControlType.Hyperlink"/> per link, named by its words, whose value is where it goes. The content is drawn rather than built from
/// controls, so without this a document is one opaque rectangle to a screen reader and to a UI journey alike.
/// <para>
/// The engine owns the content, so it is the engine that says what the links are and what following one does
/// (<see cref="ContentEngine.LaidLinks"/>, <see cref="ContentEngine.Open"/>). This only puts that where UI Automation
/// reads it, in screen coordinates, and asks again whenever the content is laid out again.
/// </para>
/// </summary>
internal sealed class ContentElementAutomationPeer(ContentElement owner) : FrameworkElementAutomationPeer(owner)
{
    private IReadOnlyList<LaidLink> _links = [];
    private List<AutomationPeer> _peers = [];

    protected override string GetClassNameCore() => nameof(ContentElement);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override List<AutomationPeer> GetChildrenCore()
    {
        var links = owner.LaidLinks();
        if (!links.SequenceEqual(_links))
        {
            _links = links;
            _peers = [.. links.Select(link => (AutomationPeer)new LinkPeer(owner, link))];
        }

        return _peers;
    }

    /// <summary>One link. Pressed, it does what a click on it does.</summary>
    private sealed class LinkPeer(ContentElement owner, LaidLink link) : AutomationPeer, IInvokeProvider, IValueProvider
    {
        /// <summary>Follows it as a click would. A link the content has since been laid out again without is gone.</summary>
        public void Invoke()
        {
            if (!owner.Holds(link)) throw new ElementNotAvailableException();
            owner.Open(link);
        }

        /// <summary>Where it goes — read, as a hyperlink's value is everywhere else, and never written.</summary>
        public string Value => link.Target;
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new ElementNotEnabledException();

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.Invoke or PatternInterface.Value ? this : null;

        protected override string GetNameCore() => link.Text;
        protected override string GetHelpTextCore() => link.Tip ?? link.Target;
        protected override string GetItemStatusCore() => string.Empty;
        protected override string GetItemTypeCore() => string.Empty;

        protected override Rect GetBoundingRectangleCore()
        {
            if (PresentationSource.FromVisual(owner) is null) return Rect.Empty;
            var local = owner.FromContent(link.Bounds);
            return new Rect(owner.PointToScreen(local.TopLeft), owner.PointToScreen(local.BottomRight));
        }

        protected override Point GetClickablePointCore() =>
            PresentationSource.FromVisual(owner) is null
                ? new Point(double.NaN, double.NaN)
                : owner.PointToScreen(owner.FromContent(link.At));

        protected override bool IsOffscreenCore() => !owner.IsVisible || !owner.Showing(link.Bounds);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;
        protected override string GetClassNameCore() => nameof(LaidLink);
        protected override string GetAutomationIdCore() => string.Empty;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override bool IsEnabledCore() => owner.IsEnabled;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override bool HasKeyboardFocusCore() => false;
        protected override void SetFocusCore() { }
        protected override string GetAcceleratorKeyCore() => string.Empty;
        protected override string GetAccessKeyCore() => string.Empty;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
    }
}
