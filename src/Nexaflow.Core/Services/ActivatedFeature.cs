using System.Reflection;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Services;

/// <summary>
/// One feature assembly as the session has taken it in, with everything it put into the session as it activated: its global
/// configs, its archive handlers and stream codecs, its location backends, its theme resources and its own setup. Kept together so
/// the feature can be told apart from the rest of the session, and withdrawn as a whole.
/// </summary>
public sealed class ActivatedFeature
{
    private readonly List<Type> _configs = [];
    private readonly List<IArchiveHandler> _archiveHandlers = [];
    private readonly List<IStreamCodec> _streamCodecs = [];
    private readonly List<ILocationProvider> _locationProviders = [];
    private readonly List<Uri> _themeResources = [];
    private readonly List<IFeatureStartup> _startups = [];

    /// <param name="file">The assembly's file, as the catalogue indexes it.</param>
    /// <param name="assembly">The assembly loaded.</param>
    internal ActivatedFeature(string file, Assembly assembly)
    {
        File     = file;
        Assembly = assembly;
    }

    /// <summary>The assembly's file, as the catalogue indexes it.</summary>
    public string File { get; }

    /// <summary>The assembly loaded.</summary>
    public Assembly Assembly { get; }

    /// <summary>The global configs it registered in the session's configuration.</summary>
    public IReadOnlyList<Type> Configs => _configs;

    /// <summary>The archive handlers it registered with the process's file system.</summary>
    public IReadOnlyList<IArchiveHandler> ArchiveHandlers => _archiveHandlers;

    /// <summary>The stream codecs it registered with the process's file system.</summary>
    public IReadOnlyList<IStreamCodec> StreamCodecs => _streamCodecs;

    /// <summary>The location backends it registered in the session's registry.</summary>
    public IReadOnlyList<ILocationProvider> LocationProviders => _locationProviders;

    /// <summary>The theme resources it contributed.</summary>
    public IReadOnlyList<Uri> ThemeResources => _themeResources;

    /// <summary>Its own setups for the session, run as it activated; one that holds what it set up is disposable.</summary>
    public IReadOnlyList<IFeatureStartup> Startups => _startups;

    internal void Registered(Type config) => _configs.Add(config);
    internal void Registered(IArchiveHandler handler) => _archiveHandlers.Add(handler);
    internal void Registered(IStreamCodec codec) => _streamCodecs.Add(codec);
    internal void Registered(ILocationProvider provider) => _locationProviders.Add(provider);
    internal void Contributed(Uri themeResource) => _themeResources.Add(themeResource);
    internal void Started(IFeatureStartup startup) => _startups.Add(startup);
}
