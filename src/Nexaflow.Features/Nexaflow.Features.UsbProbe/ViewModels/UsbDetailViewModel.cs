using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Descriptors.Layout;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>One decoded field, as the descriptor table shows it.</summary>
public sealed class UsbFieldRow(UsbDescriptorField descriptorField, int descriptorOffset) : IClipboardText
{
    public string ClipboardText => $"+{Offset}\t{Name}\t{Size}\t{Value}\t{Meaning}";
    public UsbDescriptorField Field => descriptorField;
    public string Name => descriptorField.Name;

    /// <summary>From the start of its descriptor, as the specifications number them.</summary>
    public int Offset => descriptorField.Offset - descriptorOffset;

    public int Size => descriptorField.Size;
    public string Value => descriptorField.Value;
    public string Meaning => descriptorField.Meaning ?? "";
    public ByteSpan Span => new(descriptorField.Offset, descriptorField.Size);
}

/// <summary>
/// Everything the detail pane shows about one location: its facts, and — for anything a descriptor describes —
/// the decoded fields beside the blob they came from, the two linked by hover in both directions.
/// </summary>
public sealed partial class UsbDetailViewModel : ObservableObject
{
    public UsbDetailViewModel(UsbLocation location)
    {
        Location = location;
        FactGroups = UsbLocationFacts.Of(location);

        if (location.Map is { } map && (location.Entry ?? (map.Entries.Count > 0 ? map.Entries[0] : null)) is { } entry)
        {
            Map = map;
            Entry = entry;
            Fields = [.. entry.Fields.Select(f => new UsbFieldRow(f, entry.Offset))];
            Selection = new ByteSpan(entry.Offset, entry.Length);
        }
    }

    public UsbLocation Location { get; }
    public string Title => Location.Name;
    public string Summary => Location.Summary;
    public string Path => Location.Path.Length == 0 ? "usb:" : Location.Path;
    public IconRef Icon => UsbIcons.Of(Location);

    public IReadOnlyList<UsbFactGroup> FactGroups { get; }
    public bool HasFacts => FactGroups.Count > 0;

    public UsbDescriptorMap? Map { get; }
    public UsbDescriptorEntry? Entry { get; }
    public bool HasDescriptor => Entry is not null;

    /// <summary>False for the root, which is all listing: the pane then shows its heading alone.</summary>
    public bool HasContent => HasFacts || HasDescriptor;
    public byte[]? Bytes => Map?.Bytes;
    public IReadOnlyList<UsbFieldRow> Fields { get; } = [];
    public ByteSpan? Selection { get; }

    /// <summary>"Configuration 1 · 98 bytes — this descriptor is bytes 18–26".</summary>
    public string BlobCaption => Map is null || Entry is null ? "" :
        Str.Format("UsbProbe.Detail.BlobCaption", Map.Title, Map.Bytes.Length, Entry.Offset, Entry.Offset + Entry.Length - 1);

    public IReadOnlyList<string> Problems => Map?.Problems ?? [];

    /// <summary>The bytes lit in the hex view: the field under the pointer, in the table or in the hex itself.</summary>
    [ObservableProperty] private ByteSpan? _highlight;

    /// <summary>The byte under the pointer in the hex view; -1 when it is elsewhere.</summary>
    [ObservableProperty] private int _hoveredOffset = -1;

    /// <summary>The table row under the pointer.</summary>
    [ObservableProperty] private UsbFieldRow? _hoveredField;

    /// <summary>What the hovered byte is, wherever in the blob it lies: "Endpoint 0x81 › wMaxPacketSize = 0x0200 (512) — 512 bytes".</summary>
    [ObservableProperty] private string _hoverText = "";

    partial void OnHoveredFieldChanged(UsbFieldRow? value)
    {
        Highlight = value?.Span;
        HoverText = value is null ? "" : Describe(Entry!, value.Field);
    }

    partial void OnHoveredOffsetChanged(int value)
    {
        if (Map is null || value < 0)
        {
            if (HoveredField is null) { Highlight = null; HoverText = ""; }
            return;
        }
        var entry = Map.EntryAt(value);
        var field = entry?.FieldAt(value);
        Highlight = field is null ? null : new ByteSpan(field.Offset, field.Size);
        HoverText = entry is null ? "" : field is null ? UsbLocationText.EntryName(Map, entry) : Describe(entry, field);
    }

    string Describe(UsbDescriptorEntry entry, UsbDescriptorField field) =>
        $"{UsbLocationText.EntryName(Map!, entry)} › {field.Name} = {field.Value}{(field.Meaning is { } m ? $" — {m}" : "")}";
}
