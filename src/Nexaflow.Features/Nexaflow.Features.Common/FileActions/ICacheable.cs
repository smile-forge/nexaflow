namespace Nexaflow.Features.Common;

/// <summary>
/// Marker interface for <see cref="IFileAction"/> / <see cref="IFolderAction"/> /
/// <see cref="IFileCreateAction"/> types whose instances are fully determined by
/// their <see cref="Nexaflow.Core.Models.WorkContext"/> — i.e. there is exactly one
/// logical instance per WorkContext.  <see cref="FeatureManager"/> only includes
/// implementing types in its auto-discovery lists and caches them per
/// <c>(Type, WorkContext)</c>.
///
/// Types that do <em>not</em> implement this interface (e.g.
/// <c>ShellVerbAction</c>, <c>FolderActionAdapter</c>) are excluded from the
/// discovery lists.  They must be rehydrated through
/// <see cref="IFileAction.GetReinitParams"/> / a static <c>Rehydrate</c> factory,
/// or reconstructed through a WorkContext-aware path inside
/// <c>FeatureManager.FindFileAction</c>.
/// </summary>
public interface ICacheable { }
