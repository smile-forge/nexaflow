namespace Nexaflow.Features.Common;

/// <summary>
/// Lets a feature declare that certain files should be browsed like folders — the file explorer
/// descends into them (and back out) using the same path handling as a real directory, with nesting.
/// The archive implementation backs this with the virtual file system, but the contract is deliberately
/// generic (a feature could expose any container as a dynamic folder). Discovered by reflection, the same
/// way <see cref="Viewlets.IFolderViewlet"/> is.
/// <para>
/// This contract stays structural — purely "is this path a folder, and is expanding it the default
/// action" — so <c>Features.Common</c> takes no dependency on the IO layer. Enumeration and reading of
/// the contents go through the virtual file system, not this interface.
/// </para>
/// </summary>
public interface IDynamicFolder
{
    /// <summary>True when <paramref name="path"/> (an existing file) can be browsed as a folder.</summary>
    bool CanProcess(string path);

    /// <summary>
    /// Whether double-clicking the file should expand it as a folder by default (true for archives) or
    /// keep the file's normal open action, offering "browse as folder" only explicitly (false for
    /// document formats like <c>.docx</c> that merely happen to be zip-based). Default: true.
    /// </summary>
    bool ExpandsByDefault(string path) => true;
}
