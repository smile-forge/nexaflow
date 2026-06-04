using System.IO;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>Best-effort on-disk folder sizing, shared by the second (size-filling) load pass.</summary>
public static class FolderSize
{
    /// <summary>Sums every file under <paramref name="path"/>. Returns null if it can't be walked.</summary>
    public static long? Measure(string path, CancellationToken ct)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try { total += new FileInfo(file).Length; } catch { /* skip unreadable file */ }
            }
            return total;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}
