using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class ReaderLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lucifer-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Second_owner_of_the_same_reader_is_refused_with_a_useful_error()
    {
        using var first = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");

        var ex = Assert.Throws<ReaderOwnedException>(() => ReaderLock.Acquire(_dir, "/dev/ttyUSB0"));

        Assert.Contains("/dev/ttyUSB0", ex.Message);
        Assert.Contains("stop", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ownership_is_released_on_dispose()
    {
        ReaderLock.Acquire(_dir, "/dev/ttyUSB0").Dispose();

        using var again = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");
    }

    [Fact]
    public void Different_readers_do_not_conflict()
    {
        using var first = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");
        using var second = ReaderLock.Acquire(_dir, "/dev/ttyUSB1");
    }
}
