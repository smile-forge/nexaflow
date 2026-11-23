using System.IO;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>Where Nexaflow keeps what its local models need — their files, the CUDA pack, what it has noted of their use.</summary>
internal static class LocalAiFolder
{
    /// <summary><c>NEXAFLOW_AI_DIR</c>, or <c>%LOCALAPPDATA%\Smile\nexaflow\ai</c> — not the roaming profile, which would copy
    /// gigabytes of weights to every machine a user signs in to. The variable moves it: tests, a second drive.</summary>
    public static string Root()
        => Environment.GetEnvironmentVariable("NEXAFLOW_AI_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "ai");
}
