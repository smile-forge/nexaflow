namespace Nexaflow.IO.Usb.Tools;

/// <summary>
/// A live tool that works on a function's driver rather than with the function — taking it over, giving it its driver
/// back. It does not count as covering the function: the page still says what the function is, and what a tool for it
/// would use, beside it.
/// </summary>
public interface IUsbDriverTool : IUsbTool;
