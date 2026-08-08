using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Models;
using Nexaflow.IO.Pe;

namespace Nexaflow.Features.Executable.ViewModels;

/// <summary>
/// Projections of the single parsed <see cref="PeImage"/> into what each tab shows. Everything here
/// runs on the UI thread after the parse has completed, and none of it touches the file again except
/// through <see cref="PeImage"/>'s already-mapped buffer.
/// </summary>
public sealed partial class ExecutableViewModel
{
    private FileByteRange Range(long offset, long length, string label)
        => new(FilePath, offset, length, label);

    // ── Overview ──────────────────────────────────────────────────────────────

    private void BuildOverview(PeImage image)
    {
        LoadIcon(image);
        BuildVersionCard(image);

        var dos = image.DosHeader;
        var coff = image.CoffHeader;
        var oh   = image.OptionalHeader;

        if (dos is not null)
            OverviewCards.Add(new InspectorCard("DOS header",
            [
                new("Signature",  $"0x{dos.Magic:X4} (MZ)"),
                new("NT header",  $"0x{dos.NtHeaderOffset:X}", "e_lfanew",
                    Range(dos.NtHeaderOffset, 4, "PE signature")),
                new("DOS stub",   dos.HasCustomStub ? "Non-standard" : "Standard linker stub",
                    $"{dos.StubBytes.Length} bytes",
                    dos.StubBytes.Length > 0 ? Range(0x40, dos.StubBytes.Length, "DOS stub") : null),
            ]));

        if (coff is not null)
            OverviewCards.Add(new InspectorCard("COFF header",
            [
                new("Machine",        coff.Machine.ToString(), $"0x{(ushort)coff.Machine:X4}"),
                new("Sections",       coff.NumberOfSections.ToString()),
                new("Timestamp",      image.BuildTimestamp?.ToString("u")
                                      ?? (image.Debug.IsDeterministic
                                          ? "Reproducible build — content hash, not a time"
                                          : "(not set)"),
                                      $"0x{coff.TimeDateStamp:X8}"),
                new("Characteristics", Flags(coff.Characteristics), $"0x{(ushort)coff.Characteristics:X4}"),
            ]));

        if (oh is not null)
            OverviewCards.Add(new InspectorCard("Optional header",
            [
                new("Magic",            oh.Is64Bit ? "PE32+ (64-bit)" : "PE32 (32-bit)", $"0x{oh.Magic:X4}"),
                new("Linker",           oh.LinkerVersion),
                new("Entry point",      $"0x{oh.AddressOfEntryPoint:X8}", "RVA",
                    image.RvaToFileOffset(oh.AddressOfEntryPoint) is { } entry
                        ? Range(entry, 64, "Entry point") : null),
                new("Image base",       $"0x{oh.ImageBase:X}"),
                new("Section alignment", $"0x{oh.SectionAlignment:X}"),
                new("File alignment",   $"0x{oh.FileAlignment:X}"),
                new("Size of image",    FormatSize(oh.SizeOfImage)),
                new("Subsystem",        oh.Subsystem.ToString(), $"version {oh.SubsystemVersion}"),
                new("DLL characteristics", Flags(oh.DllCharacteristics), $"0x{(ushort)oh.DllCharacteristics:X4}"),
                new("Checksum",         $"0x{oh.CheckSum:X8}"),
            ]));

        OverviewCards.Add(new InspectorCard("Data directories",
            image.DataDirectories.Where(d => d.IsPresent).Select(d => new InspectorRow(
                d.Kind.ToString(),
                $"RVA 0x{d.VirtualAddress:X8}",
                FormatSize(d.Size),
                // The security directory is the one whose "RVA" is really a file offset.
                d.Kind == PeDirectory.Security
                    ? Range(d.VirtualAddress, d.Size, "Certificate table")
                    : image.RvaToFileOffset(d.VirtualAddress) is { } off
                        ? Range(off, d.Size, d.Kind.ToString())
                        : null)))
            { Note = image.DataDirectories.All(d => !d.IsPresent) ? "No directories are present." : null });

        OverviewCards.Add(new InspectorCard("File",
        [
            new("Size",    FormatSize(image.Length)),
            new("SHA-256", image.Sha256 ?? "—"),
            new("MD5",     image.Md5 ?? "—"),
            new("ImpHash", image.ImpHash ?? "— (no imports)"),
        ]));

        BuildSectionTree(image);
        BuildRelocations(image);
    }

    private void BuildVersionCard(PeImage image)
    {
        if (image.Version is { IsEmpty: true }) return;

        var version = image.Version;
        var rows = new List<InspectorRow>
        {
            new("File version",    version.FileVersion),
            new("Product version", version.ProductVersion),
        };
        foreach (var (name, value) in new[]
                 {
                     ("Company",     version.CompanyName),
                     ("Description", version.FileDescription),
                     ("Product",     version.ProductName),
                     ("Original name", version.OriginalFilename),
                     ("Internal name", version.InternalName),
                     ("Copyright",   version.LegalCopyright),
                 })
            if (value is { Length: > 0 }) rows.Add(new InspectorRow(name, value));

        if (version.IsDebugBuild)  rows.Add(new InspectorRow("Build", "Marked as a debug build") { StatusBrushKey = "WarningBrush" });
        if (version.IsPrerelease)  rows.Add(new InspectorRow("Build", "Marked as pre-release")   { StatusBrushKey = "WarningBrush" });

        OverviewCards.Add(new InspectorCard("Version info", rows));
    }

    private void LoadIcon(PeImage image)
    {
        try
        {
            if (PeIcons.Primary(image) is not { } group) return;

            // Handing a reassembled .ico to the real icon decoder is what makes both the classic DIB
            // and the PNG-compressed variants render without a per-format branch here.
            using var stream = new MemoryStream(group.IcoBytes);
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
                                                BitmapCacheOption.OnLoad);
            IconImage = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
        }
        catch (Exception)
        {
            // A malformed icon is not worth surfacing; the header banner just shows no image.
        }
    }

    private void BuildSectionTree(PeImage image)
    {
        foreach (var section in image.Sections)
        {
            var node = new InspectorNode(
                section.Name.Length > 0 ? section.Name : "(unnamed)",
                $"{section.Permissions}  VA 0x{section.VirtualAddress:X8}  " +
                $"virtual {FormatSize(section.VirtualSize)}  raw {FormatSize(section.RawSize)} " +
                $"@0x{section.RawPointer:X8}" +
                (section.Entropy is { } h ? $"  H={h:F2}" : ""),
                section.RawSize > 0 ? Range(section.RawPointer, section.RawSize, section.Name) : null)
            { Payload = section };

            node.Children.Add(new InspectorNode("Virtual address", $"0x{section.VirtualAddress:X8}"));
            node.Children.Add(new InspectorNode("Virtual size",    $"0x{section.VirtualSize:X8} ({FormatSize(section.VirtualSize)})"));
            node.Children.Add(new InspectorNode("Raw offset",      $"0x{section.RawPointer:X8}",
                section.RawSize > 0 ? Range(section.RawPointer, section.RawSize, section.Name) : null));
            node.Children.Add(new InspectorNode("Raw size",        $"0x{section.RawSize:X8} ({FormatSize(section.RawSize)})"));
            node.Children.Add(new InspectorNode("Characteristics", $"{Flags(section.Characteristics)} (0x{(uint)section.Characteristics:X8})"));
            if (section.Entropy is { } entropy)
                node.Children.Add(new InspectorNode("Entropy", $"{entropy:F3} bits/byte"));
            if (section.Md5 is { } md5)
                node.Children.Add(new InspectorNode("MD5", md5));

            node.IsExpanded = false;
            SectionNodes.Add(node);
        }
    }

    private void BuildRelocations(PeImage image)
    {
        var relocations = image.Relocations;
        if (relocations.IsEmpty)
        {
            RelocationSummary = image.CoffHeader?.Characteristics.HasFlag(PeFileCharacteristics.RelocsStripped) == true
                ? "Relocations were stripped — the image must load at its preferred base."
                : "No base relocations.";
            return;
        }

        RelocationSummary =
            $"{relocations.Blocks.Count} blocks, {relocations.TotalFixups:N0} fixups, " +
            $"{FormatSize(relocations.TotalBytes)} — " +
            string.Join(", ", relocations.CountsByType.Select(k => $"{k.Key} × {k.Value:N0}"));

        foreach (var block in relocations.Blocks.Take(500))
            RelocationRows.Add(new InspectorRow(
                $"Page 0x{block.PageRva:X8}",
                $"{block.FixupCount} fixups",
                FormatSize(block.BlockSize),
                image.RvaToFileOffset(block.PageRva) is { } offset
                    ? Range(offset, Math.Min(4096, image.Length - offset), $"Relocation page 0x{block.PageRva:X}")
                    : null));

        if (relocations.Blocks.Count > 500)
            RelocationRows.Add(new InspectorRow("…",
                $"{relocations.Blocks.Count - 500:N0} further blocks not listed"));
    }

    // ── Imports / exports ─────────────────────────────────────────────────────

    private void BuildImportsExports(PeImage image)
    {
        int functions = image.Imports.Sum(m => m.Functions.Count);
        ImportSummary = image.Imports.Count == 0
            ? "This image imports nothing."
            : $"{image.Imports.Count} modules, {functions:N0} functions" +
              (image.ImpHash is { } h ? $" · imphash {h}" : "");

        foreach (var group in new[]
                 {
                     ("Imports", image.Imports),
                     ("Delay-loaded", image.DelayImports),
                     ("Bound", image.BoundImports),
                 })
        {
            if (group.Item2.Count == 0) continue;

            var header = new InspectorNode(group.Item1, $"{group.Item2.Count} modules") { IsExpanded = true };
            foreach (var module in group.Item2)
            {
                var moduleNode = new InspectorNode(module.Name,
                    module.Functions.Count > 0
                        ? $"{module.Functions.Count} functions{(module.IsApiSet ? " · API set" : "")}"
                        : module.Kind == PeImportKind.Bound ? "bound" : "no functions listed")
                { IsExpanded = false, Payload = module };

                foreach (var function in module.Functions)
                    moduleNode.Children.Add(new InspectorNode(
                        function.Display,
                        function.IsByOrdinal
                            ? $"ordinal {function.Ordinal}"
                            : $"hint {function.Hint}  IAT 0x{function.IatRva:X8}",
                        function.IatRva != 0 && image.RvaToFileOffset(function.IatRva) is { } iat
                            ? Range(iat, image.Is64Bit ? 8 : 4, $"{module.Name}!{function.Display}")
                            : null));

                header.Children.Add(moduleNode);
            }
            ImportNodes.Add(header);
        }

        var exports = image.Exports;
        ExportSummary = exports.Entries.Count == 0
            ? "This image exports nothing."
            : $"{exports.DllName ?? "(unnamed)"} — {exports.Entries.Count:N0} exports, " +
              $"{exports.Entries.Count(e => e.IsForwarder)} forwarders, " +
              $"{exports.Entries.Count(e => e.IsByOrdinal)} by ordinal only";

        foreach (var entry in exports.Entries.Take(5000))
            ExportRows.Add(new InspectorRow(
                entry.Display,
                entry.IsForwarder ? $"→ {entry.ForwarderTo}" : $"0x{entry.Rva:X8}",
                $"ordinal {entry.Ordinal}",
                !entry.IsForwarder && image.RvaToFileOffset(entry.Rva) is { } offset
                    ? Range(offset, 64, entry.Display)
                    : null));

        if (exports.Entries.Count > 5000)
            ExportRows.Add(new InspectorRow("…", $"{exports.Entries.Count - 5000:N0} further exports not listed"));

        if (exports.IsComSelfRegistering || image.Resources.HasTypeLib)
        {
            var parts = new List<string>();
            if (exports.IsComSelfRegistering)
                parts.Add($"self-registering COM server ({string.Join(", ", exports.ComEntryPoints)})");
            if (image.Resources.HasTypeLib) parts.Add("embedded type library");
            ComSummary = "COM: " + string.Join("; ", parts);
        }
    }

    // ── Resources ─────────────────────────────────────────────────────────────

    private void BuildResources(PeImage image)
    {
        if (image.Resources.IsEmpty)
        {
            ResourceSummary = "This image has no resource directory.";
            return;
        }

        int leaves = image.Resources.Types.SelectMany(t => t.Descend()).Count(n => n.IsLeaf);
        ResourceSummary = $"{image.Resources.Types.Count} types, {leaves} entries";

        foreach (var type in image.Resources.Types)
            ResourceNodes.Add(Convert(image, type));
    }

    private InspectorNode Convert(PeImage image, PeResourceNode source)
    {
        var node = new InspectorNode(
            source.Display,
            source.IsLeaf
                ? $"{FormatSize(source.DataSize)}  RVA 0x{source.DataRva:X8}" +
                  (source.CodePage != 0 ? $"  cp{source.CodePage}" : "")
                : null,
            source is { IsLeaf: true, DataOffset: { } offset }
                ? Range(offset, source.DataSize, source.Display)
                : null)
        {
            Payload    = source,
            CanExtract = source.IsLeaf,
            IsExpanded = source.Level == PeResourceLevel.Type,
        };

        foreach (var child in source.Children) node.Children.Add(Convert(image, child));
        return node;
    }

    // ── Manifest ──────────────────────────────────────────────────────────────

    private void BuildManifest(PeImage image)
    {
        var manifest = image.Manifest;
        HasManifest  = !manifest.IsEmpty;
        if (!HasManifest) return;

        ManifestXml = manifest.RawXml ?? string.Empty;

        if (manifest.ParseError is { } error)
        {
            ManifestCards.Add(new InspectorCard("Manifest",
                [new InspectorRow("Could not be parsed", error) { StatusBrushKey = "DangerBrush" }])
            { Note = "The raw XML is shown below." });
            ShowRawManifest = true;
            return;
        }

        var identity = new List<InspectorRow>();
        if (manifest.IsExternal)
            identity.Add(new InspectorRow("Source", "External .manifest file beside the binary"));
        foreach (var (label, value) in new[]
                 {
                     ("Name",         manifest.AssemblyName),
                     ("Version",      manifest.AssemblyVersion),
                     ("Architecture", manifest.ProcessorArchitecture),
                     ("Type",         manifest.AssemblyType),
                     ("Public key token", manifest.PublicKeyToken),
                     ("Description",  manifest.Description),
                 })
            if (value is { Length: > 0 }) identity.Add(new InspectorRow(label, value));
        if (identity.Count > 0) ManifestCards.Add(new InspectorCard("Identity", identity));

        ManifestCards.Add(new InspectorCard("Elevation (UAC)",
        [
            new InspectorRow("Requested level", manifest.ExecutionLevel switch
            {
                PeExecutionLevel.AsInvoker            => "asInvoker — runs as the invoking user",
                PeExecutionLevel.HighestAvailable     => "highestAvailable — elevates if the user can",
                PeExecutionLevel.RequireAdministrator => "requireAdministrator — always elevates",
                _ => "Not declared — subject to installer detection and virtualisation",
            })
            {
                StatusBrushKey = manifest.ExecutionLevel switch
                {
                    PeExecutionLevel.RequireAdministrator => "DangerBrush",
                    PeExecutionLevel.HighestAvailable     => "WarningBrush",
                    PeExecutionLevel.Unspecified          => "WarningBrush",
                    _ => null,
                },
            },
            new InspectorRow("uiAccess", manifest.UiAccess
                ? "true — may drive the UI of higher-privileged windows"
                : "false"),
        ]));

        ManifestCards.Add(new InspectorCard("OS compatibility",
            manifest.SupportedOs.Count == 0
                ? [new InspectorRow("Declared support", "None — the binary is shimmed as a pre-Vista application")
                    { StatusBrushKey = "WarningBrush" }]
                : manifest.SupportedOs
                    .Select(os => new InspectorRow(os.Name ?? "Unrecognised", os.Id))
                    .Append(new InspectorRow("Shims",
                        manifest.RunsUnderCompatibilityShims
                            ? "No Windows 10/11 entry — compatibility shims apply"
                            : "Windows 10/11 declared — no version shimming")
                    { StatusBrushKey = manifest.RunsUnderCompatibilityShims ? "WarningBrush" : null })
                    .ToList()));

        var settings = new List<InspectorRow>
        {
            new("DPI awareness", manifest.DpiAwareness switch
            {
                PeDpiAwareness.PerMonitorV2 => "Per-monitor v2 — the modern, fully scaled mode",
                PeDpiAwareness.PerMonitor   => "Per-monitor (v1)",
                PeDpiAwareness.System       => "System — bitmap-scaled on secondary displays",
                PeDpiAwareness.Unaware      => "Unaware — always bitmap-scaled",
                _                           => "Not declared",
            }),
            new("Long paths", manifest.LongPathAware ? "Enabled" : "Not enabled"),
        };
        if (manifest.ActiveCodePage is { Length: > 0 } codePage)
            settings.Add(new InspectorRow("Active code page", codePage));

        // Anything the decoder does not model is still listed, so a newer setting is never lost.
        foreach (var (key, value) in manifest.WindowsSettings)
            if (key is not ("dpiAware" or "dpiAwareness" or "longPathAware" or "activeCodePage"))
                settings.Add(new InspectorRow(key, value));
        ManifestCards.Add(new InspectorCard("Windows settings", settings));

        if (manifest.Dependencies.Count > 0)
            ManifestCards.Add(new InspectorCard("Dependent assemblies",
                manifest.Dependencies.Select(d => new InspectorRow(
                    d.Name,
                    d.Version is { Length: > 0 } ? $"version {d.Version}" : "",
                    string.Join("  ", new[] { d.ProcessorArchitecture, d.PublicKeyToken, d.Type }
                        .Where(s => s is { Length: > 0 }))))));

        if (manifest.HasRegistrationFreeCom)
            ManifestCards.Add(new InspectorCard("Registration-free COM",
            [
                new("COM classes",   manifest.ComClassCount.ToString()),
                new("Type libraries", manifest.TypeLibCount.ToString()),
                new("Window classes", manifest.WindowClassCount.ToString()),
                new("Proxy stubs",    manifest.ProxyStubCount.ToString()),
            ]));

        if (manifest.Other.Count > 0)
            ManifestCards.Add(new InspectorCard("Other elements",
                manifest.Other.Select(name => new InspectorRow(name, "not decoded — see the raw XML")))
            { Note = "Present in the manifest but not modelled here." });
    }

    // ── .NET ──────────────────────────────────────────────────────────────────

    private void BuildDotnet(PeImage image)
    {
        var clr = image.Clr;
        IsManaged = clr.IsManaged;
        if (!IsManaged) return;

        DotnetCards.Add(new InspectorCard("CLR header",
        [
            new("Runtime version", clr.RuntimeVersion ?? "—"),
            new("Metadata version", clr.MetadataVersion ?? "—"),
            new("Flags", clr.Flags == PeClrFlags.None ? "(none)" : clr.Flags.ToString()),
            new("Bitness", clr.Bitness),
            new("IL only", clr.IsIlOnly ? "Yes" : "No — contains native code"),
            new("Strong-name signed", clr.IsStrongNameSigned ? "Yes" : "No"),
            new("Entry point token", $"0x{clr.EntryPointToken:X8}"),
            new("Metadata", $"RVA 0x{clr.MetadataRva:X8}", FormatSize(clr.MetadataSize),
                image.RvaToFileOffset(clr.MetadataRva) is { } offset
                    ? Range(offset, clr.MetadataSize, "CLR metadata") : null),
        ]));

        var identity = new List<InspectorRow>
        {
            new("Assembly", clr.AssemblyName ?? "(not an assembly)"),
            new("Version",  clr.AssemblyVersion ?? "—"),
            new("Culture",  clr.AssemblyCulture ?? "neutral"),
            new("Public key token", clr.PublicKeyToken ?? "(unsigned)"),
            new("Target framework", clr.TargetFramework ?? "(not declared)"),
        };
        if (clr.IsWindowsRuntime)
            identity.Add(new InspectorRow("Windows Runtime",
                "This is WinRT metadata (.winmd), not a normal assembly"));
        DotnetCards.Add(new InspectorCard("Assembly", identity));

        DotnetCards.Add(new InspectorCard("Assembly references",
            clr.AssemblyReferences.Select(r => new InspectorRow(
                r.Name, r.Version,
                string.Join("  ", new[] { r.Culture, r.PublicKeyToken }.Where(s => s is { Length: > 0 })))))
        { Note = clr.AssemblyReferences.Count == 0 ? "No referenced assemblies." : null });
    }

    private static string Flags<T>(T value) where T : struct, Enum
    {
        string text = value.ToString() ?? "";
        return text is "0" or "None" ? "(none)" : text.Replace(", ", " · ");
    }
}
