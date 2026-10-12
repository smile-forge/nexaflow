using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace Nexaflow.IO.Common.Downloads;

/// <summary>How far a download has got.</summary>
public readonly record struct DownloadProgress(long Received, long Total);

/// <summary>
/// Downloads a file whose size and SHA-256 are known in advance. The bytes go to <c>&lt;destination&gt;.partial</c>,
/// which a later attempt resumes from with a range request — a large model survives a dropped connection or a
/// closed app. The file takes its name only once its size and hash are right; a mismatch deletes what arrived.
/// </summary>
public static class VerifiedDownload
{
    private const int BufferSize = 1 << 16;

    /// <exception cref="InvalidDataException">The download is complete but not the file it should be.</exception>
    public static async Task DownloadAsync(
        HttpClient http, Uri source, string destination, long size, string sha256,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
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
