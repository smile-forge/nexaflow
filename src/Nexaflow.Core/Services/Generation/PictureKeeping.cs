using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// What becomes of a picture a model made, drawn or edited alike: while Enhanced AI Protection Mode is on it passes the
/// picture check first — withheld where it is unfit for a general audience, or where the check could not run — then it is
/// kept where it was asked to be, opened, and shown to the model.
/// </summary>
internal static class PictureKeeping
{
    /// <param name="check">The check while Enhanced AI Protection Mode is on; null while it is off.</param>
    /// <param name="path">Where it was asked to be saved; null to open it as a draft.</param>
    /// <param name="about">What was asked for, which names the file.</param>
    public static async Task<ToolResult> KeptAsync(GeneratedMedia made, IPictureCheck? check, GeneratedFiles files, string? path, string about, CancellationToken ct)
    {
        if (check is not null)
        {
            bool fit;
            try { fit = await check.FitForGeneralAudienceAsync(made.Media, ct); }
            catch (InvalidOperationException ex)
            {
                return ToolResult.Error("not checked", $"The picture was not shown: Enhanced AI Protection Mode checks every picture, and its check could not run. {ex.Message}");
            }
            if (!fit)
                return ToolResult.Error("withheld", "The picture was withheld: Enhanced AI Protection Mode found it unfit for a general audience. "
                                                    + "Say so briefly and calmly, and offer to draw a version that keeps to a general audience.");
        }

        var saved = await files.KeepAsync(made.Media, MadeKind.Picture, path, about, ct);
        return GeneratedFiles.Made(saved, made.Note, new ContextImage(made.Media.Bytes, made.Media.MimeType, System.IO.Path.GetFileName(saved.Path)));
    }
}
