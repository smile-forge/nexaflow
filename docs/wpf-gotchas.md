# WPF gotchas

Traps this codebase has already fallen into. Each cost real time to diagnose, usually because the symptom points
somewhere else.

## Templates and collections

**The global `MenuItem` style overrides the default template.** The one in `src/Nexaflow.Core/Themes/Styles.xaml`
replaces WPF's. If you need submenus, header arrows, or `Role`-dependent behaviour, extend that template — adding
child `MenuItem`s in code isn't enough.

**`ItemsControl.ItemsSource` binding + `Items.Add` is illegal** — pick one.

**`ObservableCollection.Clear()` + N × `Add()` fires N+1 `CollectionChanged` events.** The intermediate "empty"
state can render as a blank frame if anything in the view rebuilds on each event. Batch updates via
`Dispatcher.BeginInvoke` (in a feature: `IShellServices.RunOnUiAsync`).

**A bare string assigned to `ToolTip` inherits the parent's `TextAlignment`** when WPF wraps it in the default popup
`TextBlock`. Assign an explicit `TextBlock` if you care about alignment.

## UI Automation

**`AutomationProperties.AutomationId` is only reliable on elements that create an `AutomationPeer`** — `Control`
subclasses (`TextBox`, `TabItem`, `Button`, `ContentControl`, `TabControl`…). Decorators (`Border`) and panels
(`Grid`, `StackPanel`) create none by default, so an id set on one may never resolve:
`FindFirstDescendant(ByAutomationId(…))` returns null forever. It is *unpredictable* rather than always-absent —
`Pdf_Panel` on a `Border` never appears, while `TabItem_{PageKind}` (also a `Border`, set in `TabStrip.xaml.cs`)
does — so treat an id on a non-control as unusable regardless of whether it happens to work today. The failure is
nastier than a red test: the *inverse* assertion ("hidden → the id is null") passes for the wrong reason, so a toggle
test reads green while testing nothing. To assert a container is shown/hidden, assert on a real control inside it —
collapsing the container removes its children from the tree too. Don't reshape the visual tree just to host an id.
(The rule that every button carries an id, and every id is named by a journey, is in
[testing.md → Automation ids](testing.md#automation-ids-nxui001--automationidjourneycoveragetests).)

**A WPF `TextBox` publishes its text through the UIA `Value` pattern, not its `Name`.** Anything rendered as a
selectable/copyable `TextBox` (the PDF panel's property rows, for instance) is invisible to `ByName`/`element.Name` —
read `element.Patterns.Value` (or `AsTextBox().Text`) instead. A test searching by name for a value it can see on
screen is the usual symptom.

## RichTextBox

**A `UIElement` embedded in a `RichTextBox` (`BlockUIContainer`) does not reliably receive mouse events**, and the
routed event's `OriginalSource` over it is unreliable too — the text container attributes clicks to the container,
the `FlowDocument`, or even a *neighbouring* `Paragraph`/`Run` depending on the region. To give an embedded element
its own mouse interaction, hook the `RichTextBox`'s Preview event, find the element with a geometric
`VisualTreeHelper.HitTest`, and drive it directly (see `IInteractiveBlock`). A markdown document needs none of this:
`MarkdownSurface` draws it on one element with no text box underneath.
