using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Session.Services;

/// <summary>
/// Takes each feature assembly into the session as it loads, however it came to load — a page opened, a lookup, the warm-up — as
/// an <see cref="ActivatedFeature"/> recording what it put in: its global configs (<see cref="FeatureConfigs"/>), its archive
/// handlers and stream codecs (the process's file system), its location backends (the session's registry), its theme resources —
/// folded into the theme by the platform's <see cref="IThemeHost"/>, on the UI thread — and its own <see cref="IFeatureStartup"/>.
/// </summary>
public sealed class FeatureActivation
{
    private static readonly string ArchiveHandlerName   = typeof(IArchiveHandler).FullName!;
    private static readonly string StreamCodecName      = typeof(IStreamCodec).FullName!;
    private static readonly string LocationProviderName = typeof(ILocationProvider).FullName!;
    private static readonly string FeatureStartupName   = typeof(IFeatureStartup).FullName!;

    private readonly FeatureCatalog _catalog;
    private readonly FeatureConfigs _configs;
    private readonly IUiThread _ui;
    private readonly IThemeHost _themes;
    private readonly LocationProviderRegistry _locationProviders;
    private readonly ILogger<FeatureActivation> _log;

    private readonly ConcurrentDictionary<string, ActivatedFeature> _activated = new(StringComparer.OrdinalIgnoreCase);

    // Theme resource dictionaries contributed by features, accumulated as assemblies activate.
    private readonly HashSet<Uri> _themeContributions = [];
    private readonly Lock _themeLock = new();
    private volatile bool _themeRefreshQueued;
    private int _themeGen;        // bumped when new contributions are added
    private int _themeApplied;    // last generation folded into the theme

    /// <param name="catalog">The feature index, which loads an assembly as it is needed and says when it has.</param>
    /// <param name="configs">Where a feature's global configs are registered.</param>
    /// <param name="ui">The thread the windows run on, where a feature's theme is folded in.</param>
    /// <param name="themes">Folds the features' theme contributions into the theme the windows are drawn with.</param>
    /// <param name="locationProviders">The session's location backends, which every feature's are registered in.</param>
    /// <param name="log">Where a feature's setup that failed is put on record.</param>
    public FeatureActivation(FeatureCatalog catalog, FeatureConfigs configs, IUiThread ui, IThemeHost themes,
                             LocationProviderRegistry locationProviders, ILogger<FeatureActivation> log)
    {
        _catalog           = catalog;
        _configs           = configs;
        _ui                = ui;
        _themes            = themes;
        _locationProviders = locationProviders;
        _log               = log;
    }

    /// <summary>Builds the catalogue's (cached) index — no feature assembly loads on the cache-hit path — and from then on takes
    /// in every feature assembly as it loads, Core's first.</summary>
    public void Start() => _catalog.Initialize(OnAssemblyActivated);

    /// <summary>The features taken in so far.</summary>
    public IReadOnlyCollection<ActivatedFeature> Activated => [.. _activated.Values];

    /// <summary>Every feature theme resource contributed so far, which a window applying a theme folds in beneath it.</summary>
    public IReadOnlyList<Uri> ThemeContributionUris { get { lock (_themeLock) return [.. _themeContributions]; } }

    /// <summary>
    /// Registers every location backend the catalogue knows, loading the assemblies that hold them — what a runtime calls before
    /// it mounts its connections, since a backend's assembly may not have activated yet. A backend already registered stays as it is.
    /// </summary>
    public void EnsureLocationProviders()
    {
        foreach (var type in _catalog.TypesImplementing<ILocationProvider>())
            if (RegisterLocationProvider(type) is { } provider && _activated.Values.FirstOrDefault(f => f.Assembly == type.Assembly) is { } owner)
                owner.Registered(provider);
    }

    /// <summary>Folds any pending feature theme contributions into the theme at once, on the UI thread — so a feature whose page is
    /// being opened has its theme resources in before its view loads, even if the warm-up only just activated it.</summary>
    internal void EnsureThemeApplied()
    {
        if (_ui.IsCurrent) ApplyThemeNow();
    }

    /// <summary>What a feature assembly puts into the session on its first load, recorded on its unit. Runs on whatever thread
    /// loaded it; the theme is folded in on the UI thread.</summary>
    private void OnAssemblyActivated(Assembly asm, FeatureCatalog.AssemblyEntry entry)
    {
        var feature      = _activated.GetOrAdd(entry.File, file => new ActivatedFeature(file, asm));
        var themeChanged = false;

        foreach (var te in entry.Types)
        {
            // Global feature configs (scoped ones are built per workspace by Workspace, not here).
            if (te.ConfigName is not null && !te.Scoped && asm.GetType(te.Name) is { } configType && _configs.Register(configType))
                feature.Registered(configType);

            // Archive backends ship in feature assemblies — registered into the process-wide file system so files inside archives
            // browse and open like a folder.
            if (te.Contracts.Contains(ArchiveHandlerName) && asm.GetType(te.Name) is { } at)
                try
                {
                    if (Activator.CreateInstance(at) is IArchiveHandler h) { VirtualFileSystem.Instance.RegisterHandler(h); feature.Registered(h); }
                }
                catch (Exception ex) { _log.LogWarning(ex, "registering the archive handler {Handler}", at.FullName); }
            if (te.Contracts.Contains(StreamCodecName) && asm.GetType(te.Name) is { } ct)
                try
                {
                    if (Activator.CreateInstance(ct) is IStreamCodec c) { VirtualFileSystem.Instance.RegisterCodec(c); feature.Registered(c); }
                }
                catch (Exception ex) { _log.LogWarning(ex, "registering the stream codec {Codec}", ct.FullName); }

            // Location backends likewise: the session's registry is where a runtime's sessions find their protocol.
            if (te.Contracts.Contains(LocationProviderName) && asm.GetType(te.Name) is { } lt && RegisterLocationProvider(lt) is { } provider)
                feature.Registered(provider);

            // Theme contributions. Fast path: the pack URIs captured during a scan with the toolkit up. Fallback: a scan that ran
            // without it (a unit-test run that rebuilt the shared catalogue) cannot build pack URIs, so ThemeUris is null — read them
            // again from the freshly loaded type, where the toolkit is up. Without this a headless-built catalogue silently drops
            // every feature's theme resources.
            var uris = te.ThemeUris;
            if ((uris is null || uris.Count == 0)
                && asm.GetType(te.Name) is { } themeType
                && typeof(IThemeContribution).IsAssignableFrom(themeType))
            {
                try
                {
                    uris = (Activator.CreateInstance(themeType) as IThemeContribution)?
                        .ResourceDictionaryUris.Select(u => u.ToString()).ToList();
                }
                catch (Exception ex) { _log.LogWarning(ex, "reading the theme of {Contribution}", themeType.FullName); }
            }
            if (uris is { Count: > 0 })
                foreach (var u in uris)
                    if (Uri.TryCreate(u, UriKind.Absolute, out var uri))
                    {
                        bool added;
                        lock (_themeLock) added = _themeContributions.Add(uri);
                        if (!added) continue;
                        feature.Contributed(uri);
                        themeChanged = true;
                    }
        }

        // The feature's own setup for the session, now its global configs are registered and before anything of it is built.
        foreach (var te in entry.Types)
        {
            if (!te.Contracts.Contains(FeatureStartupName) || asm.GetType(te.Name) is not { } startupType) continue;
            try
            {
                var startup = (IFeatureStartup)Activator.CreateInstance(startupType)!;
                startup.Start(_configs.StartupContext());
                feature.Started(startup);
            }
            catch (Exception ex) { _log.LogWarning(ex, "setting up {Feature} for the session", startupType.FullName); }
        }

        if (themeChanged)
        {
            lock (_themeLock) _themeGen++;
            RequestThemeRefresh();
        }
    }

    /// <summary>The backend of <paramref name="type"/>, registered; null when one of its kind is registered already or it cannot be
    /// made — its connections are then listed unreachable.</summary>
    private ILocationProvider? RegisterLocationProvider(Type type)
    {
        if (_locationProviders.Providers.Any(p => p.GetType() == type)) return null;
        try
        {
            if (Activator.CreateInstance(type) is not ILocationProvider provider) return null;
            _locationProviders.Register(provider);
            return provider;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "registering the location backend {Backend}", type.FullName);
            return null;
        }
    }

    // Folds the accumulated contributions in. On the UI thread this runs inline, so the first page's theme is ready before it
    // renders; off it (the warm-up) it is posted and coalesced — never a synchronous call, which would deadlock against an on-demand
    // load holding the catalogue's lock on the UI thread.
    private void RequestThemeRefresh()
    {
        if (_ui.IsCurrent) { ApplyThemeNow(); return; }
        if (_themeRefreshQueued) return;
        _themeRefreshQueued = true;
        _ui.PostInBackground(() => { _themeRefreshQueued = false; ApplyThemeNow(); });
    }

    private void ApplyThemeNow()
    {
        Uri[] uris; int gen;
        lock (_themeLock)
        {
            gen = _themeGen;
            if (gen == _themeApplied) return;   // nothing new since the last fold
            uris = [.. _themeContributions];
        }
        _themes.ApplyContributions(uris);
        lock (_themeLock) _themeApplied = gen;
    }
}
