using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch;

/// <summary>
/// Presents a <see cref="FileSearchRequest"/> as a Search tab. Discovered by reflection and dispatched via
/// <see cref="IShellServices.HandleObject"/>, so the file browser can show file-search results without
/// knowing this feature exists — the same handoff the scratchpad uses for a clicked URL.
/// </summary>
public sealed class SearchObjectHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) => obj is FileSearchRequest { HasScope: true };

    public void Handle(object obj)
    {
        if (obj is not FileSearchRequest r) return;

        // The query travels in the bar's own syntax, so a regex survives the trip through the tab's
        // string-keyed parameters and the Search tab re-parses it rather than treating "/pat/" as literal.
        shell.OpenTab(SearchTabRegistration.StaticPageKind, new()
        {
            ["query"]  = SearchSyntax.Format(r.Query),
            ["root"]   = r.Root,
            ["drives"] = string.Join(";", r.Drives)
        });
    }
}
