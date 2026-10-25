using System.ComponentModel;
using System.Windows;

namespace Nexaflow.Features.Common;

/// <summary>
/// A kind of new item that asks for more than a name before it is made — what to draw, how long a clip runs. While it is
/// chosen in the new-item picker, the picker shows the options it opens beneath the kinds, and creates the item through
/// them.
/// </summary>
public interface IFileCreateWithOptions : IFileCreateEntry
{
    /// <summary>Whether the picker offers it now: false while nothing here can make it.</summary>
    bool IsOffered { get; }

    /// <summary>The options for one new item in <paramref name="folderPath"/>, opened each time the kind is chosen in the
    /// picker and disposed when another is chosen or the picker closes.</summary>
    IFileCreateOptions OpenOptions(string folderPath);
}

/// <summary>The options one new item is being set up with, as <see cref="IFileCreateWithOptions.OpenOptions"/> opens them.</summary>
public interface IFileCreateOptions : INotifyPropertyChanged, IDisposable
{
    /// <summary>The options' view, shown in the picker beneath the kinds.</summary>
    FrameworkElement View { get; }

    /// <summary>Whether they say enough to make the item; the picker's Create waits on it.</summary>
    bool CanCreate { get; }

    /// <summary>
    /// Makes the item named <paramref name="fileName"/> in the folder the options were opened on, the name resolved as for
    /// <see cref="IFileCreateAction.CreateItemAsync"/>. The making may go on in the background, the item appearing once it is made.
    /// Returns the full path it is made at, or null where it was not begun.
    /// </summary>
    string? Create(string fileName);
}
