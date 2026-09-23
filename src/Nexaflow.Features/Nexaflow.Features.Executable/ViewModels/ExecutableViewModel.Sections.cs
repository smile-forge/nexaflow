using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Models;
using Nexaflow.IO.Pe;
using Nexaflow.Visuals.Common.Localization;

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
            OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.DOSHeader"),
            [
                new(Str.Get("Executable.Inspector.Signature"),  $"0x{dos.Magic:X4} (MZ)"),
                new(Str.Get("Executable.Inspector.NTHeader"),  $"0x{dos.NtHeaderOffset:X}", "e_lfanew",
                    Range(dos.NtHeaderOffset, 4, Str.Get("Executable.Inspector.PESignature"))),
                new(Str.Get("Executable.Inspector.DOSStub"),   dos.HasCustomStub ? Str.Get("Executable.Inspector.NonStandard") : Str.Get("Executable.Inspector.StandardLinkerStub"),
                    Str.Format("Executable.Overview.BytesFormat", dos.StubBytes.Length),
                    dos.StubBytes.Length > 0 ? Range(0x40, dos.StubBytes.Length, Str.Get("Executable.Inspector.DOSStub")) : null),
            ]));

        if (coff is not null)
            OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.COFFHeader"),
            [
                new(Str.Get("Executable.Inspector.Machine"),        coff.Machine.ToString(), $"0x{(ushort)coff.Machine:X4}"),
                new(Str.Get("Executable.Inspector.Sections"),       coff.NumberOfSections.ToString()),
                new(Str.Get("Executable.Inspector.Timestamp"),      image.BuildTimestamp?.ToString("u")
                                      ?? (image.Debug.IsDeterministic
                                          ? Str.Get("Executable.Inspector.ReproducibleBuildContentHashNotA")
                                          : Str.Get("Executable.Inspector.NotSet")),
                                      $"0x{coff.TimeDateStamp:X8}"),
                new(Str.Get("Executable.Inspector.Characteristics"), Flags(coff.Characteristics), $"0x{(ushort)coff.Characteristics:X4}"),
            ]));

        if (oh is not null)
            OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.OptionalHeader"),
            [
                new(Str.Get("Executable.Inspector.Magic"),            oh.Is64Bit ? Str.Get("Executable.Inspector.PE3264Bit") : Str.Get("Executable.Inspector.PE3232Bit"), $"0x{oh.Magic:X4}"),
                new(Str.Get("Executable.Inspector.Linker"),           oh.LinkerVersion),
                new(Str.Get("Executable.Inspector.EntryPoint"),      $"0x{oh.AddressOfEntryPoint:X8}", "RVA",
                    image.RvaToFileOffset(oh.AddressOfEntryPoint) is { } entry
                        ? Range(entry, 64, Str.Get("Executable.Inspector.EntryPoint")) : null),
                new(Str.Get("Executable.Inspector.ImageBase"),       $"0x{oh.ImageBase:X}"),
                new(Str.Get("Executable.Inspector.SectionAlignment"), $"0x{oh.SectionAlignment:X}"),
                new(Str.Get("Executable.Inspector.FileAlignment"),   $"0x{oh.FileAlignment:X}"),
                new(Str.Get("Executable.Inspector.SizeOfImage"),    FormatSize(oh.SizeOfImage)),
                new(Str.Get("Executable.Inspector.Subsystem"),        oh.Subsystem.ToString(), Str.Format("Executable.Overview.SubsystemVersionFormat", oh.SubsystemVersion)),
                new(Str.Get("Executable.Inspector.DLLCharacteristics"), Flags(oh.DllCharacteristics), $"0x{(ushort)oh.DllCharacteristics:X4}"),
                new(Str.Get("Executable.Inspector.Checksum"),         $"0x{oh.CheckSum:X8}"),
            ]));

        OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.DataDirectories"),
            image.DataDirectories.Where(d => d.IsPresent).Select(d => new InspectorRow(
                d.Kind.ToString(),
                $"RVA 0x{d.VirtualAddress:X8}",
                FormatSize(d.Size),
                // The security directory is the one whose "RVA" is really a file offset.
                d.Kind == PeDirectory.Security
                    ? Range(d.VirtualAddress, d.Size, Str.Get("Executable.Inspector.CertificateTable"))
                    : image.RvaToFileOffset(d.VirtualAddress) is { } off
                        ? Range(off, d.Size, d.Kind.ToString())
                        : null)))
            { Note = image.DataDirectories.All(d => !d.IsPresent) ? Str.Get("Executable.Inspector.NoDirectoriesArePresent") : null });

        OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.File"),
        [
            new(Str.Get("Executable.Inspector.Size"),    FormatSize(image.Length)),
            new("SHA-256", image.Sha256 ?? "—"),
            new("MD5",     image.Md5 ?? "—"),
            new("ImpHash", image.ImpHash ?? Str.Get("Executable.Inspector.NoImports")),
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
            new(Str.Get("Executable.Inspector.FileVersion"),    version.FileVersion),
            new(Str.Get("Executable.Inspector.ProductVersion"), version.ProductVersion),
        };
        foreach (var (name, value) in new[]
                 {
                     (Str.Get("Executable.Inspector.Company"),     version.CompanyName),
                     (Str.Get("Executable.Inspector.Description"), version.FileDescription),
                     (Str.Get("Executable.Inspector.Product"),     version.ProductName),
                     (Str.Get("Executable.Inspector.OriginalName"), version.OriginalFilename),
                     (Str.Get("Executable.Inspector.InternalName"), version.InternalName),
                     (Str.Get("Executable.Inspector.Copyright"),   version.LegalCopyright),
                 })
            if (value is { Length: > 0 }) rows.Add(new InspectorRow(name, value));

        if (version.IsDebugBuild)  rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Build"), Str.Get("Executable.Inspector.MarkedAsADebugBuild")) { StatusBrushKey = "WarningBrush" });
        if (version.IsPrerelease)  rows.Add(new InspectorRow(Str.Get("Executable.Inspector.Build"), Str.Get("Executable.Inspector.MarkedAsPreRelease"))   { StatusBrushKey = "WarningBrush" });

        OverviewCards.Add(new InspectorCard(Str.Get("Executable.Inspector.VersionInfo"), rows));
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
                section.Name.Length > 0 ? section.Name : Str.Get("Executable.Unnamed"),
                Str.Format("Executable.Sections.DetailFormat", section.Permissions, section.VirtualAddress,
                           FormatSize(section.VirtualSize), FormatSize(section.RawSize), section.RawPointer) +
                (section.Entropy is { } h ? $"  H={h:F2}" : ""),
                section.RawSize > 0 ? Range(section.RawPointer, section.RawSize, section.Name) : null)
            { Payload = section };

            node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.VirtualAddress"), $"0x{section.VirtualAddress:X8}"));
            node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.VirtualSize"),    $"0x{section.VirtualSize:X8} ({FormatSize(section.VirtualSize)})"));
            node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.RawOffset"),      $"0x{section.RawPointer:X8}",
                section.RawSize > 0 ? Range(section.RawPointer, section.RawSize, section.Name) : null));
            node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.RawSize"),        $"0x{section.RawSize:X8} ({FormatSize(section.RawSize)})"));
            node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.Characteristics"), $"{Flags(section.Characteristics)} (0x{(uint)section.Characteristics:X8})"));
            if (section.Entropy is { } entropy)
                node.Children.Add(new InspectorNode(Str.Get("Executable.Inspector.Entropy"), Str.Format("Executable.Sections.EntropyFormat", entropy)));
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
                ? Str.Get("Executable.Inspector.RelocationsWereStrippedTheImageMust")
                : Str.Get("Executable.Inspector.NoBaseRelocations");
            return;
        }

        RelocationSummary =
            Str.Format("Executable.Relocations.SummaryFormat", relocations.Blocks.Count, relocations.TotalFixups,
                       FormatSize(relocations.TotalBytes),
                       string.Join(", ", relocations.CountsByType.Select(k => $"{k.Key} × {k.Value:N0}")));

        foreach (var block in relocations.Blocks.Take(500))
            RelocationRows.Add(new InspectorRow(
                Str.Format("Executable.Relocations.PageFormat", block.PageRva),
                Str.Format("Executable.Relocations.FixupsFormat", block.FixupCount),
                FormatSize(block.BlockSize),
                image.RvaToFileOffset(block.PageRva) is { } offset
                    ? Range(offset, Math.Min(4096, image.Length - offset), Str.Format("Executable.Relocations.RangeFormat", block.PageRva))
                    : null));

        if (relocations.Blocks.Count > 500)
            RelocationRows.Add(new InspectorRow("…",
                Str.Format("Executable.Relocations.FurtherFormat", relocations.Blocks.Count - 500)));
    }

    // ── Imports / exports ─────────────────────────────────────────────────────

    private void BuildImportsExports(PeImage image)
    {
        int functions = image.Imports.Sum(m => m.Functions.Count);
        ImportSummary = image.Imports.Count == 0
            ? Str.Get("Executable.Inspector.ThisImageImportsNothing")
            : image.ImpHash is { } h ? Str.Format("Executable.Imports.SummaryImpHashFormat", image.Imports.Count, functions, h)
                                      : Str.Format("Executable.Imports.SummaryFormat", image.Imports.Count, functions);

        foreach (var group in new[]
                 {
                     (Str.Get("Executable.Inspector.Imports"), image.Imports),
                     (Str.Get("Executable.Inspector.DelayLoaded"), image.DelayImports),
                     (Str.Get("Executable.Inspector.Bound"), image.BoundImports),
                 })
        {
            if (group.Item2.Count == 0) continue;

            var header = new InspectorNode(group.Item1, Str.Format("Executable.Imports.ModulesFormat", group.Item2.Count)) { IsExpanded = true };
            foreach (var module in group.Item2)
            {
                var moduleNode = new InspectorNode(module.Name,
                    module.Functions.Count > 0
                        ? (module.IsApiSet ? Str.Format("Executable.Imports.FunctionsApiSetFormat", module.Functions.Count) : Str.Format("Executable.Imports.FunctionsFormat", module.Functions.Count))
                        : module.Kind == PeImportKind.Bound ? Str.Get("Executable.Imports.Bound") : Str.Get("Executable.Inspector.NoFunctionsListed"))
                { IsExpanded = false, Payload = module };

                foreach (var function in module.Functions)
                    moduleNode.Children.Add(new InspectorNode(
                        function.Display,
                        function.IsByOrdinal
                            ? Str.Format("Executable.Imports.OrdinalFormat", function.Ordinal)
                            : Str.Format("Executable.Imports.HintFormat", function.Hint, function.IatRva),
                        function.IatRva != 0 && image.RvaToFileOffset(function.IatRva) is { } iat
                            ? Range(iat, image.Is64Bit ? 8 : 4, $"{module.Name}!{function.Display}")
                            : null));

                header.Children.Add(moduleNode);
            }
            ImportNodes.Add(header);
        }

        var exports = image.Exports;
        ExportSummary = exports.Entries.Count == 0
            ? Str.Get("Executable.Inspector.ThisImageExportsNothing")
            : Str.Format("Executable.Exports.SummaryFormat", exports.DllName ?? Str.Get("Executable.Unnamed"), exports.Entries.Count,
                         exports.Entries.Count(e => e.IsForwarder), exports.Entries.Count(e => e.IsByOrdinal));

        foreach (var entry in exports.Entries.Take(5000))
            ExportRows.Add(new InspectorRow(
                entry.Display,
                entry.IsForwarder ? $"→ {entry.ForwarderTo}" : $"0x{entry.Rva:X8}",
                Str.Format("Executable.Imports.OrdinalFormat", entry.Ordinal),
                !entry.IsForwarder && image.RvaToFileOffset(entry.Rva) is { } offset
                    ? Range(offset, 64, entry.Display)
                    : null));

        if (exports.Entries.Count > 5000)
            ExportRows.Add(new InspectorRow("…", Str.Format("Executable.Exports.FurtherFormat", exports.Entries.Count - 5000)));

        if (exports.IsComSelfRegistering || image.Resources.HasTypeLib)
        {
            var parts = new List<string>();
            if (exports.IsComSelfRegistering)
                parts.Add(Str.Format("Executable.Exports.ComServerFormat", string.Join(", ", exports.ComEntryPoints)));
            if (image.Resources.HasTypeLib) parts.Add(Str.Get("Executable.Exports.TypeLibrary"));
            ComSummary = Str.Format("Executable.Exports.ComFormat", string.Join("; ", parts));
        }
    }

    // ── Resources ─────────────────────────────────────────────────────────────

    private void BuildResources(PeImage image)
    {
        if (image.Resources.IsEmpty)
        {
            ResourceSummary = Str.Get("Executable.Inspector.ThisImageHasNoResourceDirectory");
            return;
        }

        int leaves = image.Resources.Types.SelectMany(t => t.Descend()).Count(n => n.IsLeaf);
        ResourceSummary = Str.Format("Executable.Resources.SummaryFormat", image.Resources.Types.Count, leaves);

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
            ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.Manifest"),
                [new InspectorRow(Str.Get("Executable.Inspector.CouldNotBeParsed"), error) { StatusBrushKey = "DangerBrush" }])
            { Note = Str.Get("Executable.Inspector.TheRawXMLIsShownBelow") });
            ShowRawManifest = true;
            return;
        }

        var identity = new List<InspectorRow>();
        if (manifest.IsExternal)
            identity.Add(new InspectorRow(Str.Get("Executable.Inspector.Source"), Str.Get("Executable.Inspector.ExternalManifestFileBesideTheBinary")));
        foreach (var (label, value) in new[]
                 {
                     (Str.Get("Executable.Inspector.Name"),         manifest.AssemblyName),
                     (Str.Get("Executable.Inspector.Version"),      manifest.AssemblyVersion),
                     (Str.Get("Executable.Inspector.Architecture"), manifest.ProcessorArchitecture),
                     (Str.Get("Executable.Inspector.Type"),         manifest.AssemblyType),
                     (Str.Get("Executable.Inspector.PublicKeyToken"), manifest.PublicKeyToken),
                     (Str.Get("Executable.Inspector.Description"),  manifest.Description),
                 })
            if (value is { Length: > 0 }) identity.Add(new InspectorRow(label, value));
        if (identity.Count > 0) ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.Identity"), identity));

        ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.ElevationUAC"),
        [
            new InspectorRow(Str.Get("Executable.Inspector.RequestedLevel"), manifest.ExecutionLevel switch
            {
                PeExecutionLevel.AsInvoker            => Str.Get("Executable.Inspector.AsInvokerRunsAsTheInvokingUser"),
                PeExecutionLevel.HighestAvailable     => Str.Get("Executable.Inspector.HighestAvailableElevatesIfTheUserCan"),
                PeExecutionLevel.RequireAdministrator => Str.Get("Executable.Inspector.RequireAdministratorAlwaysElevates"),
                _ => Str.Get("Executable.Inspector.NotDeclaredSubjectToInstallerDetection"),
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
                ? Str.Get("Executable.Inspector.TrueMayDriveTheUIOf")
                : "false"),
        ]));

        ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.OSCompatibility"),
            manifest.SupportedOs.Count == 0
                ? [new InspectorRow(Str.Get("Executable.Inspector.DeclaredSupport"), Str.Get("Executable.Inspector.NoneTheBinaryIsShimmedAs"))
                    { StatusBrushKey = "WarningBrush" }]
                : manifest.SupportedOs
                    .Select(os => new InspectorRow(os.Name ?? Str.Get("Executable.Inspector.Unrecognised"), os.Id))
                    .Append(new InspectorRow(Str.Get("Executable.Inspector.Shims"),
                        manifest.RunsUnderCompatibilityShims
                            ? Str.Get("Executable.Inspector.NoWindows1011EntryCompatibility")
                            : Str.Get("Executable.Inspector.Windows1011DeclaredNoVersion"))
                    { StatusBrushKey = manifest.RunsUnderCompatibilityShims ? "WarningBrush" : null })
                    .ToList()));

        var settings = new List<InspectorRow>
        {
            new(Str.Get("Executable.Inspector.DPIAwareness"), manifest.DpiAwareness switch
            {
                PeDpiAwareness.PerMonitorV2 => Str.Get("Executable.Inspector.PerMonitorV2TheModernFully"),
                PeDpiAwareness.PerMonitor   => Str.Get("Executable.Inspector.PerMonitorV1"),
                PeDpiAwareness.System       => Str.Get("Executable.Inspector.SystemBitmapScaledOnSecondaryDisplays"),
                PeDpiAwareness.Unaware      => Str.Get("Executable.Inspector.UnawareAlwaysBitmapScaled"),
                _                           => Str.Get("Executable.Inspector.NotDeclared2"),
            }),
            new(Str.Get("Executable.Inspector.LongPaths"), manifest.LongPathAware ? Str.Get("Executable.Inspector.Enabled") : Str.Get("Executable.Inspector.NotEnabled")),
        };
        if (manifest.ActiveCodePage is { Length: > 0 } codePage)
            settings.Add(new InspectorRow(Str.Get("Executable.Inspector.ActiveCodePage"), codePage));

        // Anything the decoder does not model is still listed, so a newer setting is never lost.
        foreach (var (key, value) in manifest.WindowsSettings)
            if (key is not ("dpiAware" or "dpiAwareness" or "longPathAware" or "activeCodePage"))
                settings.Add(new InspectorRow(key, value));
        ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.WindowsSettings"), settings));

        if (manifest.Dependencies.Count > 0)
            ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.DependentAssemblies"),
                manifest.Dependencies.Select(d => new InspectorRow(
                    d.Name,
                    d.Version is { Length: > 0 } ? Str.Format("Executable.Manifest.VersionFormat", d.Version) : "",
                    string.Join("  ", new[] { d.ProcessorArchitecture, d.PublicKeyToken, d.Type }
                        .Where(s => s is { Length: > 0 }))))));

        if (manifest.HasRegistrationFreeCom)
            ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.RegistrationFreeCOM"),
            [
                new(Str.Get("Executable.Inspector.COMClasses"),   manifest.ComClassCount.ToString()),
                new(Str.Get("Executable.Inspector.TypeLibraries"), manifest.TypeLibCount.ToString()),
                new(Str.Get("Executable.Inspector.WindowClasses"), manifest.WindowClassCount.ToString()),
                new(Str.Get("Executable.Inspector.ProxyStubs"),    manifest.ProxyStubCount.ToString()),
            ]));

        if (manifest.Other.Count > 0)
            ManifestCards.Add(new InspectorCard(Str.Get("Executable.Inspector.OtherElements"),
                manifest.Other.Select(name => new InspectorRow(name, Str.Get("Executable.Inspector.NotDecodedSeeTheRawXML"))))
            { Note = Str.Get("Executable.Inspector.PresentInTheManifestButNot") });
    }

    // ── .NET ──────────────────────────────────────────────────────────────────

    private void BuildDotnet(PeImage image)
    {
        var clr = image.Clr;
        IsManaged = clr.IsManaged;
        if (!IsManaged) return;

        DotnetCards.Add(new InspectorCard(Str.Get("Executable.Inspector.CLRHeader"),
        [
            new(Str.Get("Executable.Inspector.RuntimeVersion"), clr.RuntimeVersion ?? "—"),
            new(Str.Get("Executable.Inspector.MetadataVersion"), clr.MetadataVersion ?? "—"),
            new(Str.Get("Executable.Inspector.Flags"), clr.Flags == PeClrFlags.None ? Str.Get("Executable.Inspector.None") : clr.Flags.ToString()),
            new(Str.Get("Executable.Inspector.Bitness"), clr.Bitness),
            new(Str.Get("Executable.Inspector.ILOnly"), clr.IsIlOnly ? Str.Get("Executable.Inspector.Yes") : Str.Get("Executable.Inspector.NoContainsNativeCode")),
            new(Str.Get("Executable.Inspector.StrongNameSigned"), clr.IsStrongNameSigned ? Str.Get("Executable.Inspector.Yes") : Str.Get("Executable.Inspector.No")),
            new(Str.Get("Executable.Inspector.EntryPointToken"), $"0x{clr.EntryPointToken:X8}"),
            new(Str.Get("Executable.Inspector.Metadata"), $"RVA 0x{clr.MetadataRva:X8}", FormatSize(clr.MetadataSize),
                image.RvaToFileOffset(clr.MetadataRva) is { } offset
                    ? Range(offset, clr.MetadataSize, Str.Get("Executable.Inspector.CLRMetadata")) : null),
        ]));

        var identity = new List<InspectorRow>
        {
            new(Str.Get("Executable.Inspector.Assembly"), clr.AssemblyName ?? Str.Get("Executable.Inspector.NotAnAssembly")),
            new(Str.Get("Executable.Inspector.Version"),  clr.AssemblyVersion ?? "—"),
            new(Str.Get("Executable.Inspector.Culture"),  clr.AssemblyCulture ?? Str.Get("Executable.Dotnet.Neutral")),
            new(Str.Get("Executable.Inspector.PublicKeyToken"), clr.PublicKeyToken ?? Str.Get("Executable.Inspector.Unsigned")),
            new(Str.Get("Executable.Inspector.TargetFramework"), clr.TargetFramework ?? Str.Get("Executable.Inspector.NotDeclared")),
        };
        if (clr.IsWindowsRuntime)
            identity.Add(new InspectorRow(Str.Get("Executable.Inspector.WindowsRuntime"),
                Str.Get("Executable.Inspector.ThisIsWinRTMetadataWinmdNot")));
        DotnetCards.Add(new InspectorCard(Str.Get("Executable.Inspector.Assembly"), identity));

        DotnetCards.Add(new InspectorCard(Str.Get("Executable.Inspector.AssemblyReferences"),
            clr.AssemblyReferences.Select(r => new InspectorRow(
                r.Name, r.Version,
                string.Join("  ", new[] { r.Culture, r.PublicKeyToken }.Where(s => s is { Length: > 0 })))))
        { Note = clr.AssemblyReferences.Count == 0 ? Str.Get("Executable.Inspector.NoReferencedAssemblies") : null });
    }

    private static string Flags<T>(T value) where T : struct, Enum
    {
        string text = value.ToString() ?? "";
        return text is "0" or "None" ? Str.Get("Executable.Inspector.None") : text.Replace(", ", " · ");
    }
}
