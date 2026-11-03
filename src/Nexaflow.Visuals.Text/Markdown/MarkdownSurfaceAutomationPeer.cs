using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A surface as automation sees it: a control whose value is the text of the document it holds, read whole when asked for and set by
/// writing it — what a screen reader reads of a field and a test asks of a text box. What the content draws, its links among it, is the
/// content's own to say, beneath this.
/// </summary>
internal sealed class MarkdownSurfaceAutomationPeer(MarkdownSurface owner) : UserControlAutomationPeer(owner), IValueProvider
{
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

    /// <inheritdoc/>
    public string Value => owner.Document.ToString();

    /// <inheritdoc/>
    public bool IsReadOnly => owner.IsReadOnly;

    /// <summary>The document's text made <paramref name="value"/>, as one step the reader can take back.</summary>
    public void SetValue(string value)
    {
        if (owner.IsReadOnly) throw new ElementNotEnabledException();

        owner.Replace(0, owner.Document.Current.Length, value);
    }
}
