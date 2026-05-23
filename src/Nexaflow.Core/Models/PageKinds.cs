namespace Nexaflow.Core.Models;

/// <summary>
/// Well-known page-kind identifiers for the core (non-feature) pages owned by
/// <c>Nexaflow.Core</c>.  Feature assemblies define their own page-kind strings
/// inside their <see cref="Nexaflow.Features.Common.IPageRegistration"/> implementations.
/// </summary>
public static class PageKinds
{
    public const string FileSystem  = "FileSystem";
    public const string AiChat      = "AiChat";
    public const string Placeholder = "Placeholder";
}
