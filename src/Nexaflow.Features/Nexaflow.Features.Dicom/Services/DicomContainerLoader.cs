using System.IO;
using System.Threading;
using FellowOakDicom;
using FellowOakDicom.Media;
using Nexaflow.Features.Dicom.Models;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Turns the paths a DICOM tab was opened with — a <c>DICOMDIR</c> (a CD/USB index), a folder of loose
/// instances, a single <c>.dcm</c>, or an explicit multi-selection — into one <see cref="DicomContainer"/>
/// (Patient → Study → Series → Instance tree). A DICOMDIR gives us the on-medium file list cheaply; every
/// instance's technical metadata is then read from its header (pixel data skipped), which is the slow path
/// for a large disc and therefore runs off the UI thread.
/// </summary>
internal static class DicomContainerLoader
{
    // Encapsulated-document SOP Classes (PDF / CDA / STL / OBJ / MTL) — these open in another viewer.
    private static readonly HashSet<string> EncapsulatedSopClasses = new(StringComparer.Ordinal)
    {
        "1.2.840.10008.5.1.4.1.1.104.1", // Encapsulated PDF
        "1.2.840.10008.5.1.4.1.1.104.2", // Encapsulated CDA
        "1.2.840.10008.5.1.4.1.1.104.3", // Encapsulated STL
        "1.2.840.10008.5.1.4.1.1.104.4", // Encapsulated OBJ
        "1.2.840.10008.5.1.4.1.1.104.5", // Encapsulated MTL
    };

    public static DicomContainer Load(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        DicomBootstrap.EnsureInitialized();

        var (files, title) = ResolveFiles(paths, ct);

        var instances = new List<(DicomInstanceInfo Info, DicomDataset Ds)>();
        var warnings = new List<string>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = DicomIo.Open(f, FileReadOption.SkipLargeTags);
                instances.Add((BuildInfo(f, file), file.Dataset));
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(f)}: {ex.Message}");
            }
        }

        return BuildContainer(title, instances, warnings);
    }

    // ── File resolution ───────────────────────────────────────────────────

    private static (IReadOnlyList<string> Files, string Title) ResolveFiles(
        IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 1)
        {
            var p = paths[0];
            if (Directory.Exists(p))
            {
                // A real folder: a DICOMDIR index is the fast path; otherwise scan it.
                var dicomdir = Path.Combine(p, "DICOMDIR");
                if (File.Exists(dicomdir))
                    return (ReadDicomDir(dicomdir), new DirectoryInfo(p).Name);
                return (EnumerateDicomFiles(p, ct), new DirectoryInfo(p).Name);
            }
            if (DicomIo.IsDirectory(p))                       // a virtual folder inside an archive (.zip)
                return (EnumerateDicomFiles(p, ct), LeafName(p));
            if (IsDicomDirFile(p))
                return (ReadDicomDir(p), DicomDirTitle(p));
            return ([p], Path.GetFileName(p));
        }

        // Multi-selection: expand any folders (real or in-archive), keep DICOM files.
        var files = new List<string>();
        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(p) || DicomIo.IsDirectory(p)) files.AddRange(EnumerateDicomFiles(p, ct));
            else if (DicomIo.IsDicom(p)) files.Add(p);
        }
        return (files, $"DICOM ({files.Count} instances)");
    }

    private static string LeafName(string path)
        => Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : path;

    private static bool IsDicomDirFile(string path)
        => string.Equals(Path.GetFileName(path), "DICOMDIR", StringComparison.OrdinalIgnoreCase);

    private static string DicomDirTitle(string dicomdirPath)
    {
        var dir = Path.GetDirectoryName(dicomdirPath);
        return string.IsNullOrEmpty(dir) ? "DICOMDIR" : new DirectoryInfo(dir).Name;
    }

    private static IReadOnlyList<string> EnumerateDicomFiles(string folder, CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var f in DicomIo.EnumerateFiles(folder))     // real directory or in-archive folder
        {
            ct.ThrowIfCancellationRequested();
            if (IsDicomDirFile(f)) continue;                  // the index itself, not an instance
            if (DicomIo.IsDicom(f)) result.Add(f);
        }
        return result;
    }

    /// <summary>Walks a DICOMDIR's record tree and resolves every <c>ReferencedFileID</c> to a real path
    /// under the DICOMDIR's own directory.</summary>
    private static IReadOnlyList<string> ReadDicomDir(string dicomdirPath)
    {
        var baseDir = Path.GetDirectoryName(dicomdirPath) ?? string.Empty;
        var files = new List<string>();
        try
        {
            var dir = DicomDirectory.Open(dicomdirPath);
            foreach (var record in dir.RootDirectoryRecordCollection)
                CollectReferencedFiles(record, baseDir, files);
        }
        catch
        {
            // A malformed index shouldn't kill the open — fall back to scanning the medium.
            return EnumerateDicomFiles(baseDir, CancellationToken.None);
        }
        return files.Count > 0 ? files : EnumerateDicomFiles(baseDir, CancellationToken.None);
    }

    private static void CollectReferencedFiles(DicomDirectoryRecord record, string baseDir, List<string> files)
    {
        if (record.TryGetValues<string>(DicomTag.ReferencedFileID, out var segments) && segments is { Length: > 0 })
        {
            // ReferencedFileID is multi-valued (one value per path segment); some writers use a single
            // backslash-joined value. Handle both.
            var parts = segments.Length == 1 && segments[0].Contains('\\')
                ? segments[0].Split('\\', StringSplitOptions.RemoveEmptyEntries)
                : segments;
            var full = Path.Combine(new[] { baseDir }.Concat(parts).ToArray());
            if (File.Exists(full)) files.Add(full);
        }
        foreach (var child in record.LowerLevelDirectoryRecordCollection)
            CollectReferencedFiles(child, baseDir, files);
    }

    // ── Instance metadata ─────────────────────────────────────────────────

    private static DicomInstanceInfo BuildInfo(string path, DicomFile file)
    {
        var ds = file.Dataset;
        var sopClass = DicomTags.Str(ds, DicomTag.SOPClassUID);
        var rows = DicomTags.Int(ds, DicomTag.Rows);
        var cols = DicomTags.Int(ds, DicomTag.Columns);
        var frames = Math.Max(1, DicomTags.Int(ds, DicomTag.NumberOfFrames, 1));

        var isEncapsulated = EncapsulatedSopClasses.Contains(sopClass) || ds.Contains(DicomTag.EncapsulatedDocument);
        // An image is identified by its geometry (Rows/Columns — small header tags always present), NOT by
        // PixelData presence: we open with SkipLargeTags, which drops PixelData for real (large) images, so a
        // Contains(PixelData) check would misclassify every real slice as a report.
        var isImage = !isEncapsulated && rows > 0 && cols > 0;

        // PixelSpacing is [rowSpacing, colSpacing] in mm; ImagerPixelSpacing is the detector fallback.
        double? spacingY = null, spacingX = null;
        if (ds.TryGetValues<double>(DicomTag.PixelSpacing, out var ps) && ps.Length >= 2)
            (spacingY, spacingX) = (ps[0], ps[1]);
        else if (ds.TryGetValues<double>(DicomTag.ImagerPixelSpacing, out var ips) && ips.Length >= 2)
            (spacingY, spacingX) = (ips[0], ips[1]);

        var ts = file.FileMetaInfo?.TransferSyntax;

        return new DicomInstanceInfo(
            FilePath: path,
            SopClassUid: sopClass,
            Modality: DicomTags.Str(ds, DicomTag.Modality, "OT"),
            Rows: rows,
            Columns: cols,
            Frames: frames,
            IsImage: isImage,
            IsEncapsulatedDocument: isEncapsulated,
            EncapsulatedMimeType: isEncapsulated ? DicomTags.Str(ds, DicomTag.MIMETypeOfEncapsulatedDocument, "application/octet-stream") : null,
            PixelSpacingX: spacingX,
            PixelSpacingY: spacingY,
            RescaleSlope: DicomTags.Double(ds, DicomTag.RescaleSlope) ?? 1.0,
            RescaleIntercept: DicomTags.Double(ds, DicomTag.RescaleIntercept) ?? 0.0,
            DefaultWindowWidth: DicomTags.Double(ds, DicomTag.WindowWidth),
            DefaultWindowCenter: DicomTags.Double(ds, DicomTag.WindowCenter),
            TransferSyntaxUid: ts?.UID.UID ?? string.Empty,
            TransferSyntaxName: ts?.UID.Name ?? "Unknown");
    }

    // ── Tree assembly ─────────────────────────────────────────────────────

    private static DicomContainer BuildContainer(
        string title, List<(DicomInstanceInfo Info, DicomDataset Ds)> instances, List<string> warnings)
    {
        var patients = new List<DicomNode>();
        var images = new List<DicomNode>();
        var reports = new List<DicomNode>();
        var modalities = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var studyCount = 0;
        var seriesCount = 0;

        // Group preserving first-seen order at each level.
        var byPatient = GroupOrdered(instances, x => DicomTags.Str(x.Ds, DicomTag.PatientID, "(no id)"));
        var patientIndex = 0;
        foreach (var (_, patientItems) in byPatient)
        {
            patientIndex++;
            var pDs = patientItems[0].Ds;
            var name = DicomTags.PersonName(pDs, DicomTag.PatientName);
            var pid = DicomTags.Str(pDs, DicomTag.PatientID, "(no id)");

            var patient = new DicomNode
            {
                Kind = DicomNodeKind.Patient,
                Label = $"{name}  ·  {pid}",
                SafeLabel = $"Patient {patientIndex}",
                IsPhi = true,
            };

            var byStudy = GroupOrdered(patientItems, x => DicomTags.Str(x.Ds, DicomTag.StudyInstanceUID, "(no study)"));
            foreach (var (_, studyItems) in byStudy)
            {
                studyCount++;
                var sDs = studyItems[0].Ds;
                var studyDesc = DicomTags.Str(sDs, DicomTag.StudyDescription, "Study");
                var studyDate = DicomTags.Date(sDs, DicomTag.StudyDate);
                var study = new DicomNode
                {
                    Kind = DicomNodeKind.Study,
                    Label = string.IsNullOrEmpty(studyDate) ? studyDesc : $"{studyDesc}  ·  {studyDate}",
                };

                var bySeries = GroupOrdered(studyItems, x => DicomTags.Str(x.Ds, DicomTag.SeriesInstanceUID, "(no series)"));
                foreach (var (_, seriesItems) in bySeries)
                {
                    seriesCount++;
                    var seDs = seriesItems[0].Ds;
                    var modality = DicomTags.Str(seDs, DicomTag.Modality, "OT");
                    modalities.Add(modality);
                    var seriesNo = DicomTags.Int(seDs, DicomTag.SeriesNumber);
                    var seriesDesc = DicomTags.Str(seDs, DicomTag.SeriesDescription);
                    var seriesLabel = $"Series {seriesNo}: {modality}".TrimEnd();
                    if (!string.IsNullOrEmpty(seriesDesc)) seriesLabel += $" — {seriesDesc}";

                    var series = new DicomNode
                    {
                        Kind = DicomNodeKind.Series,
                        Label = seriesLabel,
                        Detail = $"{seriesItems.Count} instance{(seriesItems.Count == 1 ? "" : "s")}",
                    };

                    var instNo = 0;
                    foreach (var (info, iDs) in seriesItems)
                    {
                        instNo++;
                        var leaf = BuildLeaf(info, iDs, instNo);
                        series.Children.Add(leaf);
                        (info.IsImage ? images : reports).Add(leaf);
                    }

                    study.Children.Add(series);
                }

                patient.Children.Add(study);
            }

            patient.Detail = $"{patient.Children.Count} stud{(patient.Children.Count == 1 ? "y" : "ies")}";
            patients.Add(patient);
        }

        var summary =
            $"{patients.Count} patient{P(patients.Count)} · {studyCount} stud{(studyCount == 1 ? "y" : "ies")} · " +
            $"{seriesCount} series · {images.Count} image{P(images.Count)}" +
            (reports.Count > 0 ? $", {reports.Count} report{P(reports.Count)}" : "") +
            (modalities.Count > 0 ? $" · {string.Join('/', modalities)}" : "");

        var container = new DicomContainer
        {
            Images = images,
            Reports = reports,
            Title = title,
            Summary = summary,
            Warnings = warnings,
        };
        foreach (var p in patients) container.Patients.Add(p);
        return container;
    }

    private static DicomNode BuildLeaf(DicomInstanceInfo info, DicomDataset ds, int instNo)
    {
        if (info.IsImage)
        {
            var geom = $"{info.Columns}×{info.Rows}";
            var detail = info.Frames > 1 ? $"{info.Frames} frames · {geom}" : geom;
            return new DicomNode
            {
                Kind = DicomNodeKind.Image,
                Label = $"Image {DicomTags.Int(ds, DicomTag.InstanceNumber, instNo)}",
                Detail = detail,
                FilePath = info.FilePath,
                Instance = info,
            };
        }

        var kindLabel = info.IsEncapsulatedDocument
            ? (info.EncapsulatedMimeType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true ? "PDF report" : "Encapsulated document")
            : "Report";
        return new DicomNode
        {
            Kind = DicomNodeKind.Report,
            Label = $"{kindLabel} {DicomTags.Int(ds, DicomTag.InstanceNumber, instNo)}",
            Detail = info.EncapsulatedMimeType ?? info.Modality,
            FilePath = info.FilePath,
            Instance = info,
        };
    }

    private static string P(int n) => n == 1 ? "" : "s";

    /// <summary>Groups items by a key while preserving the order in which keys first appear.</summary>
    private static List<(string Key, List<T> Items)> GroupOrdered<T>(IEnumerable<T> items, Func<T, string> keyOf)
    {
        var order = new List<string>();
        var map = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            var k = keyOf(it);
            if (!map.TryGetValue(k, out var list))
            {
                map[k] = list = [];
                order.Add(k);
            }
            list.Add(it);
        }
        return order.Select(k => (k, map[k])).ToList();
    }
}
