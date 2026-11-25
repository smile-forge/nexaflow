using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// The machine's third-party component picture: finds every <see cref="IExternalDependency"/> a feature
/// declares, probes each one off the UI thread, and caches the answers.
/// <para>
/// Two consumers. Options → About lists the whole set so "why doesn't X work on this PC" is answerable
/// without reading a log; and a feature can ask <see cref="StatusOf"/> before it starts using a component,
/// so it can say something precise instead of failing deeper in. Both read the cache — probing is not
/// repeated per question.
/// </para>
/// <para>
/// Shaped after <see cref="HostCapabilityService"/>: one-shot background probe, cached report, event
/// marshalled to the UI thread. Unlike that one this can be re-run on demand (About has a re-check
/// button), since a user who installs a missing runtime should not have to restart to see it appear.
/// </para>
/// <para>
/// The session makes one (<see cref="ShellHost"/>) and hands it to what reads it: every window, for the About page and the
/// panel a page that could not be made is drawn with, and every runtime's <see cref="ShellServices"/>, for a feature's question.
/// </para>
/// </summary>
/// <param name="ui">The thread <see cref="Updated"/> is raised on.</param>
/// <param name="catalog">The feature catalogue, which finds every declaration the features ship.</param>
/// <param name="services">What a declaration may take in its constructor, matched by type — the AI host's reading of its runtime,
/// which what Nexaflow's own models need is judged from. A declaration asking for what is not here declares nothing.</param>
public sealed class ExternalDependencyRegistry(IUiThread ui, FeatureCatalog catalog, IReadOnlyList<object> services)
{
    private readonly object _gate = new();
    private IReadOnlyList<ExternalDependencyReport> _reports = [];
    private Task? _inFlight;

    /// <summary>The last completed probe, or empty before the first one finishes.</summary>
    public IReadOnlyList<ExternalDependencyReport> Reports { get { lock (_gate) return _reports; } }

    /// <summary>True once a probe has completed at least once.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Fired on the UI thread each time a probe completes.</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// Probes everything, off the UI thread. Concurrent calls share the one in-flight run rather than
    /// stacking; a later call after it finishes re-probes (that is the About page's re-check).
    /// </summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running) return running;
            return _inFlight = Task.Run(ProbeAll);
        }
    }

    /// <summary>Kicks off the first probe if nothing has run yet. Safe to call repeatedly.</summary>
    public void StartProbe()
    {
        if (IsReady) return;
        _ = RefreshAsync();
    }

    /// <summary>
    /// What the last probe found for <paramref name="id"/>. Answers
    /// <see cref="ExternalDependencyState.Unknown"/> for an id nothing declared, and while the first probe
    /// is still running — a caller must treat Unknown as "carry on and try", never as "it is missing".
    /// </summary>
    public ExternalDependencyStatus StatusOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ExternalDependencyStatus.Unknown();

        foreach (var r in Reports)
            if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
                return r.Status;

        return ExternalDependencyStatus.Unknown(
            IsReady ? Str.Get("Shell.Components.Undeclared") : Str.Get("Shell.Components.NotProbed"));
    }

    private void ProbeAll()
    {
        var reports = BuildReports();

        lock (_gate) _reports = reports;
        IsReady = true;

        // A probe runs on the thread pool, never on the UI thread: the event is queued there.
        ui.Post(() => Updated?.Invoke(this, EventArgs.Empty));
    }

    private List<ExternalDependencyReport> BuildReports() => BuildReports(Discover(), services);

    /// <summary>
    /// The merge, with the declaring types handed in rather than discovered — the dedup, the Required-wins
    /// precedence and the tolerance of a broken declaration are the parts worth testing, and they cannot be
    /// exercised through a live <see cref="FeatureCatalog"/> scan. Each declaration is built with what
    /// <paramref name="services"/> holds of its constructor's parameters.
    /// </summary>
    internal static List<ExternalDependencyReport> BuildReports(IEnumerable<Type> types, IReadOnlyList<object> services)
    {
        // Keyed on the component's id, not the declaring type: one runtime can be declared by several features,
        // and the user cares about it listed once, naming each.
        var merged = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            IExternalDependency decl;
            try
            {
                if (Declare(type, services) is not IExternalDependency d) continue;
                decl = d;
            }
            catch { continue; }   // a declaration that cannot be built simply declares nothing

            string id;
            try { id = decl.Id; } catch { continue; }
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (!merged.TryGetValue(id, out var acc))
                merged[id] = acc = new Accumulator(decl, Probe(decl));

            ExternalDependencyKind kind;
            try { kind = decl.Kind; } catch { kind = ExternalDependencyKind.Optional; }

            acc.Add(FeatureNameOf(type), kind);
        }

        return merged.Values
            .Select(a => a.ToReport())
            .OrderByDescending(r => r.IsBlocking)                    // problems first
            .ThenBy(r => r.Kind)                                     // then Required above Optional
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// <paramref name="type"/>'s declaration, built with the constructor whose every parameter <paramref name="services"/> holds
    /// one of, the widest first — a feature's, which takes nothing, by its parameterless one; null where none can be built.
    /// </summary>
    private static object? Declare(Type type, IReadOnlyList<object> services)
    {
        foreach (var ctor in type.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var args = ctor.GetParameters().Select(p => services.FirstOrDefault(p.ParameterType.IsInstanceOfType)).ToArray();
            if (args.All(arg => arg is not null)) return ctor.Invoke(args);
        }
        return null;
    }

    private IEnumerable<Type> Discover()
    {
        try { return catalog.TypesImplementing<IExternalDependency>(); }
        catch { return []; }
    }

    private static ExternalDependencyStatus Probe(IExternalDependency decl)
    {
        try
        {
            return decl.Probe() ?? ExternalDependencyStatus.Unknown(Str.Get("Shell.Components.NoAnswer"));
        }
        catch (Exception ex)
        {
            // A probe that throws has told us nothing about the component, so it must not read as Missing —
            // that would put a scary red row on the About page for a bug in the check itself.
            return ExternalDependencyStatus.Unknown(Str.Format("Shell.Components.CheckFailedFormat", ex.Message));
        }
    }

    /// <summary>
    /// "Pdf" from <c>Nexaflow.Features.Pdf</c> — good enough to tell the user which part of the app wants
    /// the component, without adding a name to the contract for every feature to get wrong.
    /// </summary>
    private static string FeatureNameOf(Type type)
    {
        var name = type.Assembly.GetName().Name ?? type.Namespace ?? "Nexaflow";
        const string prefix = "Nexaflow.Features.";
        if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        else if (name.StartsWith("Nexaflow.", StringComparison.Ordinal)) name = name["Nexaflow.".Length..];
        return name.Replace('.', ' ');
    }

    /// <summary>Collects the features declaring one component while its probe result is held once.</summary>
    private sealed class Accumulator(IExternalDependency decl, ExternalDependencyStatus status)
    {
        private readonly SortedSet<string> _features = new(StringComparer.CurrentCultureIgnoreCase);
        private ExternalDependencyKind _kind = Safe(() => decl.Kind, ExternalDependencyKind.Optional);

        /// <param name="feature">The feature declaring it.</param>
        /// <param name="kind">
        /// That declaration's own kind — NOT re-read from the held declaration, which is only ever the first
        /// one seen. Reading it from there made the merge order-dependent: an Optional declaration met first
        /// could never be upgraded by a later Required one, so a genuinely broken feature showed as optional.
        /// </param>
        public void Add(string feature, ExternalDependencyKind kind)
        {
            _features.Add(feature);
            // Required wins: if any feature is broken without it, the row is not "optional".
            if (kind == ExternalDependencyKind.Required) _kind = ExternalDependencyKind.Required;
        }

        public ExternalDependencyReport ToReport() => new(
            Safe(() => decl.Id, string.Empty)!,
            Safe(() => decl.DisplayName, null) ?? Safe(() => decl.Id, string.Empty)!,
            Safe(() => decl.Description, null) ?? string.Empty,
            _kind,
            Safe<string?>(() => decl.InstallUrl, null),
            status,
            _features.ToList());

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try { return read(); } catch { return fallback; }
        }
    }
}
