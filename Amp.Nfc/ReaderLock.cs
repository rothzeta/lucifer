namespace Amp.Nfc;

public sealed class ReaderOwnedException(string message) : Exception(message);

/// <summary>
/// One process owns a reader at a time. The lock is an exclusively opened file named for the
/// reader inside the IPC directory, so it is released when the owner exits, even after a crash. A clean release removes the file;
/// a crash leaves a harmless unlocked file that the next owner reuses.
/// </summary>
public sealed class ReaderLock : IDisposable
{
    private readonly FileStream _file;

    private ReaderLock(FileStream file) => _file = file;

    public static ReaderLock Acquire(string directory, string device)
    {
        Directory.CreateDirectory(directory);

        // Named by the real device, so /dev/lucifer-nfc and /dev/ttyUSB0 are one reader.
        var name = new string(Resolve(device).Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        var path = Path.Combine(directory, $"lucifer-reader-{name}.lock");

        try
        {
            return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
        }
        catch (IOException)
        {
            throw new ReaderOwnedException(
                $"Reader {device} is already owned by another Lucifer process (lock {path}). " +
                "Stop the player service before running tag commands, then start it again.");
        }
    }

    private static string Resolve(string device)
    {
        try
        {
            var full = Path.GetFullPath(device);
            return new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch (IOException) { return device; }
        catch (UnauthorizedAccessException) { return device; }
    }

    public void Dispose() => _file.Dispose();
}
