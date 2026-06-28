using System;
using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// Shell context provided to <see cref="IRibbonItemExecutor.Execute"/> at button-click time.
/// Abstracts current file selection, error display, and ribbon item removal.
/// </summary>
public interface IRibbonExecutionContext
{
    /// <summary>Absolute paths of files currently selected in the active tab, if any.</summary>
    IReadOnlyList<string> SelectedFilePaths { get; }

    void ShowError(string message);

    void ShowConfirmation(string title, string message, Action onConfirm, Action? onCancel = null);

    /// <summary>Removes the ribbon button that triggered this execution.</summary>
    void RemoveCurrentRibbonItem();
}
