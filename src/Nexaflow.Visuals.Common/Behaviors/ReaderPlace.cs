using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Keeps the reader looking at what they were looking at while a list's items change under them — a folder read again
/// after a delete, a paste or a refresh. Whatever is removed, inserted or moved above it, the item at the top of the list
/// is put back at the top, for as long as it is still in the list; once it is gone the list is left to place itself.
/// <para>
/// A virtualizing panel does this on its own most of the time, by keeping the container it had at the top where it was —
/// but only when it measures before that container is let go, which is the dispatcher's timing, so under load a list
/// lands a row away. Asking for the item's place outright leaves nothing to timing: the scroll viewer carries the request
/// out after the panel has measured, whatever the panel made of the change. For a list that scrolls by item, where an
/// offset is an item's index. Switch it on with <c>beh:ReaderPlace.IsHeld="True"</c>.
/// </para>
/// </summary>
public static class ReaderPlace
{
    public static readonly DependencyProperty IsHeldProperty =
        DependencyProperty.RegisterAttached(
            "IsHeld", typeof(bool), typeof(ReaderPlace),
            new PropertyMetadata(false, OnIsHeldChanged));

    public static void SetIsHeld(DependencyObject element, bool value)
        => element.SetValue(IsHeldProperty, value);

    public static bool GetIsHeld(DependencyObject element)
        => (bool)element.GetValue(IsHeldProperty);

    private static readonly DependencyProperty HolderProperty =
        DependencyProperty.RegisterAttached("Holder", typeof(Holder), typeof(ReaderPlace));

    private static void OnIsHeldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list) return;

        (list.GetValue(HolderProperty) as Holder)?.Detach();
        list.ClearValue(HolderProperty);

        if (e.NewValue is true) list.SetValue(HolderProperty, new Holder(list));
    }

    /// <summary>One list's reader: the item they have at the top, and the scroll viewer that puts it back there.</summary>
    private sealed class Holder
    {
        private readonly ItemsControl _list;
        private ScrollViewer? _viewer;
        private object? _top;

        public Holder(ItemsControl list)
        {
            _list = list;
            _list.AddHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrolled);
            ((INotifyCollectionChanged)_list.Items).CollectionChanged += OnItemsChanged;
        }

        public void Detach()
        {
            _list.RemoveHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrolled);
            ((INotifyCollectionChanged)_list.Items).CollectionChanged -= OnItemsChanged;
        }

        private void OnScrolled(object sender, ScrollChangedEventArgs e)
        {
            // The list's own viewer, not one inside a row's template.
            if (e.OriginalSource is not ScrollViewer viewer || !ReferenceEquals(viewer.TemplatedParent, _list)) return;
            if (!viewer.CanContentScroll || VirtualizingPanel.GetScrollUnit(_list) != ScrollUnit.Item) return;

            _viewer = viewer;
            var at = (int)e.VerticalOffset;
            _top = at < _list.Items.Count ? _list.Items[at] : null;
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_viewer is null || _top is null) return;

            var at = _list.Items.IndexOf(_top);
            if (at < 0)
            {
                _top = null;
                return;
            }

            // Asked after every change, not only when it moved: requests are carried out in order after the panel measures,
            // so the place asked for last is the one that stands.
            _viewer.ScrollToVerticalOffset(at);
        }
    }
}
