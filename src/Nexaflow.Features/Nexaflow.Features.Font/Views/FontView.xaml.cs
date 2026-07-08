using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ViewModels;

namespace Nexaflow.Features.Font.Views;

public partial class FontView : UserControl, IPageView
{
    private readonly FontViewModel _vm;

    public FontView(FontViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => Focus();
    }

    IPageViewModel? IPageView.ViewModel => _vm;

    // The shell routes a param set here on activation — including when an "As Font" open lands on an
    // existing Font tab. Load the routed fonts so they aren't silently dropped.
    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("paths", out var paths) && !string.IsNullOrWhiteSpace(paths))
            _vm.OpenFiles(paths.Split('|', StringSplitOptions.RemoveEmptyEntries));
    }
}
