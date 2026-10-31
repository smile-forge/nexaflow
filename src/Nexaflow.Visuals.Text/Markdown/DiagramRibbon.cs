using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nexaflow.Icons;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Icons;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The mini ribbon a right-click puts over content: the controls the language owning what was right-clicked offers there
/// (<see cref="MiniRibbonControl"/>), drawn and handed back the value each is used with.
///
/// <para>
/// Its own control rather than entries in the document's own menu, because that menu's actions are a closed set about text —
/// cut, copy, a heading — and none of them mean anything to a node. The host shows whichever ribbon the block under the pointer
/// offers, so the two never have to know about each other.
/// </para>
/// <para>
/// <strong>It knows how to draw, and nothing about what anything means.</strong> A button is pressed, an option of a choice is
/// chosen, a value is typed into an entry or picked from its list — and whichever it was, the control's own callback is handed to
/// whatever made the ribbon (<c>use</c>), which runs it against the content as it stands and applies what it answers.
/// </para>
/// <para>
/// <strong>Two rows, pictures first.</strong> What sets something — choices and entries — stands on the first row and what is done
/// at once on the second, each button its picture where it has one and its name only where it has none. A few options stand side by
/// side; a choice of colours, or of more options than a row holds, sits behind one button showing the one the content has now that
/// drops them all down under it; an entry's usual values drop down from beside its box. So the ribbon is never more than two rows
/// deep, however much is offered. Every control still says in words what it does, to a reader hovering over it and to one hearing
/// the screen read.
/// </para>
/// <para>
/// Every control carries an automation id, <c>Diagram_Ribbon_</c> and its <see cref="MiniRibbonControl.Id"/> — an option, its
/// choice's id, a dot and its own; a value in an entry's list the same — because a control is the thing a journey uses and an id is
/// the only handle that survives a copy change.
/// </para>
/// </summary>
internal sealed class DiagramRibbon : UserControl
{
    /// <param name="controls">What is offered, in the order it is offered.</param>
    /// <param name="use">Runs a control's callback against the content as it stands, and applies the change it answers.</param>
    public DiagramRibbon(IReadOnlyList<MiniRibbonControl> controls, Action<Func<ContentEdit, ContentChange?>> use)
    {
        Controls = controls;

        var settings = new StackPanel { Orientation = Orientation.Horizontal };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };

        foreach (var control in controls)
        {
            switch (control)
            {
                case MiniRibbonChoice choice:
                    settings.Children.Add(Choosing(choice, use));
                    break;

                case MiniRibbonEntry entry:
                    settings.Children.Add(Entering(entry, use, Dismiss));
                    break;

                case MiniRibbonButton button:
                    actions.Children.Add(Pressing(button, use, Dismiss));
                    break;
            }
        }

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (settings.Children.Count > 0) rows.Children.Add(settings);
        if (settings.Children.Count > 0 && actions.Children.Count > 0) rows.Children.Add(Rule());
        if (actions.Children.Count > 0) rows.Children.Add(actions);

        Content = new Border
        {
            Child = rows,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Background = Brush("SurfaceBrush", Colors.White),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Color = Colors.Black, Opacity = 0.6 },
        };
    }

    /// <summary>What it is offering, in the order it offers them.</summary>
    public IReadOnlyList<MiniRibbonControl> Controls { get; }

    /// <summary>
    /// Takes the ribbon away from over the content — what a button pressed or a value entered is done with. A choice leaves it open, so
    /// the next option can be tried against the last.
    /// </summary>
    private void Dismiss()
    {
        for (DependencyObject? up = this; up is not null; up = LogicalTreeHelper.GetParent(up))
            if (up is Popup shown)
            {
                shown.IsOpen = false;
                return;
            }
    }

    // ── Buttons ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Something done at once: its picture where it has one, else its name — in the theme's colour for danger where it takes something
    /// away. Pressed, it is done, and so is the ribbon.
    /// </summary>
    private static FrameworkElement Pressing(MiniRibbonButton offer, Action<Func<ContentEdit, ContentChange?>> use, Action done)
    {
        var button = Flat(offer.Shape is null ? new Thickness(8, 5, 8, 5) : new Thickness(6, 4, 6, 4));
        button.Foreground = offer.Warns ? Brush("DangerBrush", Colors.Firebrick) : Brush("TextBrush", Colors.Black);
        button.Content = offer.Shape is { } shape ? Pictured(shape, button) : new TextBlock { Text = offer.Name, FontSize = 12 };
        Said(button, offer.Name, offer.Id);

        button.Click += (_, _) =>
        {
            use(offer.Pressed);
            done();
        };

        return button;
    }

    // ── Choices ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One choice, drawn however its options are read best: a few side by side, and a bank of colours, or more options than a row
    /// holds, behind the one button showing the option the content has now.
    /// </summary>
    private static FrameworkElement Choosing(MiniRibbonChoice choice, Action<Func<ContentEdit, ContentChange?>> use) =>
        choice.Options.Count > Abreast || choice.Options.All(option => option.Shade is not null)
            ? ChoosingFromAList(choice, use)
            : ChoosingInARow(choice, use);

    /// <summary>A choice's options side by side, each drawn as itself, the one the content says now picked out.</summary>
    private static FrameworkElement ChoosingInARow(MiniRibbonChoice choice, Action<Func<ContentEdit, ContentChange?>> use)
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = Options(choice, use, picked: null);

        foreach (var button in buttons) segments.Children.Add(button);

        return new Border
        {
            Child = segments,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// A choice behind the one button showing which option the content has now: pressed, it drops every option down under it — as a
    /// bank of swatches where they are colours — and closes again on the one chosen, which it then shows instead.
    /// </summary>
    private static FrameworkElement ChoosingFromAList(MiniRibbonChoice choice, Action<Func<ContentEdit, ContentChange?>> use)
    {
        var shown = Flat(new Thickness(6, 4, 6, 4));
        var face = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

        shown.Foreground = Brush("TextBrush", Colors.Black);
        shown.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { face, Chevron() } };
        Said(shown, choice.Name, choice.Id);

        var list = Dropped(shown);
        var bank = new WrapPanel { MaxWidth = Abreast * (Side + 10) };
        list.Child = Dropping(bank);

        foreach (var button in Options(choice, use, picked: option =>
                 {
                     Showing(option);
                     list.IsOpen = false;
                 }))
            bank.Children.Add(button);

        Showing(choice.Options.FirstOrDefault(option => option.Id == choice.Current));
        shown.Click += (_, _) => list.IsOpen = !list.IsOpen;

        return new Grid { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { shown, list } };

        // What the button shows: the option the content has now, or the choice's name alone where it has none.
        void Showing(MiniRibbonOption? current) =>
            face.Content = current is { } one ? Face(one, shown) : new TextBlock { Text = choice.Name, FontSize = 12 };
    }

    /// <summary>
    /// A button for each of a choice's options, the one the content says now picked out. Choosing one picks it out instead, hands
    /// the choice's callback to be run with it, and tells <paramref name="picked"/>; the ribbon stays open for the next.
    /// </summary>
    private static List<Button> Options(MiniRibbonChoice choice, Action<Func<ContentEdit, ContentChange?>> use, Action<MiniRibbonOption>? picked)
    {
        var buttons = new List<Button>();

        foreach (var option in choice.Options)
        {
            var button = Flat(new Thickness(5, 4, 5, 4));
            button.Content = Face(option, button);
            Pick(button, option.Id == choice.Current);
            Said(button, choice.Name + ": " + option.Name, choice.Id + "." + option.Id);

            button.Click += (_, _) =>
            {
                use(edit => choice.Chosen(edit, option));
                foreach (var other in buttons) Pick(other, ReferenceEquals(other, button));
                picked?.Invoke(option);
            };

            buttons.Add(button);
        }

        return buttons;
    }

    /// <summary>How many options of one choice stand in a row before they are better dropped down from a button.</summary>
    private const int Abreast = 8;

    /// <summary>An option drawn as picked out, or not.</summary>
    private static void Pick(Button button, bool picked)
    {
        button.Background = picked ? Brush("AccentSubtleBrush", Colors.LightBlue) : Brushes.Transparent;
        button.Foreground = picked ? Brush("AccentBrush", Colors.Blue) : Brush("TextBrush", Colors.Black);
    }

    // ── Entries ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A value typed into a box or picked from the usual ones dropped down beside it, as a font's size is: the box holds the value the
    /// content has now, and Enter on a value the language takes, or a value picked from the list, hands the entry's callback to be run
    /// with it and takes the ribbon away. A value it refuses goes nowhere. Its name stands before the box.
    /// </summary>
    private static FrameworkElement Entering(MiniRibbonEntry entry, Action<Func<ContentEdit, ContentChange?>> use, Action done)
    {
        var box = new TextBox
        {
            Text = entry.Current,
            MinWidth = 44,
            Padding = new Thickness(4, 2, 4, 2),
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(ForegroundProperty, "TextBrush");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextBrush");
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "AccentBrush");
        AutomationProperties.SetName(box, entry.Name);
        AutomationProperties.SetAutomationId(box, Named(entry.Id));
        box.ToolTip = new TextBlock { Text = entry.Name };

        var opener = Flat(new Thickness(4, 4, 4, 4));
        opener.Foreground = Brush("TextBrush", Colors.Black);
        opener.Content = Chevron();
        Said(opener, entry.Name, entry.Id + "_List");

        var list = Dropped(opener);
        var values = new StackPanel { Orientation = Orientation.Vertical, MinWidth = 64 };
        list.Child = Dropping(values);

        foreach (var value in entry.Listed)
        {
            var listed = Flat(new Thickness(8, 3, 8, 3));
            listed.Foreground = Brush("TextBrush", Colors.Black);
            listed.HorizontalContentAlignment = HorizontalAlignment.Left;
            listed.Content = new TextBlock { Text = value, FontSize = 12 };
            Pick(listed, value == entry.Current);
            Said(listed, entry.Name + ": " + value, entry.Id + "." + value);

            listed.Click += (_, _) =>
            {
                box.Text = value;
                list.IsOpen = false;
                Entered();
            };

            values.Children.Add(listed);
        }

        box.KeyDown += (_, key) =>
        {
            if (key.Key != Key.Enter) return;

            key.Handled = true;
            Entered();
        };
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        opener.Click += (_, _) => list.IsOpen = !list.IsOpen;

        // Its name before it, as a box of a font's size is read: a number alone says nothing of what it is the number of.
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = entry.Name,
                    FontSize = 12,
                    Margin = new Thickness(2, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("TextBrush", Colors.Black),
                },
                new Grid
                {
                    Children =
                    {
                        new Border
                        {
                            CornerRadius = new CornerRadius(4),
                            BorderThickness = new Thickness(1),
                            BorderBrush = Brush("BorderBrush", Colors.Gray),
                            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, opener } },
                        },
                        list,
                    },
                },
            },
        };

        // A value entered is done with, and so is the ribbon.
        void Entered()
        {
            var value = box.Text.Trim();
            if (entry.Accepts is { } accepts && !accepts(value)) return;

            use(edit => entry.Entered(edit, value));
            done();
        }
    }

    // ── What controls are drawn as ──────────────────────────────────────────

    /// <summary>What an option is drawn as on <paramref name="button"/>: its colour, its shape in the button's ink, its letters, or its name.</summary>
    private static UIElement Face(MiniRibbonOption option, Button button)
    {
        if (option.Shade is { } shade) return Swatched(shade);
        if (option.Shape is { } shape) return Pictured(shape, button);

        if (option.Letters is { Length: > 0 } letters)
            return new TextBlock { Text = letters, FontSize = 11, Width = Side, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        return new TextBlock { Text = option.Name, FontSize = 12 };
    }

    /// <summary>A shape in the box a button gives a picture, filled in the button's own ink.</summary>
    private static UIElement Pictured(Geometry shape, Button button)
    {
        var path = new System.Windows.Shapes.Path { Data = shape, Width = Side, Height = Side, Stretch = Stretch.None };
        path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = button });
        return path;
    }

    /// <summary>A colour drawn as itself — and a clear one, which is a colour too, as the square crossed through that says so.</summary>
    private static UIElement Swatched(Color shade)
    {
        var square = new Border
        {
            Width = Side,
            Height = Side,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(shade),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
        };

        if (shade.A > 0) return square;

        var crossed = new Grid { Width = Side, Height = Side };
        crossed.Children.Add(square);

        // In the ink words are written in, because the line round it is near the colour of whatever it is drawn on.
        crossed.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 2.5,
            Y1 = Side - 2.5,
            X2 = Side - 2.5,
            Y2 = 2.5,
            Stroke = Brush("TextBrush", Colors.Black),
            StrokeThickness = 1.25,
        });

        return crossed;
    }

    /// <summary>The mark that says a button drops something down.</summary>
    private static TextBlock Chevron() =>
        new() { Text = "", FontFamily = IconFont, FontSize = 10, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>What drops down under <paramref name="under"/>, closing itself when the reader presses anywhere else.</summary>
    private static Popup Dropped(UIElement under) => new()
    {
        StaysOpen = false,
        AllowsTransparency = true,
        Placement = PlacementMode.Bottom,
        PlacementTarget = under,
    };

    /// <summary>What is dropped down, on a surface of its own.</summary>
    private static Border Dropping(UIElement inside) => new()
    {
        Child = inside,
        Margin = new Thickness(0, 2, 0, 0),
        Padding = new Thickness(4),
        CornerRadius = new CornerRadius(6),
        Background = Brush("SurfaceBrush", Colors.White),
        BorderBrush = Brush("BorderBrush", Colors.Gray),
        BorderThickness = new Thickness(1),
    };

    /// <summary>How wide and high a picture on a button is.</summary>
    private const double Side = 16;

    /// <summary>What a reader hovering over a button, or hearing the screen read, is told it does — and the handle a journey uses it by.</summary>
    private static void Said(Button button, string name, string id)
    {
        button.ToolTip = new TextBlock { Text = name };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, Named(id));
    }

    /// <summary>The automation id of the control, option or listed value <paramref name="id"/> names.</summary>
    private static string Named(string id) => "Diagram_Ribbon_" + id;

    /// <summary>A button drawn flat: no chrome of its own, the surface under the pointer lit, pressed a shade deeper.</summary>
    private static Button Flat(Thickness padding) => new()
    {
        Padding = padding,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Cursor = Cursors.Hand,
        Template = FlatTemplate.Value,
    };

    // A template belongs to the thread that made it, and every window's thread shows ribbons of its own.
    private static readonly System.Threading.ThreadLocal<ControlTemplate> FlatTemplate = new(() =>
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        surface.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        surface.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty,
            new System.Windows.Data.Binding(nameof(HorizontalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };

        var over = new Trigger { Property = IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, Brush("Surface2Brush", Colors.LightGray), "Surface"));
        template.Triggers.Add(over);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Brush("BorderBrush", Colors.Gray), "Surface"));
        template.Triggers.Add(pressed);

        return template;
    });

    /// <summary>The line between what sets something and what is done at once.</summary>
    private static FrameworkElement Rule() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 6, 0, 6),
        Background = Brush("BorderBrush", Colors.Gray),
    };

    // ── What the renderer's own verbs are called and drawn as ───────────────

    /// <summary>
    /// What an intent is called where a reader has to read it. The verbs the renderer knows are named here so every
    /// diagram words them the same way; anything else says what it said for itself, and falls back to its verb.
    /// </summary>
    public static string Names(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Navigate => Str.Get("Text.Ribbon.OpenLink"),
        LayoutVerbs.Expand => Str.Get("Text.Ribbon.Expand"),
        LayoutVerbs.Collapse => Str.Get("Text.Ribbon.Collapse"),
        LayoutVerbs.Copy => Str.Get("Text.Ribbon.Copy"),
        LayoutVerbs.Cut => Str.Get("Text.Ribbon.Cut"),
        LayoutVerbs.Paste => Str.Get("Text.Ribbon.Paste"),
        LayoutVerbs.Save => Str.Get("Text.Ribbon.SavePicture"),
        _ => intent.Tip is { Length: > 0 } said ? said : intent.Verb,
    };

    /// <summary>
    /// The mark an intent is drawn as where there is room only for a mark — a block's corner, a ribbon's button — or null where it
    /// has none and is named instead. Segoe MDL2 Assets, which every Windows the app runs on carries.
    /// </summary>
    public static string? Icon(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Copy => "",
        LayoutVerbs.Save => "",
        LayoutVerbs.Navigate => "",
        LayoutVerbs.Paste => "",
        LayoutVerbs.Cut => "",
        _ => null,
    };

    /// <summary>The face the marks are drawn in.</summary>
    public static FontFamily IconFont { get; } = new("Segoe MDL2 Assets");

    /// <summary>
    /// <paramref name="intent"/>'s mark (<see cref="Icon"/>) as a shape in a button's box, or null where it has none — how a ribbon
    /// button for one of the renderer's own verbs is drawn. Each is made once.
    /// </summary>
    internal static Geometry? Marked(LayoutIntent intent) =>
        Icon(intent) is { } mark ? Marks.GetOrAdd(mark, static mark => Outlined(mark, IconFont)) : null;

    /// <summary>Every mark drawn so far.</summary>
    private static readonly ConcurrentDictionary<string, Geometry?> Marks = new();

    /// <summary>
    /// <paramref name="icon"/> as a shape in the ribbon's box — its glyph's outline, centred — or null where the font has no such icon and
    /// the control is named instead: how a language draws a control of its own as an icon. Each is made once.
    /// </summary>
    internal static Geometry? Drawn(IconRef icon) => Shapes.GetOrAdd(icon, static icon =>
        icon.IsEmpty || !IconCatalog.Contains(icon) || IconCatalog.FontFor(icon) is not { } font ? null : Outlined(IconCatalog.GlyphFor(icon), font));

    /// <summary>Every icon drawn so far.</summary>
    private static readonly ConcurrentDictionary<IconRef, Geometry?> Shapes = new();

    /// <summary>A glyph's outline, centred in a button's box — or null where it draws nothing.</summary>
    private static Geometry? Outlined(string glyph, FontFamily font)
    {
        var face = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var outline = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Side, Brushes.Black, 1.0)
            .BuildGeometry(new Point(0, 0));

        var bounds = outline.Bounds;
        if (bounds.IsEmpty) return null;

        outline.Transform = new TranslateTransform((Side - bounds.Width) / 2 - bounds.X, (Side - bounds.Height) / 2 - bounds.Y);
        outline.Freeze();
        return outline;
    }

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
