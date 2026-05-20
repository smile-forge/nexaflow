namespace Nexaflow.Features.Common.Viewlets;

public interface IViewletController
{
    ViewletDisplayMode CurrentMode { get; }
    event Action<ViewletDisplayMode>? ModeChanged;
    void SetDisplayMode(ViewletDisplayMode mode);
}
