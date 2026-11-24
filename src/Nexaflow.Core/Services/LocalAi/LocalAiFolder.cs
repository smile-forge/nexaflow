using System.IO;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// Where Nexaflow keeps what it notes of its local models' use — and, where it kept their files before the AI host kept them, the
/// folder the host moves them in from.
/// </summary>
internal static class LocalAiFolder
{
    /// <summary><c>NEXAFLOW_AI_DIR</c>, or <c>%LOCALAPPDATA%\Smile\nexaflow\ai</c> — not the roaming profile, which would copy
    /// what is kept here to every machine a user signs in to.</summary>
    public static string Root() => Configured() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "ai");

    /// <summary><c>NEXAFLOW_AI_DIR</c> where it is set: the folder the AI host keeps its model store in for this app — a test's, a
    /// second drive — rather than the machine's own; null where it is not.</summary>
    public static string? Configured() => Environment.GetEnvironmentVariable("NEXAFLOW_AI_DIR") is { Length: > 0 } dir ? dir : null;

    /// <summary>Deletes what the retired whisper.cpp provider kept in <see cref="Root"/>: nothing reads it, and no catalog names it for
    /// the host to move in. A file still held open stays, and the next launch tries again.</summary>
    public static void RetireWhisperCpp()
    {
        if (Configured() is not null) return;   // a configured folder is the host's store, not Nexaflow's
        try
        {
            var whisper = Path.Combine(Root(), "whisper");
            if (Directory.Exists(whisper)) Directory.Delete(whisper, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
