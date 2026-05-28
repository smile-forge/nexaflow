using Nexaflow.Core.AI;
using Nexaflow.Features.Common;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

public partial class AiPersonaControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private AiPersonaConfig? _config;
    private string _originalName = string.Empty;
    private string _originalPrompt = string.Empty;
    private bool _loading;

    public AiPersonaControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiPersonaConfig cfg) return;
        _config = cfg;
        _loading = true;
        NameBox.Text   = cfg.Name;
        PromptBox.Text = cfg.SystemPrompt;
        _originalName  = cfg.Name;
        _originalPrompt = cfg.SystemPrompt;
        _loading = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasChanges =>
        _config is not null &&
        (NameBox.Text != _originalName || PromptBox.Text != _originalPrompt);

    public event EventHandler? HasChangesChanged;

    public void Apply()
    {
        if (_config is null) return;
        _config.Name         = NameBox.Text;
        _config.SystemPrompt = PromptBox.Text;
        _originalName        = _config.Name;
        _originalPrompt      = _config.SystemPrompt;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }
}
