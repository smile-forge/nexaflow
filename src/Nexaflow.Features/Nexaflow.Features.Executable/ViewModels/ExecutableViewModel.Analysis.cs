using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Models;
using Nexaflow.Features.Executable.Services;
using Nexaflow.IO.Pe;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Executable.ViewModels;

/// <summary>
/// The Analysis tab plus every piece of work that is too expensive to do during the initial parse:
/// the signature verdict (calls into the OS and consults catalogs), the dependency walk (opens
/// dozens of other files), the string sweep (touches every byte) and the antivirus scan.
/// Each is triggered by its tab becoming visible, and each runs exactly once.
/// </summary>
public sealed partial class ExecutableViewModel
{
    private bool _dependenciesRequested;
    private bool _stringsRequested;
    private bool _signatureRequested;

    // ── Entropy + the static half of Analysis ─────────────────────────────────

    private void BuildAnalysis(PeImage image)
    {
        EntropyBuckets = image.Entropy.Buckets;
        EntropySummary = Str.Format("Executable.Analysis.EntropySummaryFormat", image.Entropy.Overall, image.Entropy.Buckets.Count, FormatSize(image.Entropy.BucketBytes));

        var entropyRows = image.Sections.Select(s => new InspectorRow(
            s.Name.Length > 0 ? s.Name : Str.Get("Executable.Unnamed"),
            s.Entropy is { } h ? $"{h:F3}" : "—",
            $"{s.Permissions}  {FormatSize(s.RawSize)}",
            s.RawSize > 0 ? Range(s.RawPointer, s.RawSize, s.Name) : null)
        {
            // High entropy alone is unremarkable — .rsrc is full of PNGs. High entropy in a section
            // the CPU will execute is the packing signal worth colouring.
            StatusBrushKey = s.IsExecutable && s.Entropy >= PeEntropy.PackedThreshold ? "DangerBrush"
                           : s.Entropy >= PeEntropy.PackedThreshold                   ? "WarningBrush"
                           : null,
        }).ToList();

        var packed = PackedSections(image).ToList();
        AnalysisCards.Add(new InspectorCard(Str.Get("Executable.Inspector.EntropyBySection"), entropyRows)
        {
            Note = packed.Count > 0
                ? Str.Format("Executable.Analysis.PackedFormat", string.Join(", ", packed))
                : Str.Get("Executable.Inspector.NoExecutableSectionIsAboveThe"),
        });

        BuildIntegrityCard(image);
        BuildDebugAndTlsCard(image);
    }

    private void BuildIntegrityCard(PeImage image)
    {
        var rows = new List<InspectorRow>();

        foreach (var section in image.Sections.Where(s => s.IsWritableExecutable))
            rows.Add(new InspectorRow(section.Name, Str.Get("Executable.Inspector.WritableAndExecutable"),
                Str.Get("Executable.Inspector.LegitimateCodeRarelyNeedsBoth")) { StatusBrushKey = "DangerBrush" });

        if (image.OptionalHeader is { } oh)
        {
            void Flag(string name, bool present, string good, string bad)
                => rows.Add(new InspectorRow(name, present ? good : bad)
                { StatusBrushKey = present ? null : "WarningBrush" });

            Flag("ASLR",  oh.DllCharacteristics.HasFlag(PeDllCharacteristics.DynamicBase),
                 Str.Get("Executable.Inspector.EnabledDYNAMICBASE"), Str.Get("Executable.Inspector.NotEnabledTheImageLoadsAt"));
            Flag("DEP",   oh.DllCharacteristics.HasFlag(PeDllCharacteristics.NxCompat),
                 Str.Get("Executable.Inspector.EnabledNXCOMPAT"), Str.Get("Executable.Inspector.NotEnabled"));
            Flag("CFG",   oh.DllCharacteristics.HasFlag(PeDllCharacteristics.GuardCf),
                 Str.Get("Executable.Inspector.EnabledGUARDCF"), Str.Get("Executable.Inspector.NotEnabled"));
            Flag("SEH",   !oh.DllCharacteristics.HasFlag(PeDllCharacteristics.NoSeh),
                 Str.Get("Executable.Inspector.StructuredExceptionHandlingPresent"), "NO_SEH");
            Flag(Str.Get("Executable.Inspector.HighEntropyASLR"), oh.DllCharacteristics.HasFlag(PeDllCharacteristics.HighEntropyVa),
                 Str.Get("Executable.Inspector.Enabled"), Str.Get("Executable.Inspector.NotEnabled"));
        }

        AnalysisCards.Add(new InspectorCard(Str.Get("Executable.Inspector.Hardening"), rows));
    }

    private void BuildDebugAndTlsCard(PeImage image)
    {
        var rows = new List<InspectorRow>();
        var debug = image.Debug;

        if (debug.Entries.Count > 0)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.DebugDirectory"),
                string.Join(" · ", debug.Entries.Select(e => e.Type.ToString()))));

        if (debug.PdbPath is { Length: > 0 } pdb)
        {
            rows.Add(new InspectorRow("PDB", pdb,
                debug.PdbGuid is { } guid ? Str.Format("Executable.Analysis.PdbAgeFormat", guid, debug.PdbAge) : null)
            {
                // An absolute build-machine path is a genuine information leak, not a curiosity.
                StatusBrushKey = debug.LeaksBuildPath ? "WarningBrush" : null,
            });
            if (debug.LeaksBuildPath)
                rows.Add(new InspectorRow(Str.Get("Executable.Inspector.BuildPath"), Str.Get("Executable.Inspector.ThePDBPathIsAbsoluteIt"))
                { StatusBrushKey = "WarningBrush" });
        }

        if (debug.IsDeterministic)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.ReproducibleBuild"),
                Str.Get("Executable.Inspector.YesTheCOFFTimestampIsA")));
        if (debug.HasEmbeddedPdb)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.EmbeddedPDB"), Str.Get("Executable.Inspector.SymbolsAreCarriedInsideTheImage")));

        var tls = image.Tls;
        if (!tls.IsPresent)
        {
            rows.Add(new InspectorRow("TLS", Str.Get("Executable.Inspector.NoThreadLocalStorageDirectory")));
        }
        else
        {
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.TLSDirectory"), $"0x{tls.AddressOfCallBacks:X}",
                Str.Format("Executable.Analysis.TlsCallbacksFormat", tls.Callbacks.Count)));

            foreach (var callback in tls.Callbacks)
                rows.Add(new InspectorRow(Str.Get("Executable.Inspector.TLSCallback"),
                    $"0x{callback.VirtualAddress:X}",
                    callback.FileOffset is { } offset ? Str.Format("Executable.Analysis.FileOffsetFormat", offset) : Str.Get("Executable.Analysis.Unmapped"),
                    callback.FileOffset is { } o ? Range(o, 64, Str.Get("Executable.Inspector.TLSCallback")) : null)
                {
                    // A TLS callback runs before the entry point on every thread attach — the classic
                    // anti-debugging and unpacking hook.
                    StatusBrushKey = "WarningBrush",
                });

            if (tls.HasCallbacks)
                rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Note"),
                    Str.Get("Executable.Analysis.TlsNote")));
        }

        AnalysisCards.Add(new InspectorCard(Str.Get("Executable.Inspector.DebugTLS"), rows));
    }

    // ── Signature (lazy) ──────────────────────────────────────────────────────

    private void EnsureSignature()
    {
        if (_signatureRequested || _image is null) return;
        _signatureRequested = true;
        SignatureLoading    = true;

        _shell.QueueBackgroundTask(new SignatureTask(this), ct: _cts.Token);
    }

    private sealed class SignatureTask(ExecutableViewModel owner) : IBackgroundTask
    {
        public string Description => Str.Format("Executable.Task.VerifyingFormat", owner.FileName);

        public async Task RunAsync(CancellationToken ct)
        {
            string path = owner.FilePath;

            // Re-read with verification on rather than mutating the shared image: WinVerifyTrust may
            // consult catalogs and the cached CRL store, which is far too slow for the first paint.
            var security = await Task.Run(
                () =>
                {
                    using var verified = PeReader.Read(path, new PeReadOptions
                    {
                        IncludeImports   = false,
                        IncludeExports   = false,
                        IncludeResources = false,
                        IncludeEntropy   = false,
                        IncludeFileHashes = false,
                        IncludeSectionHashes = false,
                        VerifySignature  = true,
                    });
                    return verified.Security;
                }, ct);

            var products = await Task.Run(
                () => OperatingSystem.IsWindows() ? AntivirusProducts.Enumerate() : [], ct);

            ct.ThrowIfCancellationRequested();
            await owner._shell.RunOnUiAsync(() => owner.PublishSignature(security, products));
        }
    }

    private void PublishSignature(PeSecurity security, IReadOnlyList<AntivirusProduct> products)
    {
        SignatureLoading = false;

        var rows = new List<InspectorRow>
        {
            new(Str.Get("Executable.Inspector.Verdict"), security.Verdict switch
            {
                PeTrustVerdict.Valid     => Str.Get("Executable.Inspector.ValidAndTrusted"),
                PeTrustVerdict.Unsigned  => Str.Get("Executable.Inspector.NotSigned"),
                PeTrustVerdict.Untrusted => Str.Get("Executable.Inspector.SignedButNotTrusted"),
                PeTrustVerdict.Expired   => Str.Get("Executable.Inspector.SignedWithAnExpiredCertificate"),
                PeTrustVerdict.Revoked   => Str.Get("Executable.Inspector.SignedWithARevokedCertificate"),
                PeTrustVerdict.Malformed => Str.Get("Executable.Inspector.TheSignatureIsPresentButInvalid"),
                _                        => Str.Get("Executable.Inspector.NotChecked"),
            })
            {
                StatusBrushKey = security.Verdict switch
                {
                    PeTrustVerdict.Valid                              => "SuccessBrush",
                    PeTrustVerdict.Unsigned                           => "WarningBrush",
                    PeTrustVerdict.NotChecked                         => null,
                    _                                                 => "DangerBrush",
                },
            },
        };

        if (security.VerdictDetail is { Length: > 0 } detail)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Detail"), detail));

        if (security.IsCatalogSigned && security.CatalogPath is { } catalog)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Catalog"), Path.GetFileName(catalog), catalog));

        if (security.Signer is { } signer)
        {
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Signer"), signer.CommonName, signer.Subject));
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Issuer"), signer.Issuer));
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Thumbprint"), signer.Thumbprint));
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Valid"), $"{signer.NotBefore:u} → {signer.NotAfter:u}")
            { StatusBrushKey = signer.IsExpired ? "WarningBrush" : null });
        }
        if (security.DigestAlgorithm is { } digest) rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Digest"), digest));
        if (security.SigningTime is { } signed)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Timestamped"), signed.ToString("u"),
                Str.Get("Executable.Inspector.TheChainIsValidatedAsAt")));

        foreach (var element in security.Chain)
            rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Chain"), element.Certificate.CommonName,
                element.IsOk ? "OK" : string.Join("; ", element.StatusMessages))
            { StatusBrushKey = element.IsOk ? null : "DangerBrush" });

        AnalysisCards.Insert(0, new InspectorCard(Str.Get("Executable.Inspector.Signature"), rows));

        AntivirusProduct = products.Count > 0
            ? string.Join(", ", products.Select(p => $"{p.Name} ({p.Status})"))
            : Str.Get("Executable.Inspector.NoAntivirusProductIsRegisteredWith");
    }

    // ── Antivirus scan ────────────────────────────────────────────────────────

    [RelayCommand]
    private void ScanWithAntivirus()
    {
        if (ScanRunning) return;
        ScanRunning = true;
        ScanResult  = Str.Get("Executable.Inspector.Scanning");
        ScanBrushKey = null;

        _shell.QueueBackgroundTask(new ScanTask(this), ct: _cts.Token);
    }

    private sealed class ScanTask(ExecutableViewModel owner) : IBackgroundTask
    {
        public string Description => Str.Format("Executable.Task.ScanningFormat", owner.FileName);

        public async Task RunAsync(CancellationToken ct)
        {
            var result = await Task.Run(
                () => OperatingSystem.IsWindows()
                    ? AmsiScanner.ScanFile(owner.FilePath, ct)
                    : new AmsiResult(AmsiVerdict.Unavailable, 0, Str.Get("Executable.Inspector.AMSIIsOnlyAvailableOnWindows"), false),
                ct);

            await owner._shell.RunOnUiAsync(() =>
            {
                owner.ScanRunning = false;
                owner.ScanResult  = result.Truncated
                    ? Str.Format("Executable.Analysis.TruncatedFormat", result.Message, FormatSize(AmsiScanner.MaxScanBytes))
                    : result.Message;
                owner.ScanBrushKey = result.Verdict switch
                {
                    AmsiVerdict.Clean or AmsiVerdict.NotDetected => "SuccessBrush",
                    AmsiVerdict.Unavailable                      => "WarningBrush",
                    _                                            => "DangerBrush",
                };
            });
        }
    }

    // ── Strings (lazy) ────────────────────────────────────────────────────────

    private void EnsureStrings()
    {
        if (_stringsRequested || _image is null) return;
        _stringsRequested = true;
        StringsLoading    = true;

        _shell.QueueBackgroundTask(new StringsTask(this), ct: _cts.Token);
    }

    private sealed class StringsTask(ExecutableViewModel owner) : IBackgroundTask
    {
        public string Description => Str.Format("Executable.Task.ExtractingStringsFormat", owner.FileName);

        public async Task RunAsync(CancellationToken ct)
        {
            var image = owner._image;
            if (image is null) return;

            int minimum = owner.MinimumStringLength;

            // Both the sweep AND the row construction happen here, off the dispatcher. Building a
            // hundred thousand rows is itself seconds of work — doing it during the UI hand-off
            // freezes the window just as thoroughly as the scan would.
            var (rows, ascii, utf16) = await Task.Run(() =>
            {
                var built = new List<InspectorRow>();
                int a = 0, u = 0;

                foreach (var hit in PeStrings.Extract(image, minimum, MaxRows, ct))
                {
                    if (hit.Encoding == PeStringEncoding.Ascii) a++; else u++;

                    // Displayed text is capped. A packed installer yields runs tens of thousands of
                    // characters long, and handing one of those to a row's text control makes WPF
                    // build an enormous visual for a line nobody can read — which is what took the
                    // process down during fast scrolling. The full run is kept for Copy.
                    bool   long_ = hit.Value.Length > MaxDisplayedCharacters;
                    string shown = long_ ? hit.Value[..MaxDisplayedCharacters] + "…" : hit.Value;

                    built.Add(new InspectorRow(
                        shown,
                        $"0x{hit.Offset:X8}",
                        $"{hit.Encoding}{(hit.Section is { } s ? $"  {s}" : "")}" +
                        (long_ ? "  " + Str.Format("Executable.Strings.CharsFormat", hit.Value.Length) : ""),
                        owner.Range(hit.Offset,
                                    hit.Value.Length * (hit.Encoding == PeStringEncoding.Utf16 ? 2 : 1),
                                    "String"))
                    {
                        FullText = long_ ? hit.Value : null,
                    });
                }
                return (built, a, u);
            }, ct);

            ct.ThrowIfCancellationRequested();
            await owner._shell.RunOnUiAsync(() =>
            {
                owner.StringsLoading = false;
                owner.PublishStrings(rows);
                owner.StringSummary =
                    rows.Count >= MaxRows
                        ? Str.Format("Executable.Strings.SummaryCappedFormat", rows.Count, minimum, ascii, utf16)
                        : Str.Format("Executable.Strings.SummaryFormat", rows.Count, minimum, ascii, utf16);
            });
        }

        /// <summary>Ceiling on rows handed to the list. Beyond this the tab stops being readable
        /// long before it stops being buildable.</summary>
        private const int MaxRows = 100_000;

        /// <summary>Longest run shown in a row. Well past anything readable at a glance, and far
        /// short of what makes text layout fall over.</summary>
        private const int MaxDisplayedCharacters = 400;
    }

    /// <summary>Swaps in a freshly built set of rows behind one notification.</summary>
    private void PublishStrings(IReadOnlyList<InspectorRow> rows)
    {
        _allStrings = rows;

        var view = new ListCollectionView((System.Collections.IList)rows)
        {
            Filter = o => _stringFilter is null || (o is InspectorRow r && _stringFilter(r)),
        };
        StringView = view;
    }

    /// <summary>Re-runs the sweep after the minimum-length filter changes.</summary>
    [RelayCommand]
    private void RefreshStrings()
    {
        // Drop any active search filter first, or the rescanned rows land behind a stale predicate
        // and the list comes back empty.
        ClearSearch();

        _stringsRequested = false;
        PublishStrings([]);
        EnsureStrings();
    }

    // ── Resource extraction ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExtractResource(InspectorNode? node)
    {
        if (_image is null || node?.Payload is not PeResourceNode resource || !resource.IsLeaf) return;

        // An icon group is only useful as a real .ico, so reassemble rather than dumping the
        // directory bytes, which no image viewer can open.
        bool isIconGroup = TypeLabel(node).Contains("GROUP_ICON", StringComparison.OrdinalIgnoreCase);
        var  bytes       = isIconGroup ? PeIcons.Build(_image, resource) ?? [] : _image.ReadResource(resource);
        if (bytes.Length == 0)
        {
            _shell.ShowError(Str.Get("Executable.Inspector.ThatResourceHasNoReadableData"));
            return;
        }

        string extension = isIconGroup ? ".ico" : ExtensionFor(node);
        string suggested = $"{Path.GetFileNameWithoutExtension(FileName)}_{Sanitise(node.Label)}{extension}";

        var target = await _shell.PickSaveFileAsync(suggested, [extension], Path.GetDirectoryName(FilePath));
        if (string.IsNullOrEmpty(target)) return;

        try
        {
            await File.WriteAllBytesAsync(target, bytes, _cts.Token);
            _shell.ShowNotification(Str.Format("Executable.Resources.ExtractedOneFormat", FormatSize(bytes.Length), Path.GetFileName(target)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError(Str.Format("Executable.Resources.WriteFailedFormat", e.Message));
        }
    }

    [RelayCommand]
    private async Task ExtractAllResources()
    {
        if (_image is null || ResourceNodes.Count == 0) return;

        var folder = await _shell.PickFolderAsync(Path.GetDirectoryName(FilePath));
        if (string.IsNullOrEmpty(folder)) return;

        int written = 0, failed = 0;
        foreach (var node in ResourceNodes.SelectMany(n => n.Descend()).Where(n => n.CanExtract))
        {
            if (node.Payload is not PeResourceNode resource) continue;

            var bytes = _image.ReadResource(resource);
            if (bytes.Length == 0) continue;

            string name = Sanitise($"{TypeLabel(node)}_{node.Label}") + ExtensionFor(node);
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, _cts.Token);
                written++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; }
        }

        _shell.ShowNotification(failed == 0
            ? Str.Format("Executable.Resources.ExtractedAllFormat", written, folder)
            : Str.Format("Executable.Resources.ExtractedSomeFormat", written, folder, failed));
    }

    /// <summary>
    /// The RT_* type a leaf sits under. The tree is built type → name → language, so a leaf's type
    /// is simply whichever root contains it — there is no type id on the leaf itself to read.
    /// </summary>
    private string TypeLabel(InspectorNode leaf)
        => ResourceNodes.FirstOrDefault(t => t.Descend().Contains(leaf))?.Label ?? "resource";

    private string ExtensionFor(InspectorNode node)
    {
        string type = TypeLabel(node);
        if (type.Contains("MANIFEST", StringComparison.OrdinalIgnoreCase)) return ".manifest";
        if (type.Contains("HTML",     StringComparison.OrdinalIgnoreCase)) return ".html";
        if (type.Contains("BITMAP",   StringComparison.OrdinalIgnoreCase)) return ".bmp";
        if (type.Contains("GROUP_ICON", StringComparison.OrdinalIgnoreCase)) return ".ico";
        if (type.Contains("ICON",     StringComparison.OrdinalIgnoreCase)) return ".ico";
        if (type.Contains("CURSOR",   StringComparison.OrdinalIgnoreCase)) return ".cur";
        if (type.Contains("TYPELIB",  StringComparison.OrdinalIgnoreCase)) return ".tlb";
        return ".bin";
    }

    private static string Sanitise(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "resource" : cleaned;
    }
}
