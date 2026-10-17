using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class IpcDirectoryTests : IDisposable
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Path.Combine("/tmp", "id-" + Guid.NewGuid().ToString("N")[..8]);

    public IpcDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static LuciferOptions Load(params (string Name, string? Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return LuciferOptions.Load(name => map.GetValueOrDefault(name), Mode.TagRead).Options!;
    }

    [Fact]
    public void Default_uses_the_runtime_directory_when_set()
    {
        var options = Load(("XDG_RUNTIME_DIR", "/run/user/1234"));

        Assert.Equal("/run/user/1234/lucifer", options.IpcDirectory);
        Assert.True(options.IpcDirectoryIsPrivateDefault);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative/path")]
    public void Default_without_a_usable_runtime_directory_is_per_user_not_bare_tmp(string? xdg)
    {
        var options = Load(("XDG_RUNTIME_DIR", xdg));

        Assert.NotEqual("/tmp", options.IpcDirectory);
        Assert.StartsWith("/tmp/lucifer-", options.IpcDirectory);
        Assert.True(options.IpcDirectoryIsPrivateDefault);
    }

    [Fact]
    public void Explicit_directory_overrides_the_default_and_is_not_policed()
    {
        var options = Load(("LUCIFER_IPC_DIR", "/run/lucifer"), ("XDG_RUNTIME_DIR", "/run/user/1234"));

        Assert.Equal("/run/lucifer", options.IpcDirectory);
        Assert.False(options.IpcDirectoryIsPrivateDefault);
    }

    [Fact]
    public void Prepare_creates_the_default_directory_with_mode_0700()
    {
        var options = Load(("XDG_RUNTIME_DIR", _root));

        IpcDirectoryPolicy.Prepare(options);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            new DirectoryInfo(options.IpcDirectory).UnixFileMode);
        IpcDirectoryPolicy.Prepare(options); // idempotent
    }

    [Fact]
    public void Prepare_refuses_a_default_directory_that_is_not_private()
    {
        var options = Load(("XDG_RUNTIME_DIR", _root));
        Directory.CreateDirectory(options.IpcDirectory);
        File.SetUnixFileMode(options.IpcDirectory, Private | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var ex = Assert.Throws<InvalidOperationException>(() => IpcDirectoryPolicy.Prepare(options));

        Assert.Contains("0700", ex.Message);
    }

    [Fact]
    public void Prepare_refuses_a_default_directory_that_is_a_symlink()
    {
        var options = Load(("XDG_RUNTIME_DIR", _root));
        var target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target, Private);
        Directory.CreateSymbolicLink(options.IpcDirectory, target);

        var ex = Assert.Throws<InvalidOperationException>(() => IpcDirectoryPolicy.Prepare(options));

        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void Prepare_leaves_an_explicit_directory_mode_alone()
    {
        var dir = Path.Combine(_root, "explicit");
        var options = Load(("LUCIFER_IPC_DIR", dir));

        IpcDirectoryPolicy.Prepare(options);

        Assert.True(Directory.Exists(dir));
    }
}
