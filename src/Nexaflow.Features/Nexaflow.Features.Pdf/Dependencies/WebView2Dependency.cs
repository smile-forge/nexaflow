using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Visuals.Web;

namespace Nexaflow.Features.Pdf.Dependencies;

/// <summary>
/// The PDF reader draws the document with Edge, so it needs the Evergreen WebView2 runtime.
/// <para>
/// The Web tab declares the SAME id — the registry merges them into one About row naming both features.
/// Keep the wording here and in <c>Nexaflow.Features.Web</c> identical: the merge keeps whichever
/// declaration it meets first, so drift between the two would show up as a message that changes depending
/// on assembly load order.
/// </para>
/// </summary>
public sealed class WebView2Dependency : IExternalDependency
{
    /// <summary>Shared with the Web tab's declaration — see the note above before changing it.</summary>
    public const string DependencyId = "webview2-runtime";

    public string Id          => DependencyId;
    public string DisplayName => "Microsoft Edge WebView2 Runtime";

    public string Description =>
        "Renders PDFs and web pages inside Nexaflow. Without it those tabs fall back to a summary panel "
        + "and offer to open the file in your default application instead.";

    public ExternalDependencyKind Kind => ExternalDependencyKind.Required;

    public string? InstallUrl => "https://developer.microsoft.com/en-us/microsoft-edge/webview2/consumer/";

    public ExternalDependencyStatus Probe()
        => WebView2Runtime.TryGetVersion() is { } version
            ? new ExternalDependencyStatus(ExternalDependencyState.Present, version)
            : new ExternalDependencyStatus(ExternalDependencyState.Missing);
}
