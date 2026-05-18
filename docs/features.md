# Building a Feature

A feature is a class library (`Nexaflow.Features.MyFeature`) that references only `Nexaflow.Features.Common` and WPF. Core never imports your types directly — everything goes through the contracts in `Features.Common`.

---

## Checklist

| Step | Required | What |
|------|----------|------|
| Create project | yes | Class library, reference `Features.Common` |
| `ITabRegistration` | yes | Factory that produces a `TabEntry` |
| `IPageView` on your `UserControl` | recommended | Shell lifecycle handle: `ViewModel` property + `Reinitialize` |
| `IPageViewModel` on your ViewModel | recommended | AI pipeline contract: `GetContext`, `GetAvailableActions`, `Execute` |
| Register in `App.xaml.cs` | yes | `fm.Register(typeof(MyTabRegistration))` |
| Add project reference in `Nexaflow.Core.csproj` | yes | So `App.xaml.cs` can see your type |
| Add ribbon entry in `ShellViewModel.BuildDefaultItems()` | optional | Puts a button on the default toolbar |
| `IFeatureConfig` | optional | Persisted settings, free Options panel UI |
| `IQueryHandler` | optional | Handle AI input bar text |
| `IFileAction` / `IFolderAction` | optional | File browser context actions |
| `IKeyboardHandler` | optional | Global keyboard shortcuts |
| `IDropTarget` | optional | File drag-drop acceptance |

---

## 1. Project Setup

```
src/Nexaflow.Features/Nexaflow.Features.MyFeature/
  MyTabRegistration.cs
  MyConfig.cs             ← optional
  ViewModels/
    MyViewModel.cs
  Views/
    MyView.xaml
    MyView.xaml.cs
```

`Nexaflow.Features.MyFeature.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Nexaflow.Features.Common\Nexaflow.Features.Common.csproj" />
  </ItemGroup>
</Project>
```

Add a `<ProjectReference>` to `Nexaflow.Core.csproj` and one `fm.Register` call in `App.xaml.cs`.

---

## 2. `ITabRegistration` — Entry Point

`FeatureManager.Register()` discovers this via reflection, resolves constructor dependencies, and instantiates it. Available for injection:

- Any `IFeatureConfig` declared in **the same assembly**
- `IShellServices` (always available after `App.OnStartup` wires it)
- Any service registered via `FeatureManager.Instance.RegisterSingletonService()` (currently `IAIService`)

```csharp
public sealed class MyTabRegistration(MyConfig config, IShellServices shellServices) : ITabRegistration
{
    public string PageKind => "MyFeature";   // stable string key; persisted in ribbon.json

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var vm = new MyViewModel(config, shellServices);

        var tab = new TabEntry
        {
            Title       = "My Feature",
            Icon        = "🔧",
            PageParams  = pageParams,
            Breadcrumbs = [new BreadcrumbSegment { Label = "My Feature" }]
        };
        tab.PageFactory = () => new MyView(vm);
        return tab;
    }
}
```

`PageFactory` is invoked lazily — `MyView` is not constructed until the tab is first activated.

---

## 3. `IPageView` / `IPageViewModel` — AI Pipeline Integration

`IPageView` is the shell's typed handle to a tab `UserControl`. Implement it on your `UserControl` with:
- `IPageViewModel? ViewModel` — exposes the ViewModel to the shell
- `Reinitialize(pageParams)` — called on first activation and whenever the shell routes the tab with a new param set (including re-clicking the already-active tab)

`IPageViewModel` is the AI pipeline contract. Implement it on your ViewModel with:
- `GetContext()` — short description sent as system context to the LLM
- `GetAvailableActions()` — `ActionDescriptor` list the AI can select when no handler matched
- `Execute(action)` — runs the AI-selected action
- `GetContextObject()` — optional strongly-typed `IContext` for query handlers to consume

```csharp
// The View — thin shell-lifecycle wrapper only
public partial class MyView : UserControl, IPageView
{
    private readonly MyViewModel _vm;

    public MyView(MyViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        var id = pageParams.GetValueOrDefault("id");
        if (id != null && id != _vm.CurrentId)
            _vm.LoadAsync(id);
    }
}

// The ViewModel — owns all AI pipeline logic
public partial class MyViewModel : ObservableObject, IPageViewModel
{
    public string GetContext() => $"User is viewing My Feature. Current item: {SelectedItem?.Name ?? "none"}";

    public IReadOnlyList<ActionDescriptor> GetAvailableActions() =>
    [
        new ActionDescriptor("Refresh", "Reload the current view"),
        new ActionDescriptor("Open",    "Open the selected item", new Dictionary<string, string> { ["item"] = SelectedItem?.Id ?? "" })
    ];

    public void Execute(ActionDescriptor action)
    {
        if (action.Name == "Refresh") RefreshCommand.Execute(null);
        if (action.Name == "Open"   ) OpenCommand.Execute(action.Parameters?["item"]);
    }
}
```

---

## 4. `IQueryHandler` — AI Input Bar

Intercepts the shell's AI input bar before text reaches the LLM. Two registration modes:

**Global** — always active regardless of which tab is open:

```csharp
// in App.xaml.cs after RegisterFeatures():
FeatureManager.Instance.RegisterQueryHandler(new MyGlobalHandler());
```

**Page-scoped** — active only when your tab is open. Implement `IQueryHandler` directly on your ViewModel; the shell checks `page?.ViewModel as IQueryHandler` automatically.

```csharp
public sealed class MyQueryHandler : IQueryHandler
{
    public string Description => "Does something useful when my tab is active";
    public string? Symbol => null;      // set to e.g. "?" to claim prefix routing

    public float CanProcess(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel vm) return 0f;
        return LooksLikeMyInput(input) ? 0.85f : 0f;
    }

    public async Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not MyViewModel vm) return "No active tab.";
        await vm.HandleInputAsync(input);
        return null;    // null = handled silently; non-null string = shown in AI Chat
    }
}
```

The shell uses `Symbol` for exact-prefix routing (e.g. `>` routes to the console). When multiple handlers score > 0, `IAIService.DisambiguateToolSelection()` asks the LLM to pick one.

---

## 5. `IShellServices` — Shell Capabilities

Injected into your `ITabRegistration` (and optionally forwarded to your ViewModel). All tab management flows through here.

```csharp
// Open or focus a tab (moves it to the caller's window if it exists elsewhere)
shellServices.OpenTab("ProjectDetail", new() { ["folder"] = folder }, callerPageView);

// Close a tab
shellServices.CloseTab(tab);

// Keep TabEntry.PageParams and breadcrumbs in sync after in-tab navigation
shellServices.UpdateTabMeta(tab, title: "New Title",
    breadcrumbs: [new BreadcrumbSegment { Label = "New Title" }],
    pageParams:  new() { ["id"] = newId });

// Check if a tab is already open before opening a duplicate
var existing = shellServices.FindTab("Search", new() { ["root"] = root });

// User-visible feedback
shellServices.ShowError("Could not load file.");
shellServices.ShowNotification("Export complete.");
```

**`OpenTab` param-matching:** the shell searches globally for an existing tab whose `PageKind` matches and whose `PageParams` are compatible (all requested keys match). If found, `Reinitialize(pageParams)` is called on it; if not, a new tab is created.

---

## 6. `IFeatureConfig` — Persisted Settings

Declare a plain POCO in your assembly. `FeatureManager.Register()` discovers it, instantiates it (loading from `%AppData%\Smile\Nexaflow\{ConfigName}\`), and injects it into your `ITabRegistration` constructor. The Options panel generates a property-grid editor for free.

```csharp
public sealed class MyConfig : IFeatureConfig
{
    public string ConfigName   => "myfeature";
    public string FriendlyName => "My Feature";

    [ConfigDisplayName("Root Folder")]
    [FolderPath]
    public string RootFolder { get; set; } = string.Empty;

    [ConfigDisplayName("AI Provider")]
    [ListSource(typeof(LlmProviderRegistry), nameof(LlmProviderRegistry.GetProviderNames))]
    public string Provider { get; set; } = string.Empty;
}
```

For a fully custom Options UI, annotate the class with `[CustomControl(typeof(MyOptionsControl))]` and implement `ICustomConfigApply.Apply()` on the control to handle saves.

Config attributes:

| Attribute | Effect |
|-----------|--------|
| `[ConfigDisplayName("Label")]` | Row label in the Options grid |
| `[FolderPath]` | TextBox + browse button + existence validation |
| `[ListSource(type, method)]` | ComboBox populated by a static `IEnumerable<string>` method |
| `[CustomControl(type)]` | Replaces the whole section with a custom `UserControl` |

---

## 7. `IFileAction` — File Browser Context Actions

Implement in your feature assembly (viewer-opener actions) or in `Nexaflow.Core.FileActions` (system-level). `FileActionManager.Discover()` finds all implementations automatically.

Constructor receives `IShellServices` and `IInputPromptService` via injection.

```csharp
public sealed class OpenInMyViewerAction(IShellServices shellServices) : IFileAction
{
    public string ExperienceId          => "/myformat";
    public string ExperienceDescription => "My Format files";
    public string DisplayName           => "Open in My Viewer";
    public string Icon                  => "👁";
    public bool   IsDestructive         => false;
    public bool   RequiresRefresh       => false;
    public bool   SupportsMultipleFiles => false;
    public bool   CanPerformAction      => true;

    public bool PerformAction(string filePath)
    {
        shellServices.OpenTab("MyFeature", new() { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
        => PerformAction(filePaths.First());
}
```

`ExperienceId` is a hierarchical path matched by `FileMapManager`. Users configure which experience applies to which file type in the Options panel. Parent IDs automatically satisfy child experiences.

---

## 8. `IContext` — Typed Context for Query Handlers

If your tab provides structured data that other query handlers need (beyond a string description), implement `IContext` and return an instance from `IPageViewModel.GetContextObject()`.

```csharp
public sealed class MyContext : IContext
{
    public required string CurrentMode { get; init; }
    public IReadOnlyList<string> SelectedItems { get; init; } = [];
}

// In MyViewModel:
public IContext? GetContextObject() => new MyContext
{
    CurrentMode   = Mode,
    SelectedItems = Selection.ToList()
};
```

Query handlers then gate on and extract it:

```csharp
public float CanProcess(string input, IPageViewModel? pageVm = null)
{
    if (pageVm?.GetContextObject() is not MyContext ctx) return 0f;
    return ctx.CurrentMode == "edit" ? 0.9f : 0f;
}
```

---

## 9. `IKeyboardHandler` — Global Keyboard Shortcuts

Implement on your `UserControl` or ViewModel. The shell calls `CanProcessKey` before consuming the event, so you can safely check state without side effects.

```csharp
public bool CanProcessKey(Key key, ModifierKeys modifiers)
    => modifiers == ModifierKeys.Control && key is Key.OemPlus or Key.OemMinus or Key.D0;

public bool ProcessKey(Key key, ModifierKeys modifiers)
{
    if (key == Key.OemPlus)  { _vm.ZoomIn();    return true; }
    if (key == Key.OemMinus) { _vm.ZoomOut();   return true; }
    if (key == Key.D0)       { _vm.ResetZoom(); return true; }
    return false;
}
```

---

## 10. `IActionExecutor` — AI JSON Action Payloads

Implement on your ViewModel when the `ActionDescriptor` list isn't expressive enough and you want the AI to drive more complex operations. Called by the shell after `IAIService.ContextChat` returns a JSON action and no `IQueryHandler` claimed the input.

```csharp
public async Task<bool> TryExecuteActionAsync(string actionJson)
{
    var action = JsonSerializer.Deserialize<MyAction>(actionJson);
    if (action is null) return false;
    await ExecuteAsync(action);
    return true;
}
```

---

## Tab Parameters Convention

Parameters are `Dictionary<string, string>`. Keys are lowercase, values are strings. Examples from existing features:

| Feature | Params |
|---------|--------|
| Search | `query`, `root` |
| Markdown | `path` |
| Images | `paths` (pipe-separated) |
| Projects | *(none)* |
| ProjectDetail | `folder` |

`TabEntry.PageParams` should always reflect the tab's current state — call `shellServices.UpdateTabMeta(tab, pageParams: ...)` after in-tab navigation so that `FindTab` and breadcrumb restores work correctly.

---

## `IAIService` — AI Capabilities

Injected via `FeatureManager.Instance.RegisterSingletonService`. Use it in ViewModels that implement `IQueryHandler` or `IActionExecutor`.

```csharp
// Let the LLM pick the best handler from a candidate list
IQueryHandler? chosen = await aiService.DisambiguateToolSelection(pageVm, input, candidates);

// One-shot contextual call: LLM returns Action, Prefill, or Message
AiResponse? response = await aiService.ContextChat(pageVm, input);
if (response?.Kind == AiResponseKind.Action)
    pageVm?.Execute(response.Action!);
```
