namespace Amp.Nfc;

/// <summary>
/// The directory that holds the mpv socket and the reader lock. When LUCIFER_IPC_DIR is not set the
/// default is private to the user (never the bare, world-writable /tmp); an explicit value is used
/// as given, because the packaged service points at /run/lucifer, which systemd creates.
/// </summary>
public static class IpcDirectoryPolicy
{
    /// <summary>$XDG_RUNTIME_DIR/lucifer when that variable holds an absolute path, else /tmp/lucifer-&lt;user&gt;.</summary>
    public static string DefaultPath(Func<string, string?> env)
    {
        var runtime = env("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtime) && Path.IsPathRooted(runtime)
            ? Path.Combine(runtime, "lucifer")
            : Path.Combine("/tmp", "lucifer-" + Environment.UserName);
    }

    /// <summary>
    /// Creates the default directory with mode 0700 and refuses one that is a symlink, not 0700, or not
    /// writable by this user (so a directory another user created first is rejected). Any other
    /// directory is only created, as before.
    /// </summary>
    public static void Prepare(LuciferOptions options)
    {
        if (!options.IpcDirectoryIsPrivateDefault)
        {
            Directory.CreateDirectory(options.IpcDirectory);
            return;
        }

        var path = options.IpcDirectory;
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
            Directory.CreateDirectory(parent);

        if (!Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch (IOException) when (Directory.Exists(path)) { } // lost a race: verify what is there
        }

        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null)
            throw Unsafe(path, "is a symbolic link");
        if (info.UnixFileMode != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            throw Unsafe(path, "must have mode 0700");

        // A directory owned by another user cannot be written here (0700), which also proves it is ours.
        var probe = Path.Combine(path, $".probe-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unsafe(path, "is not writable by this user");
        }
    }

    private static InvalidOperationException Unsafe(string path, string why) =>
        new($"Default IPC directory {path} {why}. Remove it or set LUCIFER_IPC_DIR to a directory you own.");
}
