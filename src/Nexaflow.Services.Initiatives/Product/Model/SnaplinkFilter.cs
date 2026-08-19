namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// Addresses snaplinks by what they <em>are</em> rather than where they sit in a list.
/// <para>
/// A node's snaplinks are an ordered list, so a positional index is only valid until the next edit
/// reorders it — fine when a human is looking at a rendered list and clicking one, useless in a script
/// that has to name the same link twice, or across a batch where an earlier instruction already shifted
/// it. Every field set here must agree for a link to match; the ones left null are not considered, so
/// <c>Doc</c> alone drops every link into that file and adding <c>Method</c> narrows it to the one.
/// </para>
/// </summary>
/// <param name="Type">Link type (<c>code</c>/<c>markdown</c>/<c>node</c>/<c>url</c>).</param>
/// <param name="Doc">File path, compared with <c>/</c> and <c>\</c> treated alike and case-insensitively.</param>
/// <param name="Class">Declaring type of a <c>code</c> link.</param>
/// <param name="Method">Member of a <c>code</c> link.</param>
/// <param name="Target">The target of a <c>node</c> or <c>url</c> link.</param>
public sealed record SnaplinkFilter(
    string? Type = null,
    string? Doc = null,
    string? Class = null,
    string? Method = null,
    string? Target = null)
{
    /// <summary>True when nothing was specified — a filter that would match everything, which callers
    /// treat as "no filter given" rather than as "delete the lot".</summary>
    public bool IsEmpty =>
        Type is null && Doc is null && Class is null && Method is null && Target is null;

    public bool Matches(Snaplink link) =>
        Same(Type, link.Type) &&
        SamePath(Doc, link.Doc) &&
        Same(Class, link.Class) &&
        Same(Method, link.Method) &&
        Same(Target, link.Target);

    private static bool Same(string? wanted, string? actual) =>
        wanted is null || string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase);

    /// <summary>Paths are compared slash-agnostically: the tree stores <c>/</c>, but a caller pasting from
    /// Explorer or a Windows stack trace has <c>\</c>, and a filter that silently matched nothing there
    /// would look like the link was already gone.</summary>
    private static bool SamePath(string? wanted, string? actual) =>
        wanted is null || string.Equals(Normalize(wanted), Normalize(actual), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? path) => path?.Replace('\\', '/').TrimStart('.', '/');
}
