using System.Buffers.Binary;
using System.Threading.Channels;

namespace Amp.Nfc;

/// <summary>One decoded Linux <c>struct input_event</c> (the timestamp is not used).</summary>
public readonly record struct InputEvent(ushort Type, ushort Code, int Value)
{
    public const ushort Key = 1; // EV_KEY
    public const int Release = 0;
    public const int Press = 1;
    public const int Repeat = 2; // kernel auto-repeat while the key is held
}

/// <summary>
/// The size of <c>struct input_event</c> depends on the userland word size: the leading timestamp is
/// two machine words (seconds, microseconds), followed by u16 type, u16 code and s32 value.
/// </summary>
public enum InputLayout { Bits32, Bits64 }

public static class InputEventFormat
{
    public static InputLayout Native => IntPtr.Size == 8 ? InputLayout.Bits64 : InputLayout.Bits32;

    private static int Word(InputLayout layout) => layout == InputLayout.Bits64 ? 8 : 4;

    /// <summary>24 bytes on 64-bit userland, 16 on 32-bit.</summary>
    public static int RecordSize(InputLayout layout) => 2 * Word(layout) + 8;

    /// <summary>Decodes one whole record in host byte order, as the kernel writes it.</summary>
    public static InputEvent Decode(ReadOnlySpan<byte> record, InputLayout layout)
    {
        var at = 2 * Word(layout);
        return BitConverter.IsLittleEndian
            ? new(BinaryPrimitives.ReadUInt16LittleEndian(record[at..]), BinaryPrimitives.ReadUInt16LittleEndian(record[(at + 2)..]),
                  BinaryPrimitives.ReadInt32LittleEndian(record[(at + 4)..]))
            : new(BinaryPrimitives.ReadUInt16BigEndian(record[at..]), BinaryPrimitives.ReadUInt16BigEndian(record[(at + 2)..]),
                  BinaryPrimitives.ReadInt32BigEndian(record[(at + 4)..]));
    }
}

public sealed class InputDeviceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A source of input events. The sequence ends when the device is gone or the token is cancelled.</summary>
public interface IInputDevice : IDisposable
{
    IAsyncEnumerable<InputEvent> ReadAsync(CancellationToken cancellationToken);
}

public static class InputDevicePath
{
    /// <summary>Returns why <paramref name="path"/> cannot name an input device node, or null. Nothing is opened.</summary>
    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "must not be blank";
        if (path.Contains('\0'))
            return "must not contain a NUL character";
        if (!path.StartsWith('/'))
            return "must be an absolute path such as /dev/input/event3";
        if (path.EndsWith('/'))
            return "must name a device file, not a directory";
        if (path.Split('/').Contains(".."))
            return "must not contain .. segments";
        return null;
    }
}

/// <summary>
/// Reads <c>struct input_event</c> records from a Linux evdev node (or any file or pipe carrying the
/// same records). Reading is blocking, so it runs on a dedicated background thread that hands events
/// over a channel; disposing releases the handle and does not wait for a read still blocked in the
/// kernel (the thread is a background thread and cannot keep the process alive).
/// </summary>
public sealed class EvdevInputDevice : IInputDevice
{
    private readonly FileStream _stream;
    private readonly InputLayout _layout;
    private readonly Channel<InputEvent> _events = Channel.CreateBounded<InputEvent>(
        new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _disposed = new();
    private int _started;

    private EvdevInputDevice(FileStream stream, InputLayout layout)
    {
        _stream = stream;
        _layout = layout;
    }

    public static EvdevInputDevice Open(string path, InputLayout? layout = null)
    {
        try
        {
            // No buffering: a read must return the records the kernel has, not wait to fill a buffer.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            return new(stream, layout ?? InputEventFormat.Native);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InputDeviceException($"cannot open input device {path}: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<InputEvent> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _ = Task.Factory.StartNew(Pump, TaskCreationOptions.LongRunning);

        await foreach (var ev in _events.Reader.ReadAllAsync(cancellationToken))
            yield return ev;
    }

    private void Pump()
    {
        var size = InputEventFormat.RecordSize(_layout);
        var buffer = new byte[size];
        try
        {
            while (!_disposed.IsCancellationRequested)
            {
                var filled = 0;
                while (filled < size)
                {
                    var read = _stream.Read(buffer, filled, size - filled);
                    if (read == 0)
                    {
                        _events.Writer.TryComplete(); // end of file: the device is gone (or the file is exhausted)
                        return;
                    }
                    filled += read;
                }
                _events.Writer.WriteAsync(InputEventFormat.Decode(buffer, _layout), _disposed.Token).AsTask().GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _events.Writer.TryComplete(new InputDeviceException($"input device read failed: {ex.Message}", ex));
        }
    }

    public void Dispose()
    {
        _disposed.Cancel();
        _events.Writer.TryComplete();
        _stream.Dispose();
    }
}
