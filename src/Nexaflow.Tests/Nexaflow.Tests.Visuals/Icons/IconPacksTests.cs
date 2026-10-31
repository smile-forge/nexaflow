using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>
/// The icon packs shipped with the app (<see cref="IconPacks"/>): found by their prefix, every icon of them read into outlines,
/// an alias drawn as the icon it names, and a pack's icon kept by an <see cref="IconRef"/> as Iconify writes it.
/// </summary>
[TestClass]
[CoversNode("icons-packs")]
public class IconPacksTests
{
    private const string Material = "material-icon-theme";

    [TestMethod, TestCategory("Unit")]
    public void TheMaterialIconThemeIsShipped_AndEveryIconOfItReadsIntoOutlines()
    {
        Assert.IsTrue(IconPacks.Ships(Material));
        CollectionAssert.Contains(IconPacks.Names.ToList(), Material);

        var names = IconPacks.Icons(Material).ToList();
        Assert.IsTrue(names.Count > 900, $"{names.Count} icons");

        var unread = names.Where(name => IconPacks.Of(Material, name) is not { Layers.Count: > 0, Width: > 0, Height: > 0 }).ToList();
        Assert.AreEqual(0, unread.Count, "these draw nothing: " + string.Join(", ", unread));
    }

    [TestMethod, TestCategory("Unit")]
    public void AnIconIsReadOnce_AndKept()
    {
        var rust = IconPacks.Of(IconRef.Iconify(Material, "rust"));

        Assert.IsNotNull(rust);
        Assert.AreSame(rust, IconPacks.Of(Material, "rust"));
        Assert.IsTrue(IconPacks.Has(Material, "rust"));
    }

    [TestMethod, TestCategory("Unit")]
    public void AnAliasIsTheIconItNames()
    {
        var alias = IconPacks.Of(Material, "oxlint");
        var named = IconPacks.Of(Material, "oxc");

        Assert.IsNotNull(alias);
        Assert.AreEqual((named!.Width, named.Height, named.Layers.Count), (alias.Width, alias.Height, alias.Layers.Count));
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatNoShippedPackHasIsNothing()
    {
        Assert.IsFalse(IconPacks.Ships("logos"));
        Assert.IsNull(IconPacks.Of("logos", "react"));
        Assert.IsNull(IconPacks.Of(Material, "no-such-icon"));
        Assert.IsNull(IconPacks.Of(IconRef.Fluent("folder")), "a Fluent icon is a glyph, not a pack's");
        Assert.AreEqual(0, IconPacks.Icons("logos").Count());
    }

    [TestMethod, TestCategory("Unit")]
    public void APacksIconIsKeptAsIconifyWritesIt_AndReadBack()
    {
        var rust = IconRef.Iconify(Material, "rust");

        Assert.AreEqual("iconify:material-icon-theme:rust", rust.ToString());
        Assert.AreEqual(rust, IconRef.Parse(rust.ToString()));
        Assert.IsTrue(rust.IsIconify);
        Assert.AreEqual(IconSet.Emoji, IconRef.Parse("material-icon-theme:rust").Set, "a bare string is an emoji, as it always was");
    }
}
