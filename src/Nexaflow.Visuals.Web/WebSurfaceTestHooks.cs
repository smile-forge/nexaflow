using Microsoft.Web.WebView2.Core;

namespace Nexaflow.Visuals.Web;

/// <summary>
/// Lets the test suite exercise the runtime-missing classification without naming a WebView2 type.
/// <para>
/// The whole point of fencing the WebView2 package inside this assembly is that no consumer — "and every
/// test that touches this control" — takes a reference on it. But whether an init failure is classified as
/// "the runtime isn't installed" decides if the user is offered the download link that fixes their machine,
/// which is worth a test. So the exception is minted here, behind the fence, and the test only ever handles
/// it as an <see cref="System.Exception"/>.
/// </para>
/// </summary>
internal static class WebSurfaceTestHooks
{
    /// <summary>The exception WebView2 raises when the Evergreen runtime is not installed.</summary>
    internal static Exception NewRuntimeNotFound() => new WebView2RuntimeNotFoundException();
}
