using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions
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
        public string DisplayName            => Str.Get("WindowsFileSystem.Actions.Run");
        public static string? StaticExperienceId => "/binary/executable";
        public string ExperienceId           => "/binary/executable";
        public string ExperienceDescription  => Str.Get("WindowsFileSystem.Experiences.Executables");
        public bool   RequiresRefresh        => false;
        public bool   CanPerformAction       => true;

        /// <summary>An executable needs whatever sits beside it — DLLs, config, data. Extracting it
        /// alone from an archive would launch something that cannot work, so don't offer to.</summary>
        public bool   RequiresFullyBackedPath => true;

        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
        {
            var real = VirtualFileSystem.Instance.TryResolveReal(filePath) ?? filePath;
            Process.Start(new ProcessStartInfo(real) { UseShellExecute = true });
            return Task.FromResult(true);
        }

        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        {
            foreach (var p in filePaths) return PerformActionAsync(p, ct);
            return Task.FromResult(false);
        }
    }
}
