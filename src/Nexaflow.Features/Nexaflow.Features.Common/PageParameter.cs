namespace Nexaflow.Features.Common;

/// <summary>
/// Describes one parameter a page kind accepts in its <c>pageParams</c> dictionary. Advertised by
/// <see cref="IPageRegistration.Parameters"/> so the shell can tell the AI (and other callers) which
/// pages are openable and what each one needs.
/// </summary>
public sealed record PageParameter(string Name, string Description, bool Required = true);
