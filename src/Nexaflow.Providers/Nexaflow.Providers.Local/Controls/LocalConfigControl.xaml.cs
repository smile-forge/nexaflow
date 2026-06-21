using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Catalog;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local.Controls;

/// <summary>
/// Custom Options editor for <see cref="LocalConfig"/>. Scalar fields bind two-way to the config; the
/// server-tool checkboxes and the MCP-server grid are edited via observable rows and written back in
/// <see cref="Apply"/>. Implements only <see cref="ICustomConfigApply"/> (the Features-side change
/// tracker isn't reachable from a provider).
/// </summary>
public partial class LocalConfigControl : UserControl, ICustomConfigApply
{
    private LocalConfig? _config;
    private bool _loaded;
    private readonly ObservableCollection<ToolRow> _toolRows = [];
    private readonly ObservableCollection<McpRow>  _mcpRows  = [];

    public LocalConfigControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;                             // Loaded can re-fire — build once
        _config = DataContext as LocalConfig;
        if (_config is null) return;
        _loaded = true;

        var enabled = new HashSet<string>(_config.EnabledServerTools, StringComparer.OrdinalIgnoreCase);
        foreach (var name in ServerToolRegistry.BuiltInNames)
            _toolRows.Add(new ToolRow { Name = name, Enabled = enabled.Contains(name) });
        ToolsList.ItemsSource = _toolRows;

        foreach (var m in _config.McpServers)
            _mcpRows.Add(new McpRow { Name = m.Name, Command = m.Command, Arguments = m.Arguments, Enabled = m.Enabled });
        McpGrid.ItemsSource = _mcpRows;

        CatalogPathText.Text = "Catalog: " + LocalModelCatalog.UserCatalogPath(_config.ResolvedModelsDir);

        ApplyAccelStatus();
    }

    /// <summary>Shows whether local inference will use the GPU, with a CUDA download link when it won't.</summary>
    private void ApplyAccelStatus()
    {
        switch (LocalNativeRuntime.DetectAcceleration())
        {
            case AccelStatus.Gpu:
                AccelStatusText.Text = "Acceleration: GPU — CUDA detected.";
                AccelStatusText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                CudaLinkText.Visibility = Visibility.Collapsed;
                break;
            case AccelStatus.NoCudaRuntime:
                AccelStatusText.Text = "Acceleration: CPU — an NVIDIA GPU was found but the CUDA 12 runtime is missing.";
                AccelStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                CudaLinkText.Visibility = Visibility.Visible;
                break;
            default: // NoGpu
                AccelStatusText.Text = "Acceleration: CPU — no CUDA GPU detected.";
                AccelStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                CudaLinkText.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void CudaLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* best effort */ }
        e.Handled = true;
    }

    private void ResetCatalog_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _config ?? DataContext as LocalConfig;
        if (cfg is null) return;
        var dir = string.IsNullOrWhiteSpace(ModelsDirBox.Text) ? cfg.ResolvedModelsDir : ModelsDirBox.Text.Trim();
        try
        {
            LocalModelCatalog.ResetToBundled(dir);
            CatalogPathText.Text = "Catalog reset → " + LocalModelCatalog.UserCatalogPath(dir);
        }
        catch (System.Exception ex)
        {
            CatalogPathText.Text = "Reset failed: " + ex.Message;
        }
    }

    /// <summary>Authoritatively writes every field from the controls onto the config (the DataContext
    /// is the live RealConfig the host then saves). Doesn't rely on binding having flushed.</summary>
    public void Apply()
    {
        var cfg = _config ?? DataContext as LocalConfig;
        if (cfg is null) return;

        cfg.ModelsDir     = ModelsDirBox.Text?.Trim() ?? "";
        cfg.ContextSize   = int.TryParse(ContextSizeBox.Text, out var cs) ? cs : 0;
        cfg.GpuLayerCount = int.TryParse(GpuLayersBox.Text,  out var gl) ? gl : -1;
        cfg.ThinkingMode  = ThinkingBox.IsChecked == true;

        // Only rewrite the list-backed fields once the rows have been built, so an Apply before the
        // control was ever shown can't wipe them.
        if (!_loaded) return;

        cfg.EnabledServerTools = _toolRows.Where(t => t.Enabled).Select(t => t.Name).ToList();
        cfg.McpServers = _mcpRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new McpServerEntry
            {
                Name      = r.Name.Trim(),
                Command   = r.Command?.Trim() ?? "",
                Arguments = r.Arguments?.Trim() ?? "",
                Enabled   = r.Enabled,
            })
            .ToList();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        var dlg = new OpenFolderDialog { Title = "Select models folder" };
        if (!string.IsNullOrWhiteSpace(_config.ModelsDir)) dlg.InitialDirectory = _config.ModelsDir;
        if (dlg.ShowDialog() == true)
            ModelsDirBox.Text = dlg.FolderName;          // two-way binding writes it back to the config
    }

    private void AddMcp_Click(object sender, RoutedEventArgs e)
    {
        var row = new McpRow { Name = "New MCP server", Enabled = true };
        _mcpRows.Add(row);
        McpGrid.SelectedItem = row;
        McpGrid.ScrollIntoView(row);
    }

    private void RemoveMcp_Click(object sender, RoutedEventArgs e)
    {
        if (McpGrid.SelectedItem is McpRow row) _mcpRows.Remove(row);
    }

    private sealed class ToolRow
    {
        public string Name    { get; set; } = string.Empty;
        public bool   Enabled { get; set; }
    }

    private sealed class McpRow
    {
        public string  Name      { get; set; } = string.Empty;
        public string? Command   { get; set; }
        public string? Arguments { get; set; }
        public bool    Enabled   { get; set; }
    }
}
