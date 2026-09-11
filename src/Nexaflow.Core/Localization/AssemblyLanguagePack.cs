using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Nexaflow.Core.Localization;

/// <summary>
/// A language pack as shipped: a resource-only assembly under <c>Languages\</c>, read through
/// <see cref="Assembly.GetManifestResourceStream(string)"/>. Loaded by path — packs are drop-ins, deliberately kept
/// out of deps.json, so a by-name load could never find one.
/// </summary>
internal sealed class AssemblyLanguagePack : ILanguagePack
{
    // One context for every pack. They hold no code, so they resolve nothing beyond System.Runtime, which falls
    // through to the default context (the base Load returns null). Not collectible: a pack stays loaded once read,
    // so switching back to a language costs nothing.
    private static readonly AssemblyLoadContext Context = new("Nexaflow.Languages");

    private readonly Assembly _assembly;

    private AssemblyLanguagePack(string code, Assembly assembly)
    {
        Code          = code;
        _assembly     = assembly;
        ResourceNames = assembly.GetManifestResourceNames();
    }

    public string Code { get; }

    public IReadOnlyCollection<string> ResourceNames { get; }

    public Stream? Open(string logicalName) => _assembly.GetManifestResourceStream(logicalName);

    /// <summary>Loads the pack at <paramref name="path"/> (absolute). Throws when the file is not a loadable
    /// assembly, which <see cref="LanguageManager"/> treats as "no such pack".</summary>
    public static ILanguagePack Load(string code, string path)
        => new AssemblyLanguagePack(code, Context.LoadFromAssemblyPath(path));
}
