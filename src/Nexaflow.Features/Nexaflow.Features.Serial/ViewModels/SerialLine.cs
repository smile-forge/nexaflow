using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.Serial.ViewModels;

public enum SerialDirection { Received, Sent, Note }

/// <summary>
/// One line of the transcript: what arrived (or was sent) together, when, and how it reads as text and as hex. A
/// received line stays open, gathering bytes, until a newline ends it, it grows long, or the port goes quiet.
/// </summary>
public sealed partial class SerialLine : ObservableObject, IClipboardText
{
    /// <summary>A copied line reads as the transcript shows it: when, which way, what.</summary>
    public string ClipboardText => $"{Time} {Arrow} {Text}";
    readonly List<byte> _bytes = [];

    public SerialLine(DateTimeOffset at, SerialDirection direction, ReadOnlySpan<byte> bytes)
    {
        At = at;
        LastAt = at;
        Direction = direction;
        _bytes.AddRange(bytes.ToArray());
    }

    /// <summary>A line of the page's own, such as "Connected at 115200 8N1".</summary>
    public static SerialLine Note(string text) => new(DateTimeOffset.Now, SerialDirection.Note, Encoding.UTF8.GetBytes(text));

    public DateTimeOffset At { get; }
    public DateTimeOffset LastAt { get; private set; }
    public SerialDirection Direction { get; }
    public int Length => _bytes.Count;
    public bool EndsLine => _bytes.Count > 0 && _bytes[^1] == (byte)'\n';
    public IReadOnlyList<byte> Bytes => _bytes;

    public string Time => At.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string Arrow => Direction switch { SerialDirection.Received => "←", SerialDirection.Sent => "→", _ => "·" };

    /// <summary>The bytes as UTF-8, the line ending dropped and other control characters drawn as their symbols.</summary>
    public string Text
    {
        get
        {
            var text = Encoding.UTF8.GetString([.. _bytes]).TrimEnd('\r', '\n');
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) sb.Append(c is < ' ' and not '\t' ? (char)(0x2400 + c) : c == '\x7F' ? '␡' : c);
            return sb.ToString();
        }
    }

    public string Hex => string.Join(' ', _bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    internal void Append(ReadOnlySpan<byte> more, DateTimeOffset at)
    {
        _bytes.AddRange(more.ToArray());
        LastAt = at;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Length));
    }
}
