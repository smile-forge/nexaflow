using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Executable.FileActions;

/// <summary>
/// "AV Scan" — runs the file through AMSI, i.e. through whichever antivirus engine is actually
/// registered on this machine.
/// <para>
/// Offered universally (<c>ExperienceId "/"</c>) because AMSI is content-agnostic: scanning a
/// downloaded document or script is every bit as useful as scanning a binary, and this mirrors
/// Explorer's own antivirus item being available on anything. It reports a result rather than
/// opening a tab, so it is not a viewer and needs no filemap entry of its own — "/" is already
/// mapped by the universal <c>*.*</c> rule.
/// </para>
/// </summary>
public sealed class AvScanAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/";

    public string ExperienceId => "/";
    public string ExperienceDescription =>
        "Scan the file with the antivirus engine registered on this machine (via AMSI).";

    public string DisplayName => Str.Get("Executable.Inspector.AVScan");
    public string Icon        => "🛡";
    public string? Tooltip    => Str.Get("Executable.Inspector.ScanThisFileWithTheRegistered");

    public bool IsDestructive         => false;
    public bool SupportsMultipleFiles => true;
    public bool RequiresRefresh       => false;
    public bool CanPerformAction      => true;

    /// <summary>Shows a result, not a tab — so it stays out of the viewer-reachability rules.</summary>
    public bool OpensViewer => false;

    public bool PerformAction(string filePath) => PerformAction([filePath]);

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var paths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paths.Count == 0) return false;

        // The provider does real work and can take seconds on a large file; never on the dispatcher.
        shell.QueueBackgroundTask(new ScanTask(shell, paths));
        return true;
    }

    private sealed class ScanTask(IShellServices shell, IReadOnlyList<string> paths) : IBackgroundTask
    {
        public string Description => paths.Count == 1
            ? Str.Format("Executable.Av.ScanningOneFormat", Path.GetFileName(paths[0]))
            : Str.Format("Executable.Av.ScanningManyFormat", paths.Count);

        public async Task RunAsync(CancellationToken ct)
        {
            var threats  = new List<string>();
            var unusable = new List<string>();
            int clean    = 0;

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                var result = await Task.Run(() => AmsiScanner.ScanFile(path, ct), ct);
                string name = Path.GetFileName(path);

                if (result.IsThreat)          threats.Add($"{name}: {result.Message}");
                else if (result.Verdict == AmsiVerdict.Unavailable) unusable.Add($"{name}: {result.Message}");
                else                          clean++;
            }

            await shell.RunOnUiAsync(() => Report(threats, unusable, clean));
        }

        private void Report(List<string> threats, List<string> unusable, int clean)
        {
            if (threats.Count > 0)
            {
                shell.ShowError(threats.Count == 1
                    ? Str.Format("Executable.Av.ThreatFoundFormat", threats[0])
                    : Str.Format("Executable.Av.ThreatsFoundFormat", threats.Count, string.Join("\n", threats)));
                return;
            }

            // Never let "nothing scanned it" read as "nothing is wrong".
            if (unusable.Count > 0 && clean == 0)
            {
                shell.ShowError(unusable.Count == 1
                    ? Str.Format("Executable.Av.CouldNotScanFormat", unusable[0])
                    : Str.Format("Executable.Av.CouldNotScanManyFormat", unusable.Count));
                return;
            }

            string message = clean == 1 ? Str.Get("Executable.Av.NoThreat") : Str.Format("Executable.Av.NoThreatsFormat", clean);
            if (unusable.Count > 0) message = Str.Format("Executable.Av.UnscannedFormat", message, unusable.Count);
            shell.ShowNotification(message);
        }
    }
}
