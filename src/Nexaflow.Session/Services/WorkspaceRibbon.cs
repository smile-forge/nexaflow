using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Icons;
using Nexaflow.Interaction;

namespace Nexaflow.Session.Services;

/// <summary>
/// A workspace's ribbon, shared by every window on it: the buttons it keeps, saved to the workspace's folder
/// (<see cref="RibbonLayoutService"/>) as they change, and the shipped ones whenever it would be left with none.
/// <para>
/// Every window hears a change, with who made it, so the window that made an edit need not take its own edit back. A pin, or
/// the shipped buttons put back, is heard by all of them. A pin is refused for a button already on the ribbon (the same page
/// on the same parameters), and the caller is told where that button is.
/// </para>
/// </summary>
public sealed class WorkspaceRibbon
{
    private readonly RibbonLayoutService _file;
    private readonly object _gate = new();
    private List<RibbonEntry>? _entries;

    public WorkspaceRibbon(string workspaceDir) => _file = new RibbonLayoutService(workspaceDir);

    /// <summary>The buttons, half-height groups and separators, in order: as saved, or the shipped ones while nothing is.</summary>
    public IReadOnlyList<RibbonEntry> Entries
    {
        get { lock (_gate) return _entries ??= _file.Load() is { Count: > 0 } saved ? saved : RibbonLayoutService.LoadDefaults(); }
    }

    /// <summary>Raised after the ribbon changed, with who changed it — null for a pin or the shipped buttons put back, which
    /// every window takes up.</summary>
    public event Action<object?>? Changed;

    /// <summary>Keeps <paramref name="entries"/> as the ribbon, made by <paramref name="by"/>. A ribbon left with nothing gets the
    /// shipped buttons back.</summary>
    public void Save(IEnumerable<RibbonEntry> entries, object by)
    {
        var kept = entries.ToList();
        if (kept.Count == 0) Keep(RibbonLayoutService.LoadDefaults(), by: null);
        else Keep(kept, by);
    }

    /// <summary>
    /// Pins a tab dropped at <paramref name="at"/> (-1: the end): as the tab's page kind's <see cref="ITabPinHandler"/> makes its
    /// button, or else as the tab is — its title, icon and parameters. Returns where the buttons already on the ribbon are.
    /// </summary>
    public IReadOnlyList<int> PinTab(Page tab, int at, WorkspaceRuntime runtime)
    {
        if (string.IsNullOrEmpty(tab.PageKind)) return [];

        var pin = runtime.FeatureManager.GetTabPinHandler(tab.PageKind, runtime) is { } handler ? handler.Pin(tab, at) : AsPinned(tab);
        return pin is null ? [] : Insert([pin], at);
    }

    /// <summary>
    /// Pins what was dropped: the first <see cref="IRibbonPinHandler"/> that takes a kind the payload carries and makes buttons of
    /// it has them inserted where they were dropped, in order. Returns where the buttons already on the ribbon are.
    /// </summary>
    public IReadOnlyList<int> PinDropped(RibbonPinRequest request, WorkspaceRuntime runtime)
    {
        foreach (var handler in runtime.FeatureManager.GetRibbonPinHandlers(runtime))
        {
            if (!request.Payload.ContainsAny(handler.AcceptedKinds)) continue;

            var pins = handler.Pin(request.Payload);
            if (pins.Count > 0) return Insert(pins, request.InsertIndex);
        }
        return [];
    }

    /// <summary>The pages a button can be added for: every page the workspace's features offer, less those already on the
    /// ribbon, by title.</summary>
    public IReadOnlyList<RibbonCatalogEntry> OfferedPages(WorkspaceRuntime runtime)
    {
        var entries = Entries;
        return [.. runtime.FeatureManager.GetRibbonCatalogPages(runtime)
                          .Where(page => !string.IsNullOrEmpty(page.PageKind) && ButtonFor(entries, page.PageKind!, page.PageParams) is null)
                          .Select(page => new RibbonCatalogEntry(page.Title, page.Icon, page.PageKind!, page.PageParams))
                          .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    private IReadOnlyList<int> Insert(IReadOnlyList<RibbonPinResult> pins, int at)
    {
        var entries = Entries.ToList();
        List<RibbonEntry> there = [];
        var added = false;

        foreach (var pin in pins)
        {
            if (ButtonFor(entries, pin.PageKind, pin.PageParams) is { } existing)
            {
                there.Add(existing);
                continue;
            }

            var entry = new RibbonEntry
            {
                Kind         = RibbonItemKind.Button,
                Label        = pin.Label,
                Icon         = pin.Icon,
                IconResource = pin.IconResource?.ToString(),
                Foreground   = pin.AccentColor,
                PageKind     = pin.PageKind,
                PageParams   = pin.PageParams,
            };
            if (at >= 0 && at < entries.Count) entries.Insert(at++, entry);
            else entries.Add(entry);
            added = true;
        }

        if (added) Keep(entries, by: null);
        return [.. there.Select(entry => entries.IndexOf(entry))];
    }

    private void Keep(List<RibbonEntry> entries, object? by)
    {
        lock (_gate)
        {
            _entries = entries;
            _file.Save(entries);
        }
        Changed?.Invoke(by);
    }

    private static RibbonPinResult AsPinned(Page tab) => new()
    {
        PageKind   = tab.PageKind!,
        Label      = tab.Title,
        Icon       = IconRef.Parse(tab.Icon),
        PageParams = tab.PageParams is null ? null : new(tab.PageParams),
    };

    /// <summary>The button already on the ribbon that opens <paramref name="pageKind"/> on the same parameters, or null.</summary>
    private static RibbonEntry? ButtonFor(IEnumerable<RibbonEntry> entries, string pageKind, Dictionary<string, string>? pageParams)
        => entries.FirstOrDefault(entry => entry.Kind == RibbonItemKind.Button && entry.PageKind == pageKind && SameParams(entry.PageParams, pageParams));

    private static bool SameParams(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        return a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }
}
