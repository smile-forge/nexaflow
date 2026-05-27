using Nexaflow.Features.Common;

namespace Nexaflow.Features.WindowsFileSystem.RibbonHandlers;

/// <summary>
/// Carries an <see cref="IFileAction"/> and the files selected at pin time.
/// Used as the drag payload and context-menu "Add to Ribbon" payload.
/// </summary>
internal sealed record FileActionPinPayload(IFileAction Action, IReadOnlyList<string> SelectedPaths);
