using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Email.Model;

namespace Nexaflow.Features.Email.Reading;

/// <summary>
/// The single entry point for parsing an email: picks the format reader by extension and caches the parsed
/// <see cref="EmailDocument"/> in a small MRU keyed by the underlying real file's identity
/// (<c>path | last-write | length</c>). One <c>HandleObject(mail.eml\attachment)</c> drives ~6 VFS calls
/// that each reopen an archive session, so without the cache the whole message is re-parsed each time; the
/// MRU collapses those to one parse. Thread-safe.
/// </summary>
internal sealed class EmailDocumentReader
{
    public static EmailDocumentReader Instance { get; } = new();

    private readonly IEmailFormatReader _eml = new MimeKitEmailReader();
    private readonly IEmailFormatReader _msg = new MsgEmailReader();

    private const int Capacity = 4;
    private readonly object _lock = new();
    private readonly LinkedList<(string Key, EmailDocument Doc)> _mru = new();

    /// <summary>Parses <paramref name="stream"/> (assumed positioned at 0 and seekable). Caches the result
    /// when the stream is a <see cref="FileStream"/> — which the VFS always hands us, whether the email is a
    /// real file or the materialised temp copy of one inside an archive. Does not dispose the stream.</summary>
    public EmailDocument Read(Stream stream, string fileName)
    {
        if (stream is FileStream fs && TryKey(fs, out var key))
        {
            if (TryGet(key, out var cached)) return cached;
            var parsed = Parse(stream, fileName);
            Put(key, parsed);
            return parsed;
        }
        return Parse(stream, fileName);
    }

    private EmailDocument Parse(Stream stream, string fileName)
        => (EmailFileTypes.IsMsg(fileName) ? _msg : _eml).Read(stream, fileName);

    private static bool TryKey(FileStream fs, out string key)
    {
        key = string.Empty;
        try
        {
            var name = fs.Name;
            if (string.IsNullOrEmpty(name) || !File.Exists(name)) return false;
            var fi = new FileInfo(name);
            key = $"{name}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
            return true;
        }
        catch { return false; }
    }

    private bool TryGet(string key, out EmailDocument doc)
    {
        lock (_lock)
        {
            for (var n = _mru.First; n is not null; n = n.Next)
                if (n.Value.Key == key)
                {
                    _mru.Remove(n);
                    _mru.AddFirst(n);
                    doc = n.Value.Doc;
                    return true;
                }
        }
        doc = null!;
        return false;
    }

    private void Put(string key, EmailDocument doc)
    {
        lock (_lock)
        {
            for (var n = _mru.First; n is not null; n = n.Next)
                if (n.Value.Key == key) { _mru.Remove(n); break; }
            _mru.AddFirst((key, doc));
            while (_mru.Count > Capacity) _mru.RemoveLast();
        }
    }
}
