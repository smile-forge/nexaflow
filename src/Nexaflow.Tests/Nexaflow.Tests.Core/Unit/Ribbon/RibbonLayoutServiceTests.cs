using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Tests.Core.Unit.Ribbon;

/// <summary>
/// <c>ribbon.json</c> is a user's hand-made ribbon, so a new version must never lose one. A version-1 file (the bare
/// item array, one <c>AccentColor</c> hex) must read as the same buttons with that colour on their icon and text;
/// everything version 2 adds must survive a save and load; and the shipped defaults must read in either shape.
/// </summary>
[TestClass]
[CoversNode("ribbon-layout-sync")]
public class RibbonLayoutServiceTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-ribbon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string RibbonFile => Path.Combine(_dir, "ribbon.json");

    [TestMethod]
    public void VersionOne_ReadsAccentAsForeground()
    {
        File.WriteAllText(RibbonFile, """
            [
              { "Kind": "Button", "Label": "Docs", "Icon": "📄", "AccentColor": "#FF4F8EF7", "PageKind": "FileSystem",
                "PageParams": { "mode": "path", "path": "C:\\Docs" } },
              { "Kind": "Separator" },
              { "Kind": "Button", "Label": "Chat", "Icon": "💬", "IsHalf": true, "PageKind": "AIChat" }
            ]
            """);

        var items = new RibbonLayoutService(_dir).Load()!;

        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(IconRef.Emoji("📄"), items[0].Icon);
        Assert.AreEqual(ColorSpec.Custom(Color.FromArgb(0xFF, 0x4F, 0x8E, 0xF7)), items[0].Foreground);
        Assert.IsTrue(items[0].Background.IsDefault);
        Assert.AreEqual(RibbonButtonShape.Standard, items[0].Shape);
        Assert.AreEqual("C:\\Docs", items[0].PageParams!["path"]);
        Assert.AreEqual(RibbonItemKind.Separator, items[1].Kind);
        Assert.IsTrue(items[2].IsHalf);
        Assert.IsTrue(items[2].Foreground.IsDefault);
    }

    [TestMethod]
    public void VersionOne_IsWrittenBackAsVersionTwo()
    {
        File.WriteAllText(RibbonFile, """[ { "Kind": "Button", "Label": "Docs", "Icon": "📄", "AccentColor": "#FF112233" } ]""");
        var service = new RibbonLayoutService(_dir);

        service.Save(service.Load()!);

        var json = File.ReadAllText(RibbonFile);
        StringAssert.Contains(json, "\"Version\": 2");
        StringAssert.Contains(json, "\"Foreground\": \"#FF112233\"");
        Assert.IsFalse(json.Contains("AccentColor"), json);
    }

    [TestMethod]
    public void VersionTwo_RoundTripsEveryLookProperty()
    {
        var service = new RibbonLayoutService(_dir);
        service.Save([
            new RibbonItem
            {
                Label        = "Home",
                Icon         = IconRef.Fluent("home", filled: true),
                IsHalf       = true,
                Foreground   = ColorSpec.FromSwatch("Teal"),
                Background   = ColorSpec.Custom(Color.FromArgb(0x80, 1, 2, 3)),
                BorderColor  = ColorSpec.FromSwatch("Pink"),
                BorderWeight = RibbonBorderWeight.Medium,
                Shape        = RibbonButtonShape.Squircle,
                PageKind     = "Projects",
            },
            new RibbonItem { Kind = RibbonItemKind.Separator },
        ]);

        var back = service.Load()!;
        var home = back[0];
        Assert.AreEqual(2, back.Count);
        Assert.AreEqual("Home", home.Label);
        Assert.AreEqual(IconRef.Fluent("home", filled: true), home.Icon);
        Assert.IsTrue(home.IsHalf);
        Assert.AreEqual(ColorSpec.FromSwatch("Teal"), home.Foreground);
        Assert.AreEqual(ColorSpec.Custom(Color.FromArgb(0x80, 1, 2, 3)), home.Background);
        Assert.AreEqual(ColorSpec.FromSwatch("Pink"), home.BorderColor);
        Assert.AreEqual(RibbonBorderWeight.Medium, home.BorderWeight);
        Assert.AreEqual(RibbonButtonShape.Squircle, home.Shape);
        Assert.AreEqual("Projects", home.PageKind);
        Assert.AreEqual(RibbonItemKind.Separator, back[1].Kind);
    }

    [TestMethod]
    public void AnUnstyledButton_WritesNoLook()
    {
        var service = new RibbonLayoutService(_dir);
        service.Save([new RibbonItem { Label = "Plain", Icon = IconRef.Emoji("📁") }]);

        var json = File.ReadAllText(RibbonFile);
        foreach (var name in new[] { "Foreground", "Background", "BorderColor", "BorderWeight", "Shape" })
            Assert.IsFalse(json.Contains(name), $"{name} written for an unstyled button:\n{json}");
    }

    [TestMethod]
    public void Corrupt_LoadsAsNothing()
    {
        File.WriteAllText(RibbonFile, "{ not json");
        Assert.IsNull(new RibbonLayoutService(_dir).Load());
    }

    [TestMethod]
    public void Missing_LoadsAsNothing() => Assert.IsNull(new RibbonLayoutService(_dir).Load());

    [TestMethod]
    [CoversNode("ribbon-defaults")]
    public void ShippedDefaults_ReadAndDrawTheirIcons()
    {
        var defaults = RibbonLayoutService.LoadDefaults();

        Assert.IsTrue(defaults.Count > 0, "default-ribbon.json did not load");
        foreach (var button in defaults.Where(d => d.Kind == RibbonItemKind.Button))
        {
            Assert.IsFalse(button.Icon.IsEmpty, button.Label);
            Assert.IsTrue(IconCatalog.Contains(button.Icon), button.Label);
        }
    }
}
