using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace Nexaflow.Features.Git.Services;

/// <summary>A username/password pair resolved from the system git credential helper.</summary>
public sealed record GitCredential(string Username, string Password);

/// <summary>
/// Bridges LibGit2Sharp's <see cref="CredentialsHandler"/> to the <b>system Git Credential Manager</b> by
/// shelling out to <c>git credential fill|approve|reject</c>. LibGit2Sharp does not consult the git credential
/// helper on its own, so a fetch/pull against an authenticating remote (e.g. Bitbucket) throws
/// "remote authentication required but no callback set" unless a provider is supplied — this is that provider.
/// <para>
/// Per-repository keying is <b>automatic</b>: <see cref="Fill"/> hands git the full <c>url=</c> and lets git
/// decompose it and apply the user's own <c>credential.*</c> config (including per-remote
/// <c>credential.useHttpPath</c>). That is exactly why the user's different-token-per-repo setup already works
/// at the CLI — this reuses it rather than storing tokens ourselves.
/// </para>
/// </summary>
public sealed class GitCredentialHelper
{
    /// <summary>Kill the helper and give up if it produces no result within this long (guards a would-be
    /// interactive prompt when the store is empty — the normal stored-credential path returns instantly).</summary>
    private const int TimeoutMs = 15_000;

    private readonly string _workingDir;

    /// <summary>Runs <c>git credential &lt;subcommand&gt;</c> with <paramref name="stdinBlock"/> on stdin and
    /// returns stdout, or <c>null</c> when git could not be started (not on PATH) or timed out. Injectable so
    /// the protocol logic is unit-testable without a real git.</summary>
    private readonly Func<string, string, string?> _run;

    public GitCredentialHelper(string workingDir) : this(workingDir, run: null) { }

    internal GitCredentialHelper(string workingDir, Func<string, string, string?>? run)
    {
        _workingDir = workingDir;
        _run        = run ?? ((sub, stdin) => RunProcessCredential("git", _workingDir, sub, stdin, TimeoutMs));
    }

    /// <summary>Resolves credentials for a full remote URL via <c>git credential fill</c>. Returns <c>null</c>
    /// when git is absent or the helper produced no usable username/password (→ caller falls back).</summary>
    public GitCredential? Fill(string remoteUrl)
    {
        var stdout = _run("fill", BuildFillInput(remoteUrl));
        if (stdout is null) return null;

        var fields = ParseCredentialOutput(stdout);
        return fields.TryGetValue("username", out var user)
            && fields.TryGetValue("password", out var pass)
            && !string.IsNullOrEmpty(pass)
            ? new GitCredential(user, pass)
            : null;
    }

    /// <summary>Persists a credential into the system store (<c>git credential approve</c>). Used only by the
    /// fallback that captured a brand-new token — the normal path never writes, since GCM already holds it.</summary>
    public void Approve(string remoteUrl, GitCredential cred)
        => _run("approve", BuildStoreInput(remoteUrl, cred));

    /// <summary>Erases a stored credential (<c>git credential reject</c>). Deliberately <b>not</b> wired into the
    /// pull-failure path: a transient auth error would otherwise nuke a token that works fine at the CLI.</summary>
    public void Reject(string remoteUrl, GitCredential cred)
        => _run("reject", BuildStoreInput(remoteUrl, cred));

    /// <summary>The LibGit2Sharp adapter — assign to <c>FetchOptions.CredentialsProvider</c>.</summary>
    public CredentialsHandler Provider => (url, _, types) =>
    {
        // SSH / Kerberos etc.: don't force a username/password — let libgit2 use its own path (ssh-agent).
        if ((types & SupportedCredentialTypes.UsernamePassword) == 0)
            return new DefaultCredentials();

        var cred = Fill(url);
        // Null → no credential available. Returning DefaultCredentials lets the fetch fail with the server's
        // real 401 rather than the opaque "no callback set", and the caller then runs its fallback.
        return cred is null
            ? new DefaultCredentials()
            : new UsernamePasswordCredentials { Username = cred.Username, Password = cred.Password };
    };

    // ── Wire protocol (git credential ⇄ stdin/stdout) ─────────────────────────

    /// <summary>The stdin block for <c>git credential fill</c>: a single <c>url=</c> line, blank-line terminated.
    /// Feeding the whole URL (not pre-decomposed fields) delegates all keying/decomposition to git itself.</summary>
    internal static string BuildFillInput(string remoteUrl)
        => $"url={remoteUrl}\n\n";

    /// <summary>The stdin block for <c>approve</c>/<c>reject</c>: url plus the resolved username/password.</summary>
    internal static string BuildStoreInput(string remoteUrl, GitCredential cred)
        => $"url={remoteUrl}\nusername={cred.Username}\npassword={cred.Password}\n\n";

    /// <summary>Parses a <c>git credential</c> stdout block (<c>key=value</c> per line, first blank line ends the
    /// block). Splits on the first <c>=</c> so values may contain <c>=</c>; extra keys (e.g. password_expiry_utc)
    /// are ignored. Tolerant of both LF and CRLF.</summary>
    internal static IReadOnlyDictionary<string, string> ParseCredentialOutput(string stdout)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) break;                 // blank line terminates the credential block
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;                        // no key, or a stray line — skip
            fields[line[..eq]] = line[(eq + 1)..];
        }
        return fields;
    }

    // ── Subprocess ────────────────────────────────────────────────────────────

    /// <summary>Runs <c>&lt;exe&gt; credential &lt;subcommand&gt;</c>, feeding <paramref name="stdinBlock"/> on stdin,
    /// and returns stdout — or <c>null</c> if the process can't start (exe not on PATH → <see cref="Win32Exception"/>)
    /// or doesn't finish within <paramref name="timeoutMs"/>. Static + exe-parameterised so both the happy path and
    /// the exe-absent path are unit-testable without a real git.</summary>
    internal static string? RunProcessCredential(string exe, string workingDir, string subcommand, string stdinBlock, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory       = workingDir,           // so the repo's local credential.* config applies
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        psi.ArgumentList.Add("credential");
        psi.ArgumentList.Add(subcommand);
        // Never let git block on an interactive terminal prompt inside this headless subprocess.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return null;
        }
        catch (Win32Exception) { return null; }            // git not on PATH
        catch (InvalidOperationException) { return null; }

        try
        {
            proc.StandardInput.Write(stdinBlock);
            proc.StandardInput.Close();

            // Read async so the TimeoutMs guard holds even if git/helper wedges (a sync ReadToEnd would block
            // forever). Drain stderr too, so a chatty helper can't deadlock on a full stderr pipe.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();

            if (!stdoutTask.Wait(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }

            try { proc.WaitForExit(2_000); } catch { /* ignore */ }
            return stdoutTask.Result;
        }
        catch (Exception)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return null;
        }
    }
}
