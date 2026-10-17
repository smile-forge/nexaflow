using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Security;

namespace Nexaflow.IO.Usb.Naming;

/// <summary>What the store remembers between runs about the downloaded copy and the last time it looked.</summary>
public sealed record UsbIdsCheckState(
    DateTimeOffset? LastChecked, DateTimeOffset? LastFailure, string? FailureMessage, string? ETag, DateTimeOffset? LastModified)
{
    public static readonly UsbIdsCheckState None = new(null, null, null, null, null);
}

public enum UsbIdsUpdateOutcome
{
    /// <summary>A newer database was downloaded and is now <see cref="UsbIdsStore.Current"/>.</summary>
    Updated,
    /// <summary>linux-usb.org had nothing newer than the copy in use.</summary>
    AlreadyCurrent,
    /// <summary>The download failed or was not a usable database; the copy in use is unchanged.</summary>
    Failed,
}

public sealed record UsbIdsUpdateResult(UsbIdsUpdateOutcome Outcome, string? Version, string Message);

/// <summary>
/// The usb.ids database Nexaflow names devices with: the copy bundled at build time, or a newer one downloaded
/// from linux-usb.org, whichever carries the later <c># Version:</c>.
/// </summary>
/// <remarks>
/// <para>
/// The download is taken under the file's BSD-3-Clause option (linux-usb.org offers GPL-2.0-or-later or
/// BSD-3-Clause), which is how the bundled copy is used too.
/// </para>
/// <para>
/// One store per user, shared by every page and the Options editor in the process, so an update made from one
/// is seen by all of them through <see cref="Changed"/>. The check is a conditional GET — an unchanged file
/// costs linux-usb.org a 304 — and a download replaces the copy in use only after it parsed, carries a
/// version, names a plausible number of vendors, and is newer. The file is written to a temporary name and
/// moved into place, so a crash mid-write never leaves a half file to load.
/// </para>
/// </remarks>
public sealed class UsbIdsStore
{
    public static readonly Uri Source = new("https://www.linux-usb.org/usb.ids");

    /// <summary>
    /// The same file over plain HTTP, used only when <see cref="Source"/>'s certificate is valid in every respect but
    /// the host name — linux-usb.org shares a host whose certificate names another site. The download is checked as
    /// strictly either way; HTTP only loses the assurance that nobody on the path changed the names in it.
    /// </summary>
    public static readonly Uri PlainSource = new("http://www.linux-usb.org/usb.ids");

    /// <summary>How long a successful check stands before the page looks again on its own.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(30);

    /// <summary>How long a failed check waits before an automatic retry, so an offline machine is not asked every open.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    /// <summary>Fewer vendors than this is a truncated or substituted file, not a database.</summary>
    const int MinimumVendors = 1000;

    const long MaximumBytes = 32L * 1024 * 1024;

    static readonly Lazy<UsbIdsStore> UserStore = new(() => new UsbIdsStore(DefaultDirectory(), UsbIdsDatabase.LoadBundled));

    readonly Func<UsbIdsDatabase?> _bundled;
    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly object _startLock = new();
    readonly Lazy<UsbIdsDatabase?> _initial;
    volatile UsbIdsDatabase? _replaced;
    Task<UsbIdsUpdateResult>? _running;
    readonly Func<HttpRequestException, bool> _nameMismatchOnly;

    /// <summary>The <see cref="SslPolicyErrors"/> of the last HTTPS handshake the default handler made.</summary>
    int _tlsErrors;

    public UsbIdsStore(string directory, Func<UsbIdsDatabase?> bundled, HttpMessageHandler? handler = null, TimeProvider? time = null)
        : this(directory, bundled, handler, time, nameMismatchOnly: null)
    {
    }

    /// <param name="nameMismatchOnly">
    /// Whether a failed HTTPS request failed on the certificate's host name and nothing else. Left null, the default
    /// handler's own record of the handshake decides; a test with its own handler says.
    /// </param>
    internal UsbIdsStore(string directory, Func<UsbIdsDatabase?> bundled, HttpMessageHandler? handler, TimeProvider? time,
        Func<HttpRequestException, bool>? nameMismatchOnly)
    {
        Directory = directory;
        _bundled = bundled;
        _time = time ?? TimeProvider.System;
        _nameMismatchOnly = nameMismatchOnly ?? (_ => (SslPolicyErrors)Volatile.Read(ref _tlsErrors) == SslPolicyErrors.RemoteCertificateNameMismatch);
        _http = new HttpClient(handler ?? DefaultHandler(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nexaflow", "1.0"));
        _initial = new(LoadNewest, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Validates certificates as the platform does, and remembers why one failed so the fallback can be that narrow.</summary>
    SocketsHttpHandler DefaultHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        SslOptions =
        {
            RemoteCertificateValidationCallback = (_, _, _, errors) =>
            {
                Volatile.Write(ref _tlsErrors, (int)errors);
                return errors == SslPolicyErrors.None;
            },
        },
    };

    /// <summary>The store for this Windows user, under <c>%LOCALAPPDATA%\Smile\nexaflow\usb</c>.</summary>
    public static UsbIdsStore ForUser => UserStore.Value;

    public string Directory { get; }
    public string DatabasePath => Path.Combine(Directory, "usb.ids");
    public string StatePath => Path.Combine(Directory, "usb.ids.state.json");

    /// <summary>The database in use: the newer of the bundled and downloaded copies, read once and then kept.</summary>
    public UsbIdsDatabase? Current => _replaced ?? _initial.Value;

    /// <summary>True when <see cref="Current"/> is a downloaded copy rather than the one that shipped.</summary>
    public bool CurrentIsDownloaded => Current is { } current && !ReferenceEquals(current, _bundled());

    /// <summary>Raised, on the thread that ran the update, after <see cref="Current"/> was replaced.</summary>
    public event EventHandler? Changed;

    public UsbIdsCheckState State => ReadState();

    /// <summary>
    /// True when the page should look for a newer copy on its own: never checked, or the last success is older
    /// than <see cref="CheckInterval"/> — unless a check failed within <see cref="RetryInterval"/>.
    /// </summary>
    public bool IsCheckDue()
    {
        var state = ReadState();
        var now = _time.GetUtcNow();
        if (state.LastFailure is { } failed && now - failed < RetryInterval) return false;
        return state.LastChecked is not { } checkedAt || now - checkedAt > CheckInterval;
    }

    /// <summary>
    /// Asks linux-usb.org for a newer database and adopts it if there is one. Calls that overlap share one
    /// download — the first caller's token cancels it — so a page opening while Options is checking does not ask twice.
    /// </summary>
    public Task<UsbIdsUpdateResult> UpdateAsync(CancellationToken ct)
    {
        lock (_startLock)
        {
            if (_running is { IsCompleted: false } running) return running;
            return _running = UpdateCoreAsync(ct);
        }
    }

    async Task<UsbIdsUpdateResult> UpdateCoreAsync(CancellationToken ct)
    {
        try
        {
            var state = ReadState();
            var current = Current;
            bool haveDownload = File.Exists(DatabasePath);
            HttpResponseMessage sent;
            string via = "";
            try
            {
                Volatile.Write(ref _tlsErrors, (int)SslPolicyErrors.None);
                sent = await SendAsync(Source, state, haveDownload, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException e) when (_nameMismatchOnly(e))
            {
                sent = await SendAsync(PlainSource, state, haveDownload, ct).ConfigureAwait(false);
                via = " (over HTTP: linux-usb.org's certificate names another host)";
            }
            using var response = sent;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                WriteState(state with { LastChecked = _time.GetUtcNow(), LastFailure = null, FailureMessage = null });
                return new(UsbIdsUpdateOutcome.AlreadyCurrent, current?.Version, $"usb.ids {current?.Version} is current{via}.");
            }
            if (!response.IsSuccessStatusCode)
                return Failed(state, $"linux-usb.org answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            if (response.Content.Headers.ContentLength > MaximumBytes)
                return Failed(state, $"linux-usb.org offered {response.Content.Headers.ContentLength} bytes, more than a usb.ids file can be.");

            byte[] body = await ReadBoundedAsync(response.Content, ct).ConfigureAwait(false);
            UsbIdsDatabase downloaded;
            using (var stream = new MemoryStream(body, writable: false)) downloaded = UsbIdsDatabase.Load(stream);
            if (downloaded.Version is null || downloaded.VendorCount < MinimumVendors)
                return Failed(state, $"The download is not a usb.ids database ({downloaded.VendorCount} vendors, version {downloaded.Version ?? "missing"}).");

            var checkedState = state with
            {
                LastChecked = _time.GetUtcNow(), LastFailure = null, FailureMessage = null,
                ETag = response.Headers.ETag?.ToString(), LastModified = response.Content.Headers.LastModified,
            };
            if (!IsNewer(downloaded, current))
            {
                WriteState(checkedState);
                return new(UsbIdsUpdateOutcome.AlreadyCurrent, current?.Version, $"usb.ids {current?.Version} is current{via}.");
            }

            WriteAtomically(DatabasePath, body);
            WriteState(checkedState);
            _replaced = downloaded;
            Changed?.Invoke(this, EventArgs.Empty);
            return new(UsbIdsUpdateOutcome.Updated, downloaded.Version, $"usb.ids updated to {downloaded.Version}{via}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            // The innermost message says what actually went wrong — "the remote certificate is invalid…
            // RemoteCertificateNameMismatch" — where the outer one only says to look inside.
            return Failed(ReadState(), e.GetBaseException().Message);
        }
    }

    /// <summary>A conditional GET: a download already on disk is asked about by its ETag and date, so an unchanged file is a 304.</summary>
    Task<HttpResponseMessage> SendAsync(Uri uri, UsbIdsCheckState state, bool haveDownload, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (haveDownload && state.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
        if (haveDownload && state.LastModified is { } modified) request.Headers.IfModifiedSince = modified;
        return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    UsbIdsUpdateResult Failed(UsbIdsCheckState state, string message)
    {
        WriteState(state with { LastFailure = _time.GetUtcNow(), FailureMessage = message });
        return new(UsbIdsUpdateOutcome.Failed, Current?.Version, message);
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        for (int read; (read = await source.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0;)
        {
            if (buffer.Length + read > MaximumBytes) throw new IOException("The download is larger than a usb.ids file can be.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Versions are dates ("2026.06.26"), so ordinal order is date order.</summary>
    static bool IsNewer(UsbIdsDatabase candidate, UsbIdsDatabase? current) =>
        current?.Version is not { } version || string.CompareOrdinal(candidate.Version, version) > 0;

    UsbIdsDatabase? LoadNewest()
    {
        var bundled = _bundled();
        UsbIdsDatabase? downloaded = null;
        try
        {
            if (File.Exists(DatabasePath))
                using (var stream = File.OpenRead(DatabasePath)) downloaded = UsbIdsDatabase.Load(stream);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (downloaded is null || downloaded.Version is null || downloaded.VendorCount < MinimumVendors) return bundled;
        return bundled is null || IsNewer(downloaded, bundled) ? downloaded : bundled;
    }

    UsbIdsCheckState ReadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(StatePath), UsbIdsStateJsonContext.Default.UsbIdsCheckState) ?? UsbIdsCheckState.None
                : UsbIdsCheckState.None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return UsbIdsCheckState.None;
        }
    }

    void WriteState(UsbIdsCheckState state) =>
        WriteAtomically(StatePath, JsonSerializer.SerializeToUtf8Bytes(state, UsbIdsStateJsonContext.Default.UsbIdsCheckState));

    void WriteAtomically(string path, byte[] bytes)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nexaflow", "usb");
}

[JsonSerializable(typeof(UsbIdsCheckState))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal sealed partial class UsbIdsStateJsonContext : JsonSerializerContext;
