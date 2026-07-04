using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexaflow.Providers.Common;

/// <summary>
/// SDK-agnostic helpers for turning Nexaflow's neutral <see cref="LlmMessage"/> list into the shape every
/// provider needs before handing off to its own SDK: splitting the leading system prompt, partitioning a
/// message's attachments into images vs. other files, and appending the non-image file list to the prompt
/// text. Each provider previously carried its own copy of this logic; centralising it keeps the four
/// backends in lock-step and makes the prep independently testable.
/// </summary>
public static class PromptComposer
{
    /// <summary>
    /// If the first message carries <see cref="LlmRole.System"/>, returns its text plus the remaining
    /// turns; otherwise returns <c>(null, messages)</c>. Used by providers whose API takes the system
    /// prompt out-of-band (Claude, Gemini).
    /// </summary>
    public static (string? System, IReadOnlyList<LlmMessage> Turns) SplitSystemPrompt(IReadOnlyList<LlmMessage> messages)
        => messages.Count > 0 && messages[0].Role == LlmRole.System
            ? (messages[0].Text, messages.Skip(1).ToList())
            : (null, messages);

    /// <summary>The image attachments and the non-image attachments, each in original order. Both lists
    /// are empty when <paramref name="attachments"/> is null or empty.</summary>
    public static (IReadOnlyList<LlmAttachment> Images, IReadOnlyList<LlmAttachment> Files) PartitionAttachments(
        IReadOnlyList<LlmAttachment>? attachments)
        => attachments is null || attachments.Count == 0
            ? ([], [])
            : (attachments.Where(a => a.IsImage).ToList(), attachments.Where(a => !a.IsImage).ToList());

    /// <summary>
    /// Appends an "Attached files:" block listing each file's path to <paramref name="prompt"/>. Returns
    /// the prompt unchanged when there are no files (the path text-fallback for non-image attachments on
    /// every provider, and the only attachment handling on text-only backends).
    /// </summary>
    public static string AppendFileList(string prompt, IReadOnlyList<LlmAttachment> files)
    {
        if (files.Count == 0) return prompt;

        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var a in files)
            sb.AppendLine($"  {a.FilePath}");
        return sb.ToString();
    }
}
