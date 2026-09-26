using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Tests.Core.Unit.Ribbon;

/// <summary>
/// The ribbon editor works on a draft. Nothing reaches the ribbon until Done, Cancel leaves it exactly as it was,
/// and every arranging and styling command acts on the draft alone.
/// </summary>
[TestClass]
[CoversNode("ribbon-editor-draft")]
public class RibbonEditorViewModelTests
{
    private List<RibbonItem> _live = null!;
    private IReadOnlyList<RibbonItem>? _committed;
    private bool _closed;

    [TestInitialize]
    public void Setup()
    {
        _live =
        [
            new RibbonItem { Label = "Projects", Icon = IconRef.Emoji("🗂"), PageKind = "Projects" },
            new RibbonItem { Kind = RibbonItemKind.Separator },
            new RibbonItem { Label = "Chat", Icon = IconRef.Emoji("💬"), PageKind = "AIChat" },
        ];
        _committed = null;
        _closed    = false;
    }

    private RibbonEditorViewModel Editor(params RibbonCatalogEntry[] pages) => new(
        _live, pages,
        loadDefaults: () => [new RibbonItem { Label = "Default", Icon = IconRef.Emoji("⭐") }],
        commit: items => _committed = items,
        close: () => _closed = true,
        findResource: key => key switch
        {
            "TextMutedBrush" or "TextBrush" => Brushes.White,
            "Ribbon.ButtonBg" or "BgBrush"  => Brushes.Black,
            _                               => null,
        });

    private static RibbonEditorCard Card(RibbonEditorViewModel vm, string label) => vm.Cards.Single(c => c.Item.Label == label);

    [TestMethod]
    public void Draft_IsACopy()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.SelectedItem!.Label = "Talk";
        vm.SelectedColour = ColorSpec.FromSwatch("Red");

        Assert.AreEqual("Chat", _live[2].Label);
        Assert.IsTrue(_live[2].Foreground.IsDefault);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-done")]
    public void Cancel_CommitsNothing()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.DeleteCommand.Execute(null);
        vm.CancelCommand.Execute(null);

        Assert.IsNull(_committed);
        Assert.IsTrue(_closed);
        Assert.AreEqual(3, _live.Count);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-done")]
    public void Done_CommitsTheDraftInOrder()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.MoveLeftCommand.Execute(null);
        vm.DoneCommand.Execute(null);

        Assert.IsTrue(_closed);
        CollectionAssert.AreEqual(new[] { "Projects", "Chat", "" }, _committed!.Select(i => i.Label).ToArray());
    }

    [TestMethod]
    [CoversNode("ribbon-editor-addpage")]
    public void AddPage_InsertsBeforeTheSelectionAndLeavesTheCatalog()
    {
        var page = new RibbonCatalogEntry("Notes", "📝", "Scratchpad");
        var vm = Editor(page);
        vm.Selected = Card(vm, "Chat");
        vm.AddPageCommand.Execute(null);

        Assert.AreEqual("Notes", vm.Cards[2].Item.Label);
        Assert.AreEqual(IconRef.Emoji("📝"), vm.Cards[2].Item.Icon);
        Assert.AreSame(vm.Cards[2], vm.Selected);
        Assert.AreEqual(0, vm.AvailablePages.Count);
        Assert.IsFalse(vm.AddPageCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("ribbon-editor-insertsep")]
    public void AddSeparator_WithNothingSelected_Appends()
    {
        var vm = Editor();
        vm.AddSeparatorCommand.Execute(null);
        Assert.IsTrue(vm.Cards[^1].IsSeparator);
        StringAssert.StartsWith(vm.Cards[^1].AutomationId, "RibbonEditor_Separator");
    }

    [TestMethod]
    [CoversNode("ribbon-editor-delete")]
    public void Delete_SelectsTheNeighbour()
    {
        var vm = Editor();
        vm.Selected = vm.Cards[1];
        vm.DeleteCommand.Execute(null);

        Assert.AreEqual(2, vm.Cards.Count);
        Assert.AreSame(vm.Cards[1], vm.Selected);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-dragreorder")]
    public void Reorder_CountsTheListBeforeTheMove()
    {
        var vm = Editor();
        var projects = Card(vm, "Projects");
        vm.ReorderCommand.Execute(new ReorderRequest(projects, 3));   // dropped after the last card

        Assert.AreSame(projects, vm.Cards[^1]);
        Assert.AreSame(projects, vm.Selected);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-reset")]
    public void Reset_RebuildsTheDraftOnly()
    {
        var vm = Editor();
        vm.ResetDefaultsCommand.Execute(null);

        Assert.AreEqual("Default", vm.Cards.Single().Item.Label);
        Assert.IsNull(vm.Selected);
        Assert.AreEqual(3, _live.Count);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-size")]
    public void ToggleSize_OnlyForButtons()
    {
        var vm = Editor();
        vm.Selected = vm.Cards[1];
        Assert.IsFalse(vm.ToggleSizeCommand.CanExecute(null));

        vm.Selected = Card(vm, "Chat");
        vm.ToggleSizeCommand.Execute(null);
        Assert.IsTrue(vm.SelectedItem!.IsHalf);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-colourpicker")]
    public void SelectedColour_FollowsTheSlot()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");

        vm.SelectedSlot = vm.Slots.Single(s => s.Value == RibbonColourSlot.Background);
        vm.SelectedColour = ColorSpec.FromSwatch("Blue");

        Assert.AreEqual(ColorSpec.FromSwatch("Blue"), vm.SelectedItem!.Background);
        Assert.IsTrue(vm.SelectedItem.Foreground.IsDefault);
        CollectionAssert.Contains(vm.UsedColours.ToList(), ColorSpec.FromSwatch("Blue"));
    }

    [TestMethod]
    [CoversNode("ribbon-editor-colourpicker")]
    public void BorderColour_BringsAnOutline()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.SelectedSlot = vm.Slots.Single(s => s.Value == RibbonColourSlot.Border);
        vm.SelectedColour = ColorSpec.Custom(Colors.Orange);

        Assert.AreEqual(RibbonBorderWeight.Thin, vm.SelectedItem!.BorderWeight);
        Assert.AreEqual(RibbonBorderWeight.Thin, vm.SelectedBorderWeight!.Value);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-shape")]
    public void Shape_GalleryShowsTheSelectedButton()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.SelectedShape = vm.ShapeOptions.Single(o => o.Shape == RibbonButtonShape.Hexagon);

        Assert.AreEqual(RibbonButtonShape.Hexagon, vm.SelectedItem!.Shape);
        Assert.IsTrue(vm.ShapeOptions.All(o => o.Preview.Label == "Chat" && o.Preview.Icon == IconRef.Emoji("💬")));
        Assert.IsTrue(vm.ShapeOptions.All(o => o.Preview.Shape == o.Shape));
    }

    [TestMethod]
    [CoversNode("ribbon-editor-contrast")]
    public void Contrast_WarnsWhenTextMatchesItsBackground()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        Assert.IsFalse(vm.IsLowContrast);   // white on black by the test's theme

        vm.SelectedSlot = vm.Slots.Single(s => s.Value == RibbonColourSlot.Background);
        vm.SelectedColour = ColorSpec.Custom(Color.FromRgb(0xF0, 0xF0, 0xF0));
        vm.SelectedSlot = vm.Slots.Single(s => s.Value == RibbonColourSlot.Foreground);
        vm.SelectedColour = ColorSpec.Custom(Colors.White);
        Assert.IsTrue(vm.IsLowContrast);
    }

    [TestMethod]
    [CoversNode("ribbon-editor-contrast")]
    public void ThemeText_OnAPaleBackground_TurnsDark()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        vm.SelectedSlot = vm.Slots.Single(s => s.Value == RibbonColourSlot.Background);
        vm.SelectedColour = ColorSpec.Custom(Color.FromRgb(0xF0, 0xF0, 0xF0));

        // The theme's white text would vanish; the button draws the theme's dark page colour instead.
        Assert.IsFalse(vm.IsLowContrast);
        Assert.AreEqual(Colors.Black, RibbonColourSlots.EffectiveForeground(vm.SelectedItem!, key => key is "BgBrush" ? Brushes.Black : Brushes.White));
    }

    [TestMethod]
    [CoversNode("ribbon-editor-resetlook")]
    public void ResetLook_KeepsNameIconAndSize()
    {
        var vm = Editor();
        vm.Selected = Card(vm, "Chat");
        var item = vm.SelectedItem!;
        item.IsHalf = true;
        item.Shape = RibbonButtonShape.Pill;
        item.Foreground = ColorSpec.FromSwatch("Red");
        item.BorderWeight = RibbonBorderWeight.Thick;

        vm.ResetLookCommand.Execute(null);

        Assert.AreEqual(RibbonButtonShape.Standard, item.Shape);
        Assert.IsTrue(item.Foreground.IsDefault);
        Assert.AreEqual(RibbonBorderWeight.None, item.BorderWeight);
        Assert.IsTrue(item.IsHalf);
        Assert.AreEqual("Chat", item.Label);
    }
}
