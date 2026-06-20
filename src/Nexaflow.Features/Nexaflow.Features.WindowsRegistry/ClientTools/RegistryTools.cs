using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Features.WindowsRegistry.ViewModels;

namespace Nexaflow.Features.WindowsRegistry.ClientTools;

/// <summary>Read-only: lists the subkeys of the current key, or of a downward relative path.</summary>
internal sealed class RegistryListSubkeysTool(RegistryViewModel vm) : IClientTool
{
    public string Name => "registry_list_subkeys";
    public string Description =>
        "List the child key names under the current registry key, or under a relative path below it. " +
        "Navigation is downward only — relative paths cannot move up to a parent key.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path", "Optional relative key path below the current key (e.g. \"Software\\Microsoft\"). Omit for the current key.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!vm.TryResolveDown(ToolArgs.Str(arguments, "path"), out var subPath, out var error))
            return Task.FromResult(ToolResult.Error(error));

        var names = vm.ReadSubKeys(subPath);
        var label = subPath.Length == 0 ? vm.CurrentRoot.Token : $"{vm.CurrentRoot.Token}\\{subPath}";
        if (names.Count == 0)
            return Task.FromResult(ToolResult.Ok($"{label}: no subkeys", $"'{label}' has no subkeys."));

        return Task.FromResult(ToolResult.Ok($"{names.Count} subkey(s) under {label}",
            $"Subkeys of '{label}':\n" + string.Join('\n', names)));
    }
}

/// <summary>Read-only: lists the values of the current key (or a downward path) and navigates there.</summary>
internal sealed class RegistryGetValuesTool(RegistryViewModel vm) : IClientTool
{
    public string Name => "registry_get_values";
    public string Description =>
        "List the values (name, type, data) of the current registry key, or of a relative path below it, " +
        "and move the view to that key. Navigation is downward only.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("path", "Optional relative key path below the current key. Omit for the current key.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!vm.TryResolveDown(ToolArgs.Str(arguments, "path"), out var subPath, out var error))
            return ToolResult.Error(error);

        var label = subPath.Length == 0 ? vm.CurrentRoot.Token : $"{vm.CurrentRoot.Token}\\{subPath}";
        var values = vm.ReadValues(subPath);

        // Move the view to the inspected key so the user sees what the model is reading.
        await vm.Shell.RunOnUiAsync(() => { vm.NavigateTo(label); return Task.FromResult(true); });

        var sb = new StringBuilder($"Values of '{label}':\n");
        foreach (var v in values)
            sb.Append(v.Name).Append("  ").Append(v.TypeLabel).Append("  ").Append(v.Data).Append('\n');
        return ToolResult.Ok($"{values.Count} value(s) at {label}", sb.ToString().TrimEnd());
    }
}

/// <summary>Mutating: changes an existing value under the current key (requires approval).</summary>
internal sealed class RegistrySetValueTool(RegistryViewModel vm) : IClientTool
{
    public string Name => "registry_set_value";
    public string Description =>
        "Change an existing value under the current registry key. Keeps the value's existing type unless " +
        "'type' is given.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "Value name to change. Use an empty string for the key's default value."),
        new("value", "New data. For DWord/QWord: decimal or 0x-hex; Binary: hex bytes; MultiString: newline-separated."),
        new("type", "Optional value type: String, ExpandString, DWord, QWord, Binary, MultiString.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => false;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        => RegistryWriteHelper.WriteAsync(vm, arguments, mustExist: true);
}

/// <summary>Mutating: creates a new value under the current key (requires approval).</summary>
internal sealed class RegistryInsertValueTool(RegistryViewModel vm) : IClientTool
{
    public string Name => "registry_insert_value";
    public string Description => "Create a new value under the current registry key.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("name", "Name for the new value."),
        new("type", "Value type: String, ExpandString, DWord, QWord, Binary, MultiString."),
        new("value", "Data. For DWord/QWord: decimal or 0x-hex; Binary: hex bytes; MultiString: newline-separated."),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => false;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        => RegistryWriteHelper.WriteAsync(vm, arguments, mustExist: false);
}

internal static class RegistryWriteHelper
{
    public static async Task<ToolResult> WriteAsync(RegistryViewModel vm, JsonObject arguments, bool mustExist)
    {
        var name = ToolArgs.Str(arguments, "name") ?? "";
        var typeToken = ToolArgs.Str(arguments, "type");
        var rawValue = ToolArgs.Str(arguments, "value") ?? "";

        RegistryValueKind kind;
        if (!string.IsNullOrWhiteSpace(typeToken))
        {
            if (!Enum.TryParse(typeToken, ignoreCase: true, out kind))
                return ToolResult.Error($"Unknown value type '{typeToken}'.");
        }
        else
        {
            var existing = vm.GetValueKind(name);
            if (mustExist && existing is null)
                return ToolResult.Error($"Value '{name}' does not exist; use registry_insert_value with a type.");
            kind = existing ?? RegistryValueKind.String;
        }

        string wire;
        try { wire = RegistryValueCodec.ParseUserInput(rawValue, kind); }
        catch (Exception ex) { return ToolResult.Error($"Invalid {RegistryValueCodec.TypeLabel(kind)} data: {ex.Message}"); }

        var ok = await vm.Shell.RunOnUiAsync(async () =>
        {
            var written = await vm.WriteValueAsync(name, kind, wire);
            if (written) vm.Refresh();
            return written;
        });

        var label = name.Length == 0 ? "(Default)" : name;
        return ok
            ? ToolResult.Ok($"Set {label} = {rawValue}", $"Set '{label}' ({RegistryValueCodec.TypeLabel(kind)}) at {vm.CurrentKeyPath}.")
            : ToolResult.Error($"Could not set '{label}' (declined or access denied).");
    }
}
