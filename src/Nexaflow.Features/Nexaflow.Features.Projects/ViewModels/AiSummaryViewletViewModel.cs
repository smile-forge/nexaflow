using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// The AI summary viewlet over a project folder: the summary written for the folder, and saving a new one. How it is
/// shown and edited is the view's.
/// </summary>
public sealed class AiSummaryViewletViewModel(ProjectOperations ops, string folderPath, IViewletController controller)
{
    /// <summary>The host the viewlet sits in: its display mode, and asking for another.</summary>
    public IViewletController Controller { get; } = controller;

    /// <summary>The folder's summary as last saved.</summary>
    public string SavedText { get; private set; } = ops.ReadDirectorySummary(folderPath) ?? string.Empty;

    /// <summary>Saves <paramref name="text"/> as the folder's summary. One that cannot be written (access denied) leaves the saved summary as it was.</summary>
    public void Save(string text)
    {
        try
        {
            ops.WriteDirectorySummary(folderPath, text);
            SavedText = text;
        }
        catch { /* access denied: the summary on disk is unchanged */ }
    }
}
