using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace Nexaflow.AiHost.Provisioning;

/// <summary>How far a download has got.</summary>
internal readonly record struct DownloadProgress(long Received, long Total);

/// <summary>
/// Downloads a file whose size and SHA-256 are known in advance. The bytes go to <c>&lt;destination&gt;.partial</c>,
/// which the next pass resumes from with a range request — a large model survives a dropped connection or a closed app.
/// A connection dropped, or a server failing, mid-download is resumed at once, after a pause that grows while passes gain
/// nothing; it is given up only after <see cref="FruitlessLimit"/> passes in a row gain no byte. The file takes its name
/// only once its size and hash are right; a mismatch deletes what arrived.
/// </summary>
internal static class VerifiedDownload
{
    private const int BufferSize = 1 << 16;

    /// <summary>How many passes in a row may gain nothing before the download is given up.</summary>
    public const int FruitlessLimit = 5;

    /// <exception cref="InvalidDataException">The download is complete but not the file it should be.</exception>
    /// <exception cref="HttpRequestException">The server refused it, or failed <see cref="FruitlessLimit"/> passes in a row.</exception>
    /// <exception cref="IOException">The connection dropped <see cref="FruitlessLimit"/> passes in a row, gaining nothing.</exception>
    public static async Task DownloadAsync(
        HttpClient http, Uri source, string destination, long size, string sha256,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default, TimeProvider? time = null)
    {
        string partial = destination + ".partial";
        int fruitless = 0;
        while (true)
        {
            long before = new FileInfo(partial) is { Exists: true } kept ? kept.Length : 0;
            try
            {
                await PassAsync(http, source, destination, size, sha256, progress, ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && Resumable(ex))
            {
                long now = new FileInfo(partial) is { Exists: true } after ? after.Length : 0;
                fruitless = now > before ? 0 : fruitless + 1;
                if (fruitless >= FruitlessLimit) throw;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << fruitless)), time ?? TimeProvider.System, ct);
            }
        }
    }

    /// <summary>Whether the next pass may get further: a dropped connection or a failing server — not one that refuses the request,
    /// nor a file that is not the one it should be.</summary>
    private static bool Resumable(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => (int)status >= 500,
        IOException => true,
        _ => false,
    };

    /// <summary>One pass: what the partial holds is checked into the hash, and the rest asked for.</summary>
    private static async Task PassAsync(
        HttpClient http, Uri source, string destination, long size, string sha256, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = new FileInfo(partial) is { Exists: true } existing ? existing.Length : 0;
        if (received > size) { File.Delete(partial); received = 0; }

        HttpResponseMessage? response = null;
        try
        {
            // A partial that already holds every byte needs only checking; anything less asks for the rest.
            if (received < size)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                if (received > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(received, null);

                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (received > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    received = 0;   // the server will not resume: start again from its first byte
                response.EnsureSuccessStatusCode();
            }

            await using var file = new FileStream(partial, received > 0 ? FileMode.Open : FileMode.Create,
                                                  FileAccess.ReadWrite, FileShare.None, BufferSize, useAsync: true);

            // What an earlier attempt kept is part of the hash too.
            var buffer = new byte[BufferSize];
            for (long left = received; left > 0;)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                left -= read;
            }
            file.SetLength(received);
            file.Position = received;
            progress?.Report(new DownloadProgress(received, size));

            if (response is not null)
            {
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                int n;
                while ((n = await body.ReadAsync(buffer, ct)) > 0)
                {
                    if (received + n > size) throw Mismatch(partial, source, "more bytes than the file has");
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    hash.AppendData(buffer, 0, n);
                    received += n;
                    progress?.Report(new DownloadProgress(received, size));
                }
            }
        }
        finally { response?.Dispose(); }
        if (received != size) throw Mismatch(partial, source, $"{received:N0} of {size:N0} bytes");

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            throw Mismatch(partial, source, "a different SHA-256");

        File.Move(partial, destination, overwrite: true);
    }

    private static InvalidDataException Mismatch(string partial, Uri source, string what)
    {
        try { File.Delete(partial); } catch (IOException) { /* the next attempt replaces it */ }
        return new InvalidDataException($"The download from {source} is not the file it should be: {what}.");
    }
}

/// <summary>A file's SHA-256 as the manifests write it: lower-case hex.</summary>
internal static class FileHash
{
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
    }
}
