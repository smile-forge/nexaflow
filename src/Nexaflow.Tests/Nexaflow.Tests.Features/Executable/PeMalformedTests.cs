using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.IO.Pe;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Executable;

/// <summary>
/// The tolerance contract. An inspector's normal input includes packed, truncated and deliberately
/// hostile binaries, so <see cref="PeReader"/> promises never to throw — every failure becomes a
/// diagnostic and whatever else parsed still comes back. These tests are that promise.
/// </summary>
[TestClass]
[CoversNode("functionality-2")]
public sealed class PeMalformedTests
{
    [TestMethod, TestCategory("Unit")]
    public void A_file_with_no_mz_signature_is_reported_not_thrown()
    {
        using var image = PeReader.Read(PeFixtures.NotAPe());

        Assert.IsFalse(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Any(d => d.Severity == PeSeverity.Error && d.Area == "DosHeader"),
            "The missing MZ signature should be reported against the DOS header.");
    }

    [TestMethod, TestCategory("Unit")]
    public void An_mz_stub_with_no_pe_header_is_reported()
    {
        using var image = PeReader.Read(PeFixtures.MzOnly());

        Assert.IsFalse(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "NtHeader"));
    }

    [TestMethod, TestCategory("Unit")]
    public void An_nt_offset_past_the_end_of_the_file_is_reported()
    {
        using var image = PeReader.Read(PeFixtures.BadNtOffset());

        Assert.IsFalse(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "NtHeader"));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_truncated_image_keeps_the_headers_it_did_parse()
    {
        using var image = PeReader.Read(PeFixtures.Truncated());

        Assert.IsTrue(image.IsPe, "The headers survived, so the image is still recognisably a PE.");
        Assert.IsTrue(image.Sections.Count > 0, "The section table was inside the surviving prefix.");
        Assert.IsTrue(image.Diagnostics.Any(d => d.Severity == PeSeverity.Warning),
            "Sections running past the end of the file should be warned about.");
    }

    [TestMethod, TestCategory("Unit")]
    public void An_absurd_section_count_is_clamped_to_the_architectural_maximum()
    {
        using var image = PeReader.Read(PeFixtures.InsaneSectionCount());

        // Clamping to 96 rather than to "whatever fits in the file" is what stops a 0xFFFF count on
        // a small image producing thousands of junk sections for the UI to render.
        Assert.IsTrue(image.Sections.Count <= 96, $"Got {image.Sections.Count} sections; the maximum is 96.");
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "Sections"));
        Assert.IsTrue(image.Diagnostics.Count < 100, "Diagnostics must not balloon with the bad count.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Dangling_directories_lose_only_their_own_structures()
    {
        using var image = PeReader.Read(PeFixtures.DanglingDirectories());

        Assert.IsTrue(image.IsPe);
        Assert.AreEqual(0, image.Imports.Count);
        Assert.IsTrue(image.Resources.IsEmpty);
        Assert.IsTrue(image.Sections.Count > 0, "The section table is untouched and must survive.");
        Assert.IsTrue(image.Entropy.Overall > 0, "Entropy does not depend on the directories.");
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "Imports"));
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "Resources"));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_section_running_past_the_end_of_the_file_is_clamped()
    {
        using var image = PeReader.Read(PeFixtures.SectionPastEof());

        Assert.IsTrue(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "Sections"));

        // The invariant is that no section claims readable bytes outside the file. A pointer that is
        // itself out of range keeps its bogus value — there is nothing truthful to change it to —
        // but its size is driven to zero so nothing will ever be read from there.
        foreach (var section in image.Sections)
        {
            if (section.RawSize == 0) continue;
            Assert.IsTrue(section.RawPointer + (long)section.RawSize <= image.Length,
                $"'{section.Name}' claims {section.RawSize} readable bytes at 0x{section.RawPointer:X} " +
                $"in a {image.Length}-byte file.");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void A_self_referential_resource_directory_terminates()
    {
        // Left unguarded this recurses until the stack runs out — the reason the walk carries a
        // visited set as well as a depth cap.
        using var image = PeReader.Read(PeFixtures.ResourceCycle());

        Assert.IsTrue(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Any(d => d.Area == "Resources"),
            "The loop should be reported rather than followed.");
    }

    [TestMethod, TestCategory("Unit")]
    public void An_empty_input_is_handled()
    {
        using var image = PeReader.Read(System.ReadOnlyMemory<byte>.Empty);

        Assert.IsFalse(image.IsPe);
        Assert.IsTrue(image.Diagnostics.Count > 0);
    }
}
