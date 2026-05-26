using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nexaflow.Core.FileActions
{
    /// <summary>
    /// Launches an executable (.exe) using the shell, exactly as if the user
    /// double-clicked it in Explorer (verbs, UAC prompts, etc. all apply).
    /// </summary>
    public class ExecuteFile : IFileAction, ICacheable
    {
        public bool   IsDestructive          => false;
        public bool   SupportsMultipleFiles  => false;
        public string Icon                   => "▶";
        public string DisplayName            => "Run";
        public static string? StaticExperienceId => "/binary/executable";
        public string ExperienceId           => "/binary/executable";
        public string ExperienceDescription  => "Executable files (.exe)";
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        public bool PerformAction(string filePath)
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }

        public bool PerformAction(IEnumerable<string> filePaths)
        {
            foreach (var p in filePaths) return PerformAction(p);
            return false;
        }
    }
}
