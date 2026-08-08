using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.IO.Pe;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Executable;

/// <summary>Headers, section table and address translation against real system binaries.</summary>
[TestClass]
[CoversNode("functionality-2")]
public sealed class PeReaderTests
{
    [TestMethod, TestCategory("Unit")]
    public void Reads_a_64_bit_system_dll()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        Assert.IsTrue(image.IsPe, "kernel32.dll should parse as a PE.");
        Assert.IsTrue(image.Is64Bit, "kernel32.dll is PE32+ on a 64-bit Windows install.");
        Assert.AreEqual(PeMachine.Amd64, image.Machine);
        Assert.IsTrue(image.IsDll, "The DLL characteristic should be set.");
        Assert.AreEqual(PeOptionalHeader.Pe32PlusMagic, image.OptionalHeader!.Magic);
        Assert.IsTrue(image.Sections.Count > 0, "A real image has sections.");
        Assert.IsNotNull(image.Sha256);
        Assert.AreEqual(64, image.Sha256!.Length, "SHA-256 renders as 64 lower-case hex characters.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Reads_an_executable_and_its_subsystem()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        Assert.IsTrue(image.IsPe);
        Assert.IsFalse(image.IsDll, "notepad.exe is not a DLL.");
        Assert.AreEqual(PeSubsystem.WindowsGui, image.OptionalHeader!.Subsystem);
        Assert.AreNotEqual(0u, image.OptionalHeader.AddressOfEntryPoint);
    }

    [TestMethod, TestCategory("Unit")]
    public void Section_rvas_translate_back_to_their_raw_offsets()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        foreach (var section in image.Sections.Where(s => s.RawSize > 0))
        {
            long? offset = image.RvaToFileOffset(section.VirtualAddress);
            Assert.AreEqual((long)section.RawPointer, offset,
                $"The start of '{section.Name}' should map to its raw pointer.");
        }
    }

    [TestMethod, TestCategory("Unit")]
    public void An_rva_outside_every_section_does_not_resolve()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        Assert.IsNull(image.RvaToFileOffset(0x7F00_0000),
            "An RVA in no section and beyond the headers has no file offset.");
    }

    [TestMethod, TestCategory("Unit")]
    public void The_text_section_is_executable_and_the_data_section_is_not()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32);

        var text = image.Sections.First(s => s.Name == ".text");
        Assert.IsTrue(text.IsExecutable, ".text must be executable.");
        Assert.IsTrue(text.IsCode);
        Assert.AreEqual("R-X", text.Permissions);
        Assert.IsFalse(text.IsWritableExecutable, "A stock system DLL has no W+X section.");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_reproducible_build_reports_no_build_timestamp()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        // In-box Windows binaries are built reproducibly, so the COFF timestamp is a content hash.
        // Surfacing it as a date is how an inspector ends up claiming a file was built in 2090.
        Assert.IsTrue(image.Debug.IsDeterministic, "In-box Windows binaries are reproducible builds.");
        Assert.IsNull(image.BuildTimestamp, "A hash must not be presented as a build time.");
        Assert.AreNotEqual(0u, image.CoffHeader!.TimeDateStamp, "The raw field is still populated.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Headers_only_skips_the_expensive_work()
    {
        using var image = PeReader.Read(PeFixtures.Kernel32, PeReadOptions.HeadersOnly);

        Assert.IsTrue(image.IsPe);
        Assert.IsTrue(image.Sections.Count > 0);
        Assert.AreEqual(0, image.Imports.Count, "Imports were not requested.");
        Assert.IsTrue(image.Resources.IsEmpty, "Resources were not requested.");
        Assert.IsNull(image.Sha256, "File hashes were not requested.");
    }
}
