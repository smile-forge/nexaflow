using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hex;

/// <summary>
/// Opening the hex viewer <em>at</em> a byte range. The route is the shell's generic object
/// dispatch — the same one a clicked post-it link takes — so a caller says "show me these bytes"
/// without naming the Hex feature at all.
/// </summary>
[TestClass]
[CoversNode("hex")]
public sealed class HexOffsetTests
{
    [TestMethod, TestCategory("Unit")]
    public void The_handler_claims_a_byte_range_for_a_real_file()
    {
        var handler = new HexObjectHandler(Substitute.For<IShellServices>());
        var range   = new FileByteRange(PeFixturePath, 0x1000, 512, ".text");

        Assert.IsTrue(handler.CanHandleObject(range));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_handler_ignores_payloads_that_are_not_byte_ranges()
    {
        var handler = new HexObjectHandler(Substitute.For<IShellServices>());

        // A bare path belongs to the file-system handler; claiming it here would hijack every
        // ordinary "open this file" dispatch.
        Assert.IsFalse(handler.CanHandleObject(PeFixturePath));
        Assert.IsFalse(handler.CanHandleObject("https://example.com"));
        Assert.IsFalse(handler.CanHandleObject(42));
        Assert.IsFalse(handler.CanHandleObject(new FileByteRange(@"C:\nope\missing.bin", 0, 1)));
        Assert.IsFalse(handler.CanHandleObject(new FileByteRange(PeFixturePath, -1, 0)));
    }

    [TestMethod, TestCategory("Unit")]
    public void Handling_a_range_opens_hex_with_the_offset_and_length()
    {
        var shell   = Substitute.For<IShellServices>();
        var handler = new HexObjectHandler(shell);

        handler.Handle(new FileByteRange(PeFixturePath, 0x2A00, 256));

        shell.Received(1).OpenTab(
            HexTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(p =>
                p["path"] == PeFixturePath && p["offset"] == "10752" && p["length"] == "256"),
            Arg.Any<IPageView>(), Arg.Any<bool>());
    }

    [TestMethod, TestCategory("Unit")]
    public void Offsets_parse_as_decimal_or_hex()
    {
        Assert.AreEqual(4096,  HexTabRegistration.ParseOffset("4096"));
        Assert.AreEqual(4096,  HexTabRegistration.ParseOffset("0x1000"));
        Assert.AreEqual(4096,  HexTabRegistration.ParseOffset("0X1000"));
        Assert.AreEqual(255,   HexTabRegistration.ParseOffset(" 0xFF "));
        Assert.AreEqual(0,     HexTabRegistration.ParseOffset("0"));
    }

    [TestMethod, TestCategory("Unit")]
    public void An_absent_or_unusable_offset_reads_as_none()
    {
        // -1 rather than 0, so "no offset given" is distinguishable from "the start of the file".
        Assert.AreEqual(-1, HexTabRegistration.ParseOffset(null));
        Assert.AreEqual(-1, HexTabRegistration.ParseOffset(""));
        Assert.AreEqual(-1, HexTabRegistration.ParseOffset("   "));
        Assert.AreEqual(-1, HexTabRegistration.ParseOffset("not-a-number"));
        Assert.AreEqual(-1, HexTabRegistration.ParseOffset("-5"));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_page_declares_its_optional_offset_parameters()
    {
        var registration = new HexTabRegistration(Substitute.For<IShellServices>());
        var names = new List<string>();
        foreach (var parameter in registration.Parameters) names.Add(parameter.Name);

        CollectionAssert.AreEquivalent(new[] { "path", "offset", "length" }, names);
        foreach (var parameter in registration.Parameters)
            if (parameter.Name != "path")
                Assert.IsFalse(parameter.Required, $"'{parameter.Name}' must stay optional.");
    }

    private static string PeFixturePath =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "notepad.exe");
}
