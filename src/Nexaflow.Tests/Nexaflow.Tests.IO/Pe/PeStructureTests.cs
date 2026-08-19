using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.IO.Pe;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Pe;

/// <summary>Imports, exports, resources, version info, relocations, debug, CLR metadata and entropy.</summary>
[TestClass]
[CoversNode("functionality-2")]
public sealed class PeStructureTests
{
    // ── Imports / exports ─────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Imports_are_grouped_by_module_with_named_functions()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsTrue(image.Imports.Count > 0, "notepad.exe imports from several modules.");
        var user32 = image.Imports.FirstOrDefault(m => m.Name.Equals("USER32.dll", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(user32, "A GUI application imports from USER32.");
        Assert.IsTrue(user32!.Functions.Count > 0);
        Assert.IsTrue(user32.Functions.Any(f => f.Name is { Length: > 0 }), "Names should resolve, not just ordinals.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Api_set_imports_are_recognised()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        var apiSets = image.Imports.Where(m => m.IsApiSet).ToList();
        Assert.IsTrue(apiSets.Count > 0, "Modern in-box binaries import through API sets.");
        Assert.IsTrue(apiSets.All(m => m.Name.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
                                       m.Name.StartsWith("ext-ms-",     StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod, TestCategory("Unit")]
    public void Delay_loaded_imports_are_kept_separate_from_ordinary_ones()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsTrue(image.DelayImports.Count > 0, "notepad.exe delay-loads several modules.");
        Assert.IsTrue(image.DelayImports.All(m => m.IsDelayLoad));
        Assert.IsFalse(image.Imports.Any(m => m.IsDelayLoad), "The ordinary table must not contain delay entries.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Exports_include_forwarders_resolved_to_their_targets()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        Assert.IsTrue(image.Exports.Entries.Count > 100, "kernel32 exports a great many symbols.");
        var forwarders = image.Exports.Entries.Where(e => e.IsForwarder).ToList();
        Assert.IsTrue(forwarders.Count > 0, "kernel32 forwards a large share of its exports to ntdll.");
        Assert.IsTrue(forwarders.All(e => e.ForwarderTo!.Contains('.')),
            "A forwarder reads as TARGETDLL.Function.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Imphash_is_stable_across_reads()
    {
        using var first  = PeReader.Read(PeFixtures.Notepad);
        using var second = PeReader.Read(PeFixtures.Notepad);

        Assert.IsNotNull(first.ImpHash);
        Assert.AreEqual(32, first.ImpHash!.Length, "An MD5 renders as 32 hex characters.");
        Assert.AreEqual(first.ImpHash, second.ImpHash);
    }

    [TestMethod, TestCategory("Unit")]
    public void An_image_with_no_imports_has_no_imphash()
    {
        using var image = PeReader.Read(PeFixtures.DanglingDirectories());
        Assert.IsNull(image.ImpHash);
    }

    // ── Resources ─────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void The_resource_tree_exposes_the_manifest_and_the_icons()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsFalse(image.Resources.IsEmpty);
        Assert.IsTrue(image.Resources.HasType(PeResourceTypes.Manifest), "notepad.exe embeds a manifest.");
        Assert.IsTrue(image.Resources.HasType(PeResourceTypes.GroupIcon), "notepad.exe has an icon.");
        Assert.IsTrue(image.Resources.HasType(PeResourceTypes.Version));
    }

    [TestMethod, TestCategory("Unit")]
    public void An_icon_group_reassembles_into_a_valid_ico()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        var group = PeIcons.Primary(image);
        Assert.IsNotNull(group, "notepad.exe has at least one icon group.");
        Assert.IsTrue(group!.ImageCount > 0);

        // An .ico is a 6-byte ICONDIR: reserved 0, type 1, then the image count.
        var bytes = group.IcoBytes;
        Assert.IsTrue(bytes.Length > 6);
        Assert.AreEqual(0, bytes[0] | bytes[1]);
        Assert.AreEqual(1, bytes[2] | (bytes[3] << 8));
        Assert.AreEqual(group.ImageCount, bytes[4] | (bytes[5] << 8));
    }

    // ── Version info ──────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Version_info_decodes_the_fixed_block_and_the_strings()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        var version = image.Version;
        Assert.IsFalse(version.IsEmpty, "notepad.exe carries version info.");
        Assert.IsTrue(version.FileVersion.Contains('.'), "The file version is dotted quad.");
        Assert.IsTrue(version.Strings.Count > 0, "There should be at least one language block.");

        // Strings are counted in characters, not bytes — read as bytes they come out half-length.
        Assert.AreEqual("Microsoft Corporation", version.CompanyName);
        Assert.IsTrue(version.OriginalFilename is { Length: > 0 });
    }

    // ── Relocations, debug, TLS ───────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Relocations_parse_into_page_blocks()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsFalse(image.Relocations.IsEmpty, "An ASLR-enabled image carries base relocations.");
        Assert.IsTrue(image.Relocations.TotalFixups > 0);
        Assert.IsTrue(image.Relocations.CountsByType.ContainsKey(PeRelocationType.Dir64),
            "A 64-bit image uses DIR64 fixups.");
        Assert.IsTrue(image.Relocations.Blocks.All(b => b.BlockSize >= 8));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_debug_directory_yields_the_pdb_identity()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsTrue(image.Debug.Entries.Any(e => e.Type == PeDebugType.CodeView));
        Assert.IsTrue(image.Debug.PdbPath is { Length: > 0 }, "The CodeView record names a PDB.");
        Assert.IsNotNull(image.Debug.PdbGuid);
        Assert.IsNotNull(image.Debug.PdbAge);
    }

    [TestMethod, TestCategory("Unit")]
    public void An_absolute_pdb_path_is_flagged_as_a_build_path_leak()
    {
        // A locally built assembly records the full obj path; that discloses the build machine.
        using var image = PeReader.Read(PeFixtures.ManagedAssembly);

        Assert.IsTrue(image.Debug.PdbPath is { Length: > 0 });
        Assert.IsTrue(image.Debug.LeaksBuildPath,
            $"'{image.Debug.PdbPath}' is absolute and should be flagged.");
    }

    // ── CLR ───────────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_managed_assembly_reports_its_metadata()
    {
        using var image = PeReader.Read(PeFixtures.ManagedAssembly);

        var clr = image.Clr;
        Assert.IsTrue(clr.IsManaged);
        Assert.IsTrue(clr.IsIlOnly, "A normal C# library is IL-only.");
        Assert.AreEqual("Nexaflow.IO.Pe", clr.AssemblyName);
        Assert.IsTrue(clr.TargetFramework is { Length: > 0 }, "TargetFrameworkAttribute should decode.");
        Assert.IsTrue(clr.TargetFramework!.Contains(".NETCoreApp"));
        Assert.IsTrue(clr.AssemblyReferences.Count > 0);
        Assert.IsTrue(clr.AssemblyReferences.Any(r => r.Name == "System.Runtime"));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_native_binary_reports_no_clr_header()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        Assert.IsFalse(image.Clr.IsManaged);
        Assert.IsFalse(image.Clr.IsWindowsRuntime);
    }

    // ── Entropy ───────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Shannon_entropy_spans_zero_to_eight()
    {
        Assert.AreEqual(0.0, PeEntropy.Shannon(new byte[4096]), 1e-9,
            "A single repeated byte carries no information.");
        Assert.AreEqual(0.0, PeEntropy.Shannon([]), 1e-9);

        // Every byte value equally often is the maximum: exactly 8 bits per byte.
        var uniform = new byte[256 * 16];
        for (int i = 0; i < uniform.Length; i++) uniform[i] = (byte)(i % 256);
        Assert.AreEqual(8.0, PeEntropy.Shannon(uniform), 1e-9);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_entropy_sweep_covers_the_file_in_range()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsTrue(image.Entropy.Buckets.Count > 0);
        Assert.IsTrue(image.Entropy.Overall is > 0 and <= 8);
        Assert.IsTrue(image.Entropy.Buckets.All(b => b is >= 0 and <= 8));
        Assert.IsTrue(image.Sections.Where(s => s.RawSize > 0).All(s => s.Entropy is >= 0 and <= 8));
    }
}
