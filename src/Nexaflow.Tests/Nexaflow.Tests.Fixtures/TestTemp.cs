using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Where a test host keeps the files it makes. Every test asks here instead of <see cref="Path.GetTempPath"/>,
/// so nothing a test writes lands loose in the machine's <c>%TEMP%</c>: a test host gets one folder of its own,
/// <c>%TEMP%\nexaflow-tests\&lt;suite&gt;-&lt;pid&gt;-&lt;id&gt;</c>, and the folder goes when the host exits —
/// whether or not the test that filled it remembered to clean up. <c>TempHygieneRules</c> forbids the raw
/// temp APIs in test code, so this cannot be bypassed by accident.
/// <para>
/// A host that dies without exiting (killed, crashed, debugger stopped) cannot delete its folder, so each folder
/// is paired with a lock file the host holds open for its whole life. The next host to start deletes every
/// folder whose lock it can take, because nothing alive holds it. That makes the clean-up a property of the
/// layout rather than of the host's last moments: a run that died is cleaned by the next one.
/// </para>
/// <para>
/// The folder is claimed on first use and released on <see cref="AppDomain.ProcessExit"/>, not from an
/// <c>[AssemblyCleanup]</c>: MSTest honours that only in the assembly under test, so it would need a shim in
/// every suite, and it runs before the host's last child processes and handles are gone.
/// </para>
/// </summary>
public static class TestTemp
{
    private static readonly Lazy<RunFolder> Run = new(RunFolder.Claim, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>This test host's folder. It exists, and is deleted with everything in it when the host exits.</summary>
    public static string Root => Run.Value.Path;

    /// <summary>
    /// A new, empty folder under <see cref="Root"/> — the stand-in for
    /// <see cref="Directory.CreateTempSubdirectory(string?)"/>, with the prefix naming the test that made it.
    /// </summary>
    public static DirectoryInfo CreateSubdirectory(string? prefix = null)
        => Directory.CreateDirectory(Path.Combine(Root, (prefix ?? "") + Guid.NewGuid().ToString("N")));

    /// <summary>
    /// A new, empty file under <see cref="Root"/> — the stand-in for <see cref="Path.GetTempFileName"/>, which
    /// it matches by creating the file and handing back its path.
    /// </summary>
    public static string CreateFile(string extension = ".tmp")
    {
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + extension);
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
        return path;
    }

    /// <summary>
    /// One host's claim on its folder. The lock file sits beside the folder rather than inside it, and is taken
    /// <i>before</i> the folder exists, so there is no moment at which a live host's folder is unguarded.
    /// </summary>
    private sealed class RunFolder
    {
        private const string HostsFolderName = "nexaflow-tests";
        private const string LockExtension = ".lock";

        private readonly string _hosts;
        private readonly FileStream _lock;

        public string Path { get; }

        private RunFolder(string hosts, string path, FileStream held)
        {
            _hosts = hosts;
            Path = path;
            _lock = held;
        }

        public static RunFolder Claim()
        {
            var hosts = System.IO.Path.Combine(System.IO.Path.GetTempPath(), HostsFolderName);
            var path = System.IO.Path.Combine(hosts, $"{SuiteName()}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}");
            var held = TakeLock(hosts, path);
            Directory.CreateDirectory(path);

            var run = new RunFolder(hosts, path, held);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => run.Release();
            // Off the test's thread: a dead run can leave a large tree, and nothing waits on it going.
            _ = Task.Run(() => SweepAbandoned(hosts, path));
            return run;
        }

        /// <summary>
        /// Holds the lock for the host's lifetime. <see cref="FileOptions.DeleteOnClose"/> means the OS removes
        /// the file however the process ends, so a lock file outlives its host only while another holds it.
        /// </summary>
        private static FileStream TakeLock(string hosts, string path)
        {
            while (true)
            {
                Directory.CreateDirectory(hosts);
                try
                {
                    return new FileStream(path + LockExtension, FileMode.CreateNew, FileAccess.ReadWrite,
                                          FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                }
                // A host exiting at this moment removed the emptied hosts folder; make it again.
                catch (DirectoryNotFoundException) { }
            }
        }

        /// <summary>Deletes every other host's folder whose lock nothing holds, which means its host is gone.</summary>
        private static void SweepAbandoned(string hosts, string own)
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(hosts))
                {
                    if (string.Equals(folder, own, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using var abandoned = new FileStream(folder + LockExtension, FileMode.OpenOrCreate,
                                                             FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
                                                             FileOptions.DeleteOnClose);
                        DeleteTree(folder);
                    }
                    catch (IOException) { }                  // held: that host is alive
                    catch (UnauthorizedAccessException) { }  // not ours to take
                }
            }
            catch (IOException) { }
        }

        private void Release()
        {
            DeleteTree(Path);
            _lock.Dispose();
            // Last host out removes the hosts folder too; any host still running keeps it non-empty.
            try { Directory.Delete(_hosts, recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Deletes a test's leftovers, including the read-only files Git writes under <c>.git\objects</c>, which
        /// a plain recursive delete refuses. Anything still open stays for the next host's sweep.
        /// </summary>
        private static void DeleteTree(string folder)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            try
            {
                var everything = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,  // a link's target is not ours to touch
                };
                foreach (var file in Directory.EnumerateFiles(folder, "*", everything))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The test project's name without its shared prefix, so a folder says which suite made it.</summary>
        private static string SuiteName()
        {
            var name = Assembly.GetEntryAssembly()?.GetName().Name
                       ?? System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            const string prefix = "Nexaflow.Tests.";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
        }
    }
}
