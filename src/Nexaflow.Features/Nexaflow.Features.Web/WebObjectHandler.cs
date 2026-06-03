using Nexaflow.Features.Common;

namespace Nexaflow.Features.Web;

/// <summary>
/// Opens an http(s) URL in a browser tab. Discovered by reflection and dispatched via
/// <see cref="IShellServices.HandleObject"/>, so a feature (e.g. the scratchpad) can route a
/// clicked URL link here without knowing the web feature exists.
/// </summary>
public sealed class WebObjectHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj)
        => obj is string s
           && Uri.TryCreate(s, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public void Handle(object obj)
    {
        if (obj is string url)
            shell.OpenTab(HtmlTabRegistration.StaticPageKind, new() { ["path"] = url });
    }
}
