using System.IO;
using System.IO.Ports;

namespace Nexaflow.Features.Serial.Services;

/// <summary>How a port is opened: the line settings both ends must agree on, and the two control lines.</summary>
public sealed record SerialSettings(int BaudRate, int DataBits, Parity Parity, StopBits StopBits, Handshake Handshake, bool Dtr, bool Rts)
{
    public static SerialSettings Default { get; } = new(115_200, 8, Parity.None, StopBits.One, Handshake.None, Dtr: false, Rts: false);

    /// <summary>"115200 8N1", the way serial settings are written down.</summary>
    public string Summary => $"{BaudRate} {DataBits}{Parity.ToString()[0]}{StopBits switch { StopBits.Two => "2", StopBits.OnePointFive => "1.5", _ => "1" }}";
}

/// <summary>An open serial port: bytes in as they come, bytes out on request, and the control lines.</summary>
public interface ISerialLink : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Opens <paramref name="port"/>. Throws when it is missing or another program has it.</summary>
    void Open(string port, SerialSettings settings);

    void Close();

    /// <summary>Raised off the UI thread with each block the port delivers.</summary>
    event Action<byte[]>? Received;

    /// <summary>Raised off the UI thread when the port stops working under it — unplugged, mostly.</summary>
    event Action<string>? Faulted;

    Task WriteAsync(byte[] data, CancellationToken ct);

    bool Dtr { get; set; }
    bool Rts { get; set; }
}

/// <summary>
/// <see cref="ISerialLink"/> over <see cref="SerialPort"/>. Reads go through the port's stream in a loop rather than
/// its <c>DataReceived</c> event, which can fire late and in odd pieces; the loop delivers what each read returned.
/// </summary>
public sealed class SystemSerialLink : ISerialLink
{
    readonly object _gate = new();
    SerialPort? _port;
    bool _closing;

    public bool IsOpen { get { lock (_gate) return _port?.IsOpen == true; } }

    public event Action<byte[]>? Received;
    public event Action<string>? Faulted;

    public void Open(string port, SerialSettings settings)
    {
        Close();
        var serial = new SerialPort(port, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            Handshake = settings.Handshake,
            DtrEnable = settings.Dtr,
            RtsEnable = settings.Rts,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 3000,
        };
        serial.Open();
        lock (_gate)
        {
            _port = serial;
            _closing = false;
        }
        _ = Task.Run(() => ReadLoopAsync(serial));
    }

    async Task ReadLoopAsync(SerialPort serial)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int read = await serial.BaseStream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0) break;
                Received?.Invoke(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException or UnauthorizedAccessException or OperationCanceledException)
        {
            bool closing;
            lock (_gate) closing = _closing || !ReferenceEquals(_port, serial);
            if (!closing) Faulted?.Invoke(e.Message);
        }
    }

    public Task WriteAsync(byte[] data, CancellationToken ct)
    {
        SerialPort port;
        lock (_gate) port = _port ?? throw new InvalidOperationException("The port is not open.");
        return port.BaseStream.WriteAsync(data, ct).AsTask();
    }

    public bool Dtr
    {
        get { lock (_gate) return _port?.DtrEnable ?? false; }
        set { lock (_gate) if (_port is { IsOpen: true } p) p.DtrEnable = value; }
    }

    public bool Rts
    {
        get { lock (_gate) return _port?.RtsEnable ?? false; }
        set { lock (_gate) if (_port is { IsOpen: true } p) p.RtsEnable = value; }
    }

    public void Close()
    {
        SerialPort? port;
        lock (_gate)
        {
            port = _port;
            _port = null;
            _closing = true;
        }
        if (port is null) return;
        try { port.Close(); }
        catch (IOException) { }
        port.Dispose();
    }

    public void Dispose() => Close();
}
