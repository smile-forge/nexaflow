using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// <c>&gt;</c> on the USB page goes somewhere: a location path (<c>&gt; 3-10.2</c>), or the first device a name,
/// VID:PID or COM port finds (<c>&gt; webcam</c>, <c>&gt; 046d:0843</c>) — what <c>&gt;</c> is in the file browser.
/// </summary>
public sealed class UsbProbeQueryHandler : IQueryHandler
{
    public string Description => "Go to a USB location or device on the USB page";
    public string? Symbol => ">";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not UsbProbeViewModel page) return 0;
        if (prefixed) return 1.0f;
        // Bare text only when it is a path that resolves — anything else is a question for the agent.
        return input.Length > 0 && page.Browser.Resolve(input) is not null ? 0.6f : 0;
    }

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not UsbProbeViewModel page) return Task.FromResult<string?>(null);
        return Task.FromResult(page.Navigate(input.Trim()) ? null : Str.Format("UsbProbe.Query.NotFound", input.Trim()));
    }

    public Task<string?> CompleteAsync(string input, bool prefixed, IPageViewModel? pageVm = null) => Task.FromResult<string?>(null);
}
