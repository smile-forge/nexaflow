namespace Nexaflow.IO.Usb.Inventory;

/// <summary>
/// One machine has one USB tree, so every page looking at it shares one inventory: one hot-plug registration, one
/// walk of the hubs per change, one set of string-descriptor reads waking each device. <see cref="Acquire"/> hands
/// each consumer its own <see cref="IUsbDeviceInventory"/> over that shared one; the shared inventory is built by the
/// first and disposed with the last.
/// </summary>
/// <remarks>
/// A consumer's view behaves as a whole inventory: it raises <see cref="IUsbDeviceInventory.Changed"/> only while it
/// is held, and its <see cref="IUsbDeviceInventory.StartAsync"/> awaits the one first pass however many consumers ask
/// for it — a page opened second finds the tree already read.
/// </remarks>
public sealed class UsbInventoryShare
{
    /// <summary>The share this process uses for the machine's own USB tree.</summary>
    public static UsbInventoryShare Machine { get; } = new();

    readonly object _gate = new();
    Shared? _shared;

    /// <summary>A view over the shared inventory, building it with <paramref name="create"/> when nobody holds one.
    /// Dispose the view to let go of it.</summary>
    public IUsbDeviceInventory Acquire(Func<IUsbDeviceInventory> create)
    {
        lock (_gate)
        {
            _shared ??= new Shared(this, create());
            _shared.Holders++;
            return new View(_shared);
        }
    }

    /// <summary>How many views are held now; zero once the shared inventory has been let go.</summary>
    public int Holders { get { lock (_gate) return _shared?.Holders ?? 0; } }

    void Release(Shared shared)
    {
        bool last;
        lock (_gate)
        {
            last = --shared.Holders == 0;
            if (last && ReferenceEquals(_shared, shared)) _shared = null;
        }
        if (last) shared.Inventory.Dispose();
    }

    sealed class Shared(UsbInventoryShare owner, IUsbDeviceInventory inventory)
    {
        readonly object _startGate = new();
        Task? _start;

        public UsbInventoryShare Owner { get; } = owner;
        public IUsbDeviceInventory Inventory { get; } = inventory;
        public int Holders;

        /// <summary>The one first pass. Not tied to any one view's token: a page closed mid-start must not cancel the
        /// pass another page is waiting on.</summary>
        public Task StartAsync()
        {
            lock (_startGate) return _start ??= Inventory.StartAsync(CancellationToken.None);
        }
    }

    sealed class View : IUsbDeviceInventory
    {
        readonly Shared _shared;
        int _disposed;

        public View(Shared shared)
        {
            _shared = shared;
            _shared.Inventory.Changed += Forward;
        }

        public UsbInventorySnapshot Snapshot => _shared.Inventory.Snapshot;

        public event EventHandler<UsbInventoryChangedEventArgs>? Changed;

        public Task StartAsync(CancellationToken ct) => _shared.StartAsync().WaitAsync(ct);

        public Task<UsbInventorySnapshot> RefreshAsync(CancellationToken ct) => _shared.Inventory.RefreshAsync(ct);

        public Task<UsbDeviceNode?> RefreshDeviceAsync(string instanceId, CancellationToken ct) =>
            _shared.Inventory.RefreshDeviceAsync(instanceId, ct);

        public Task<UsbInventorySnapshot> UseNamesAsync(Naming.UsbIdsDatabase? names, CancellationToken ct) =>
            _shared.Inventory.UseNamesAsync(names, ct);

        void Forward(object? sender, UsbInventoryChangedEventArgs e) => Changed?.Invoke(this, e);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _shared.Inventory.Changed -= Forward;
            Changed = null;
            _shared.Owner.Release(_shared);
        }
    }
}
