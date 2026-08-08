using System;
using System.IO;
using System.Threading;
using Nexaflow.IO.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Logging;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// One opened PDF, plus the two things that have to be torn down with it: the stream it was read from and
/// the cancellation hook that kills that stream.
/// <para>
/// PdfPig is entirely synchronous, so a <see cref="CancellationToken"/> can't interrupt it directly — the
/// parse would run to completion long after the caller stopped caring. Disposing the underlying stream is
/// the lever that does work: the next read inside PdfPig throws and the whole parse unwinds. That matters
/// because the search sweep reads candidates one after another, so one wedged file delays every file behind
/// it, and the tab that started the sweep may already be closed.
/// </para>
/// </summary>
internal sealed class PdfDocumentScope : IDisposable
{
    private readonly Stream _stream;
    private readonly CancellationTokenRegistration _cancelHook;

    public PdfDocument Document { get; }

    private PdfDocumentScope(PdfDocument document, Stream stream, CancellationTokenRegistration cancelHook)
    {
        Document    = document;
        _stream     = stream;
        _cancelHook = cancelHook;
    }

    /// <summary>
    /// Opens <paramref name="path"/> for reading, or returns null when it isn't a PDF this can make sense of
    /// — corrupt, encrypted with a password we don't have, locked, or gone. Null means "couldn't tell",
    /// which callers must keep distinct from "read it, there was no text".
    /// <para>
    /// Read through the VFS rather than <see cref="File"/> so a PDF inside an archive works too: for a real
    /// path this is just a <c>FileStream</c>, and for a virtual one the entry is materialised first.
    /// </para>
    /// </summary>
    public static PdfDocumentScope? TryOpen(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Stream? stream = null;
        var cancelHook = default(CancellationTokenRegistration);
        try
        {
            stream = VirtualFileSystem.Instance.OpenRead(path);

            // Registered before the parse starts, so a token cancelled during Open() still bites.
            var toKill = stream;
            cancelHook = ct.Register(() => { try { toKill.Dispose(); } catch { } });

            var document = PdfDocument.Open(stream, new ParsingOptions
            {
                // The 90%-of-real-PDFs setting. Plenty of files in the wild have a broken cross-reference
                // table or a font the writer never embedded; strict parsing rejects them outright, lenient
                // parsing recovers by scanning for objects and carries on.
                UseLenientParsing = true,
                SkipMissingFonts  = true,

                // "Encrypted with an owner password but no user password" is common — a document marked
                // no-copy that any reader still opens. An empty password unlocks exactly those and nothing
                // it shouldn't: a PDF that genuinely needs a secret still throws.
                Password = string.Empty,

                // Nothing here draws the page, so clipping work is pure cost.
                ClipPaths = false,

                FilterProvider = PdfFilterProvider.Instance,
                Logger         = SilentLog.Instance,
            });

            return new PdfDocumentScope(document, stream, cancelHook);
        }
        catch (OperationCanceledException)
        {
            cancelHook.Dispose();
            stream?.Dispose();
            throw;
        }
        catch
        {
            // Every other failure — encrypted, malformed beyond recovery, locked, denied — is "not readable",
            // not a crash. The caller falls back to scanning the raw bytes.
            cancelHook.Dispose();
            stream?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        // The hook first: it holds a reference to the stream and must not fire mid-teardown.
        _cancelHook.Dispose();
        try { Document.Dispose(); } catch { }
        try { _stream.Dispose(); } catch { }
    }

    /// <summary>PdfPig narrates recoverable damage at Debug/Warn. On a sweep over thousands of files that is
    /// noise, and it is never actionable — the outcome is already in the return value.</summary>
    private sealed class SilentLog : ILog
    {
        public static SilentLog Instance { get; } = new();
        public void Debug(string message) { }
        public void Debug(string message, Exception ex) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
    }
}
