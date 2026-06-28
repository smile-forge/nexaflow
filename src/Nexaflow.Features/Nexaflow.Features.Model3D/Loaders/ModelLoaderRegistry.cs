namespace Nexaflow.Features.Model3D.Loaders;

/// <summary>
/// The feature's model-loader registry: holds the registered <see cref="IModelLoader"/>s and picks the
/// first one that accepts a given file. Built-ins are registered in the constructor; adding a new format
/// is one <see cref="Register"/> call (or a new built-in line here) — nothing outside this feature changes.
/// First-registered wins on overlap, so register more specific loaders before broader ones.
/// </summary>
public sealed class ModelLoaderRegistry
{
    private readonly List<IModelLoader> _loaders = [];

    public ModelLoaderRegistry()
    {
        Register(new HelixModelLoader());
        Register(new GltfModelLoader());
        Register(new AssimpModelLoader()); // last: dedicated loaders above win for their formats
    }

    public void Register(IModelLoader loader)
    {
        if (!_loaders.Contains(loader)) _loaders.Add(loader);
    }

    /// <summary>The loader that handles <paramref name="path"/>, or null if no registered loader does.</summary>
    public IModelLoader? LoaderFor(string path) => _loaders.FirstOrDefault(l => l.CanLoad(path));

    /// <summary>All extensions any registered loader handles (for diagnostics / context).</summary>
    public IReadOnlyList<string> SupportedExtensions =>
        _loaders.SelectMany(l => l.SupportedExtensions).Distinct().ToList();
}
