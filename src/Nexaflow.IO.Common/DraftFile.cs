using System.IO;

namespace Nexaflow.IO.Common;

/// <summary>
/// A file made for the user and kept in a temp folder — a picture drawn, a recording, a clip, a cut-out — handed to
/// <c>IShellServices.HandleObject</c> to open as a draft: shown in its viewer, with Save as to keep it where the user
/// chooses, offered in <see cref="OfferedFolder"/>. The feature whose viewer shows the file claims it, so whatever made it
/// never names one. Like any file, a draft is in front of the assistant only once the user gives it the draft's tab.
/// </summary>
/// <param name="Path">The file in its temp folder, on this machine's own disk.</param>
/// <param name="OfferedFolder">Where Save as starts — beside what it was made from, or the user's own folder for its kind.</param>
public sealed record DraftFile(string Path, string? OfferedFolder = null)
{
    /// <summary>The page parameter a draft's tab carries: the folder its Save as starts in.</summary>
    public const string PageParameterName = "draft";

    public bool IsValid => !string.IsNullOrWhiteSpace(Path) && File.Exists(Path);
}
