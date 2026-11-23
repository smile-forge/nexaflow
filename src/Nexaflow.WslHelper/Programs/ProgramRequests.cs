using System.Diagnostics;
using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.WslHelper.Programs;

/// <summary>
/// Runs programs for the backend as the distro's user runs them from a login shell: found by name on the <c>PATH</c> a login
/// <c>sh</c> sets, in the environment it sets, with the arguments as sent and nothing on stdin. What a program writes streams
/// back as it is written, then how it ended; a cancel ends it and every process it started.
/// </summary>
internal sealed class ProgramRequests(ReplyChannel replies)
{
    // Ends whatever a profile prints, so a greeting is not read as a variable.
    private const string Mark = "--nexaflow-environment--";

    private const string PrintEnvironment = "printf '\\n%s\\n' '" + Mark + "'; exec env -0";

    private static readonly TimeSpan LoginPatience = TimeSpan.FromSeconds(15);

    // Output that outlives its program — a process it left running, holding the pipe — is waited for this long, no longer.
    private static readonly TimeSpan TrailingOutputPatience = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private Task<IReadOnlyDictionary<string, string>>? _login;

    public async Task RunAsync(int id, string program, string folder, IReadOnlyList<string> arguments,
                               IReadOnlyList<KeyValuePair<string, string>> environment, CancellationToken ct)
    {
        var login     = await LoginEnvironmentAsync().WaitAsync(ct).ConfigureAwait(false);
        var variables = new Dictionary<string, string>(login, StringComparer.Ordinal);
        foreach (var (name, value) in environment) variables[name] = value;

        if (!Directory.Exists(folder)) throw new HelperFailure(WslFailure.NotFound, $"{folder}: no such folder");
        string file = Resolve(program, variables.GetValueOrDefault("PATH"))
                      ?? throw new HelperFailure(WslFailure.NotFound, $"{program}: not on the PATH of a login shell");

        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory       = folder,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var (name, value) in variables) start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new HelperFailure(WslFailure.Other, $"{program} did not start");
        process.StandardInput.Close();
        using var ending = ct.Register(() => End(process));

        using var pumping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pumps = Task.WhenAll(PumpAsync(id, process.StandardOutput.BaseStream, isError: false, pumping.Token),
                                 PumpAsync(id, process.StandardError.BaseStream, isError: true, pumping.Token));
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (await Task.WhenAny(pumps, Task.Delay(TrailingOutputPatience, ct)).ConfigureAwait(false) == pumps)
            await pumps.ConfigureAwait(false);
        else
            await pumping.CancelAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        await replies.SendAsync(new WslWriter(WslReply.Exited, id).Int32(process.ExitCode), ct).ConfigureAwait(false);
    }

    /// <summary>Where a program named on a <c>PATH</c> is, or null where it is on none. A name holding <c>/</c> is a path
    /// already. An empty entry, which <c>sh</c> reads as the current folder, is skipped, and so are the Windows folders WSL
    /// appends under <c>/mnt</c>: a program run on a distro's files is the distro's own, never Windows'.</summary>
    internal static string? Resolve(string program, string? path)
    {
        if (program.Contains('/')) return IsRunnable(program) ? program : null;
        foreach (var folder in (path ?? string.Empty).Split(':'))
        {
            if (folder.Length == 0 || folder.StartsWith("/mnt/", StringComparison.Ordinal)) continue;
            string candidate = Path.Combine(folder, program);
            if (IsRunnable(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsRunnable(string file)
    {
        const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        try { return File.Exists(file) && (File.GetUnixFileMode(file) & AnyExecute) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The variables after the mark in <c>env -0</c>'s output; null when there is no mark.</summary>
    internal static IReadOnlyDictionary<string, string>? ParseEnvironment(string printed)
    {
        const string Marked = "\n" + Mark + "\n";
        int at = printed.IndexOf(Marked, StringComparison.Ordinal);
        if (at < 0) return null;

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in printed[(at + Marked.Length)..].Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0) variables[pair[..equals]] = pair[(equals + 1)..];
        }
        return variables;
    }

    // Read once per helper, and again after a failure: the profile may have been mended since.
    private Task<IReadOnlyDictionary<string, string>> LoginEnvironmentAsync()
    {
        lock (_gate)
        {
            if (_login is null || _login.IsFaulted || _login.IsCanceled) _login = ReadLoginEnvironmentAsync();
            return _login;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadLoginEnvironmentAsync()
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var argument in (string[])["-lc", PrintEnvironment]) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new HelperFailure(WslFailure.Other, "a login shell did not start");
        process.StandardInput.Close();
        using var patience = new CancellationTokenSource(LoginPatience);
        try
        {
            var printed = process.StandardOutput.ReadToEndAsync(patience.Token);
            var said    = process.StandardError.ReadToEndAsync(patience.Token);
            await process.WaitForExitAsync(patience.Token).ConfigureAwait(false);
            return ParseEnvironment(await printed.ConfigureAwait(false))
                   ?? throw new HelperFailure(WslFailure.Other,
                                              $"a login shell did not say its environment: {(await said.ConfigureAwait(false)).Trim()}");
        }
        catch (OperationCanceledException)
        {
            End(process);
            throw new HelperFailure(WslFailure.Other, $"a login shell took longer than {LoginPatience.TotalSeconds:0}s to start");
        }
    }

    private async Task PumpAsync(int id, Stream from, bool isError, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                await replies.SendAsync(new WslWriter(WslReply.Output, id).Bool(isError).Bytes(buffer.AsSpan(0, read)), ct)
                             .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ended or given up on: nothing more is sent under this id.
        }
    }

    private static void End(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already ended */ }
    }
}
