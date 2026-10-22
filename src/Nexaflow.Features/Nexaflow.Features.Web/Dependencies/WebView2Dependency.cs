using Nexaflow.Features.Common.Dependencies;

using Nexaflow.Visuals.Web;

namespace Nexaflow.Features.Web.Dependencies;

/// <summary>
/// The Web tab is a WebView2 host, so it needs the Evergreen WebView2 runtime.
/// <para>
/// A feature that comes to host the browser too declares this SAME id, and the registry merges the declarations into one
/// About row naming each. Such a declaration takes its name and its description from here and from
/// <see cref="WebView2Runtime.Description"/>: the merge keeps whichever declaration it meets first, so drift between them
/// would show up as a message that changes depending on assembly load order.
/// </para>
/// </summary>
public sealed class WebView2Dependency : IExternalDependency
{
    /// <summary>The runtime's one id, whichever feature declares it — see the note above before changing it.</summary>
    public const string DependencyId = "webview2-runtime";

    public string Id          => DependencyId;
    public string DisplayName => "Microsoft Edge WebView2 Runtime";

    public string Description =>
        WebView2Runtime.Description;

    public ExternalDependencyKind Kind => ExternalDependencyKind.Required;

    public string? InstallUrl => "https://developer.microsoft.com/en-us/microsoft-edge/webview2/consumer/";

    public ExternalDependencyStatus Probe()
        => WebView2Runtime.TryGetVersion() is { } version
            ? new ExternalDependencyStatus(ExternalDependencyState.Present, version)
            : new ExternalDependencyStatus(ExternalDependencyState.Missing);
}
