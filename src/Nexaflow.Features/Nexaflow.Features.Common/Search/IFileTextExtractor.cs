using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// Pulls searchable plain text out of a file of a format this implementation understands — so a content
/// search can look inside a notebook, an archive entry or a structured document rather than only files that
/// happen to be plain text.
/// <para>
/// Deliberately headless, and deliberately NOT <see cref="ISearchable"/>. That contract is implemented by
/// page ViewModels, which own thread-affine UI state and start watchers; spinning one up per file on a
/// background verification pass would be wrong at every level. A feature that owns a format implements this
/// alongside its viewer instead.
/// </para>
/// <para>
/// Found with <c>IShellServices.DiscoverImplementations&lt;IFileTextExtractor&gt;()</c> and constructed with
/// <c>Activator.CreateInstance</c>, so implementations need a public parameterless constructor. When none
/// claims a file, the verifier falls back to reading it as text.
/// </para>
/// </summary>
public interface IFileTextExtractor
{
    /// <summary>True when this implementation understands <paramref name="path"/>'s format. Must be a cheap
    /// decision — typically the extension — because it is asked for every candidate file.</summary>
    bool CanExtract(string path);

    /// <summary>
    /// The file's text, or null when it can't be read or isn't text-bearing after all. Stop at
    /// <paramref name="maxBytes"/>: a verification pass may open thousands of files and must not be able to
    /// pull a multi-gigabyte one into memory. Return what was read rather than throwing on a partial read.
    /// </summary>
    Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct);
}
