namespace Nexaflow.IO.Usb.Tools;

/// <summary>
/// A live tool that shows folders on this PC whole — a USB disk's mounted volumes — so the page it is on can put them in
/// front of the assistant, and its tools reach paths inside them and nowhere else. A tool whose files are not on this
/// PC's file system (a phone's, over MTP) has none to give.
/// </summary>
public interface IUsbToolShownFolders
{
    /// <summary>The folders shown, as paths on this PC; empty until the tool has read them.</summary>
    IReadOnlyList<string> ShownFolders { get; }
}
