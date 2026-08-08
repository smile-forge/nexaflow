using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.Features.Executable.Services;

/// <summary>What the registered antivirus made of the content.</summary>
public enum AmsiVerdict
{
    /// <summary>AMSI is not available, or no provider is registered — <b>not</b> the same as clean.</summary>
    Unavailable,
    Clean,
    /// <summary>The provider examined it and did not object, but declined to call it clean.</summary>
    NotDetected,
    /// <summary>Blocked by administrative policy rather than by detection.</summary>
    BlockedByAdmin,
    Detected,
}

/// <param name="Truncated">The file was larger than the scan cap, so only a prefix was examined.</param>
public sealed record AmsiResult(AmsiVerdict Verdict, int RawResult, string Message, bool Truncated)
{
    public bool IsThreat => Verdict is AmsiVerdict.Detected or AmsiVerdict.BlockedByAdmin;
}

/// <summary>
/// Scans content through AMSI — the platform's own "ask the registered antivirus" interface. It
/// routes to Defender or to whichever third-party engine has registered an AMSI provider, needs no
/// elevation, and runs in-process.
/// <para>
/// It reports honestly: when amsi.dll is missing or no provider answers, the verdict is
/// <see cref="AmsiVerdict.Unavailable"/>, never <see cref="AmsiVerdict.Clean"/>. Telling someone a
/// file is clean because nothing looked at it would be worse than saying nothing.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class AmsiScanner
{
    /// <summary>
    /// Ceiling on a single scan, imposed by AMSI's own API rather than by any antivirus engine:
    /// <c>AmsiScanBuffer</c> takes a 32-bit length, so one call can never cover more than 2 GB.
    /// <para>
    /// Below this the whole file is scanned. The bytes are handed over as a <em>memory-mapped
    /// view</em>, not a managed copy, so a 900 MB binary costs no heap and no read — which is why
    /// there is no arbitrary size cap here at all. A truncated scan is only ever reported for a file
    /// genuinely larger than the API can express.
    /// </para>
    /// </summary>
    public const long MaxScanBytes = int.MaxValue;

    // AMSI_RESULT: 0 clean, 1 not detected, 2..31 increasingly likely, >= 32 identified as malware.
    private const int ResultClean            = 0;
    private const int ResultNotDetected      = 1;
    private const int ResultBlockedByAdminMin = 16384;
    private const int ResultBlockedByAdminMax = 20479;
    private const int ResultDetected         = 32768;

    [LibraryImport("amsi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AmsiInitialize(string appName, out IntPtr context);

    [LibraryImport("amsi.dll")]
    private static partial void AmsiUninitialize(IntPtr context);

    [LibraryImport("amsi.dll")]
    private static partial int AmsiOpenSession(IntPtr context, out IntPtr session);

    [LibraryImport("amsi.dll")]
    private static partial void AmsiCloseSession(IntPtr context, IntPtr session);

    [LibraryImport("amsi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AmsiScanBuffer(
        IntPtr context, IntPtr buffer, uint length, string contentName, IntPtr session, out int result);

    /// <summary>
    /// Scans a file. Blocking and potentially slow — the provider does the real work — so callers
    /// must run this off the dispatcher.
    /// <para>
    /// The file is memory-mapped and the view handed straight to the provider, so nothing is copied
    /// onto the managed heap and size is not a practical constraint.
    /// </para>
    /// </summary>
    public static unsafe AmsiResult ScanFile(string path, CancellationToken ct = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new AmsiResult(AmsiVerdict.Unavailable, 0, "The file no longer exists.", false);
            if (info.Length == 0)
                return new AmsiResult(AmsiVerdict.Clean, 0, "The file is empty.", false);

            // Only a file beyond what a 32-bit length can express is genuinely out of reach.
            bool truncated = info.Length > MaxScanBytes;
            long length    = Math.Min(info.Length, MaxScanBytes);

            ct.ThrowIfCancellationRequested();

            using var file = MemoryMappedFile.CreateFromFile(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
            using var view = file.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);

            byte* pointer = null;
            try
            {
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                return Scan((IntPtr)pointer, length, Path.GetFileName(path), truncated);
            }
            finally
            {
                if (pointer is not null) view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            return new AmsiResult(AmsiVerdict.Unavailable, 0, $"The file could not be read: {e.Message}", false);
        }
    }

    /// <summary>Scans an in-memory buffer.</summary>
    public static unsafe AmsiResult Scan(byte[] content, string contentName, bool truncated = false)
    {
        fixed (byte* pointer = content)
            return Scan((IntPtr)pointer, content.Length, contentName, truncated);
    }

    private static AmsiResult Scan(IntPtr content, long length, string contentName, bool truncated)
    {
        IntPtr context = IntPtr.Zero, session = IntPtr.Zero;
        try
        {
            if (AmsiInitialize("Nexaflow", out context) != 0 || context == IntPtr.Zero)
                return new AmsiResult(AmsiVerdict.Unavailable, 0,
                    "AMSI could not be initialised; no antivirus provider is available.", truncated);

            // A session correlates several scans of the same object. One buffer still benefits:
            // without it the provider treats each call as unrelated content.
            AmsiOpenSession(context, out session);

            int hr = AmsiScanBuffer(context, content, (uint)length, contentName, session, out int result);
            if (hr != 0)
                return new AmsiResult(AmsiVerdict.Unavailable, result,
                    $"The antivirus provider returned an error (0x{hr:X8}).", truncated);

            return Translate(result, truncated);
        }
        catch (DllNotFoundException)
        {
            return new AmsiResult(AmsiVerdict.Unavailable, 0,
                "amsi.dll is not present on this system, so no scan could be performed.", truncated);
        }
        catch (EntryPointNotFoundException)
        {
            return new AmsiResult(AmsiVerdict.Unavailable, 0,
                "This build of AMSI does not expose the scanning entry points.", truncated);
        }
        catch (Exception e)
        {
            return new AmsiResult(AmsiVerdict.Unavailable, 0, $"The scan failed: {e.Message}", truncated);
        }
        finally
        {
            if (session != IntPtr.Zero) AmsiCloseSession(context, session);
            if (context != IntPtr.Zero) AmsiUninitialize(context);
        }
    }

    private static AmsiResult Translate(int result, bool truncated) => result switch
    {
        ResultClean       => new(AmsiVerdict.Clean, result, "No threat was found.", truncated),
        ResultNotDetected => new(AmsiVerdict.NotDetected, result,
                                 "The provider examined the content and detected nothing.", truncated),

        >= ResultBlockedByAdminMin and <= ResultBlockedByAdminMax =>
            new(AmsiVerdict.BlockedByAdmin, result,
                "This content is blocked by administrative policy.", truncated),

        >= ResultDetected => new(AmsiVerdict.Detected, result,
                                 "The antivirus provider identified this content as malware.", truncated),

        // 2..31: the provider is increasingly suspicious but has not committed to a detection.
        _ => new(AmsiVerdict.NotDetected, result,
                 $"The provider returned an inconclusive result ({result}).", truncated),
    };
}
