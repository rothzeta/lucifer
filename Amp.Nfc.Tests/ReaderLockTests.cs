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

    [Fact]
    public void A_symlink_to_the_reader_and_the_reader_itself_are_one_owner()
    {
        Directory.CreateDirectory(_dir);
        var device = Path.Combine(_dir, "ttyUSB0");
        File.WriteAllText(device, "");
        var link = Path.Combine(_dir, "lucifer-nfc");
        File.CreateSymbolicLink(link, device);
        var relative = Path.Combine(_dir, "rel-nfc");
        File.CreateSymbolicLink(relative, "ttyUSB0");

        using var first = ReaderLock.Acquire(_dir, link);

        Assert.Throws<ReaderOwnedException>(() => ReaderLock.Acquire(_dir, device));
        Assert.Throws<ReaderOwnedException>(() => ReaderLock.Acquire(_dir, relative));
    }

    [Fact]
    public void Releasing_the_lock_keeps_the_file_and_lets_the_next_owner_in()
    {
        Directory.CreateDirectory(_dir);
        var other = Path.Combine(_dir, "amp-mpv.sock");
        File.WriteAllText(other, "");

        ReaderLock.Acquire(_dir, "/dev/ttyUSB0").Dispose();

        var lockFile = Assert.Single(Directory.GetFiles(_dir, "lucifer-reader-*"));
        Assert.True(File.Exists(other));
        using var next = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");
        Assert.Single(Directory.GetFiles(_dir, "lucifer-reader-*"), lockFile);
    }

    [Fact]
    public void A_refused_second_owner_leaves_the_first_owners_lock_in_place()
    {
        using var first = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");

        Assert.Throws<ReaderOwnedException>(() => ReaderLock.Acquire(_dir, "/dev/ttyUSB0"));

        Assert.Single(Directory.GetFiles(_dir, "lucifer-reader-*"));
        Assert.Throws<ReaderOwnedException>(() => ReaderLock.Acquire(_dir, "/dev/ttyUSB0"));
    }
}
