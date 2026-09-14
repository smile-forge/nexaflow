using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexaflow.Services.Initiatives.Hosting.Ipc;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// One resident process per product tree. Each build of nfi talks on a pipe of its own, so the process an older build
/// started kept running beside the new one until it idled out — twenty minutes of two processes each holding its own
/// copy of the same graph, each able to answer from a copy that no longer matched the other's writes.
/// <para>
/// So a process starting up asks the processes older builds started for the same tree to stop. Politely: the request
/// is the ordinary stop, which finishes whatever is in flight and saves before it goes. Only older builds are asked, so
/// an old nfi run by mistake never ends the current one, and two cannot take turns ending each other.
/// </para>
/// </summary>
internal static class Succession
{
    /// <summary>A resident process, as it announces itself: its pipe, its process, and when its build was made.</summary>
    internal sealed record Resident(string Pipe, int ProcessId, long BuiltTicks);

    /// <summary>
    /// Records this process as a resident of <paramref name="productRoot"/>, forgets the residents that are gone, and
    /// asks the ones an older build started to stop. Returns the pipes it asked.
    /// </summary>
    public static IReadOnlyList<string> Claim(string productRoot, string pipe, string? registry = null, long? builtTicks = null)
    {
        var directory = registry ?? DirectoryFor(productRoot);
        var mine      = new Resident(pipe, Environment.ProcessId, builtTicks ?? BuiltTicks());
        var asked     = new List<string>();

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, pipe + ".json"), JsonSerializer.Serialize(mine));

            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), pipe, StringComparison.OrdinalIgnoreCase)) continue;

                var other = Read(file);
                if (other is null || !Alive(other.ProcessId))
                {
                    TryDelete(file);
                    continue;
                }
                if (other.BuiltTicks >= mine.BuiltTicks) continue;   // a newer build, or the same one: not this one's to end

                DaemonLog.Say("-", "succession", $"asking {other.Pipe}, started by an older build, to finish and stop");
                if (AskToStop(other.Pipe)) asked.Add(other.Pipe);
                else TryDelete(file);                                 // nothing answers there: its process id belongs to something else now
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Succession is a courtesy: a process that cannot record itself still serves, as every process did before.
        }

        return asked;
    }

    /// <summary>Removes this process's record, on its way out.</summary>
    public static void Leave(string productRoot, string pipe, string? registry = null) =>
        TryDelete(Path.Combine(registry ?? DirectoryFor(productRoot), pipe + ".json"));

    private static bool AskToStop(string pipe)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
            client.Connect(1_000);
            DaemonProtocol.Write(client, new DaemonRequest { Stop = true, Ticket = DaemonRequest.NewTicket() });

            // Taken is enough. Its answer comes once its own work is done, and nothing here needs to wait for that.
            return DaemonProtocol.Read<DaemonAck>(client) is not null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static Resident? Read(string file)
    {
        try { return JsonSerializer.Deserialize<Resident>(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static bool Alive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>When this build was made: the newest of the assemblies beside it, which a spawned copy keeps.</summary>
    private static long BuiltTicks()
    {
        try
        {
            return Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                            .Select(f => File.GetLastWriteTimeUtc(f).Ticks).DefaultIfEmpty(0).Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static string DirectoryFor(string productRoot)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(productRoot)).ToLowerInvariant();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nfi",
                            "residents", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8).ToLowerInvariant());
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
