namespace Amp.Nfc;

public sealed class ReaderOwnedException(string message) : Exception(message);

/// <summary>
/// One process owns a reader at a time. The lock is an exclusively opened file named for the
/// reader inside the IPC directory, so it is released when the owner exits, even after a crash.
/// </summary>
public sealed class ReaderLock : IDisposable
{
    private readonly FileStream _file;

    private ReaderLock(FileStream file) => _file = file;

    public static ReaderLock Acquire(string directory, string device)
    {
        Directory.CreateDirectory(directory);

        var name = new string(device.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        var path = Path.Combine(directory, $"lucifer-reader-{name}.lock");

        try
        {
            return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            throw new ReaderOwnedException(
                $"Reader {device} is already owned by another Lucifer process (lock {path}). " +
                "Stop the player service before running tag commands, then start it again.");
        }
    }

    public void Dispose() => _file.Dispose();
}
