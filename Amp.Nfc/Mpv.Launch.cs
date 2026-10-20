using System.Net.Sockets;
using System.Diagnostics;

namespace Amp.Nfc;

// Process launch, readiness and child/socket teardown.
public sealed partial class Mpv
{
    /// <summary>Connect to an mpv that is already serving <paramref name="socketPath"/>; the caller owns it.</summary>
    public static Task<Mpv> ConnectAsync(
        string socketPath,
        MpvTimeouts timeouts,
        TimeSpan startTimeout,
        CancellationToken cancellationToken) =>
        OpenAsync(socketPath, timeouts, startTimeout, null, null, cancellationToken);

    /// <summary>
    /// Start mpv and own it from the moment it exists. Ready means its socket accepted a connection
    /// and answered a harmless request. On failure or cancellation the child is stopped and only a
    /// socket this instance created is removed.
    /// </summary>
    public static async Task<Mpv> StartAsync(
        string mpvPath,
        string socketPath,
        string audioDevice,
        TimeSpan startTimeout,
        CancellationToken cancellationToken,
        MpvTimeouts? timeouts = null)
    {
        timeouts ??= MpvTimeouts.Default;
        RefuseLiveSocketOrRemoveStale(socketPath, timeouts);

        // Output is redirected and discarded (not inherited): it must never reach the journal.
        var startInfo = new ProcessStartInfo
        {
            FileName = mpvPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in LaunchArguments(audioDevice, socketPath))
            startInfo.ArgumentList.Add(argument);

        Process child;
        try
        {
            child = Process.Start(startInfo)
                ?? throw new MpvException(MpvFailureKind.StartFailed, "Could not start mpv.");
            child.OutputDataReceived += static (_, _) => { };
            child.ErrorDataReceived += static (_, _) => { };
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is not MpvException)
        {
            throw new MpvException(MpvFailureKind.StartFailed, $"Could not start mpv: {ex.Message}", ex);
        }

        var ownership = new SocketOwnership();
        try
        {
            return await OpenAsync(socketPath, timeouts, startTimeout, child, ownership, cancellationToken);
        }
        catch
        {
            StopChild(child);
            if (ownership.Owned)
                TryDelete(socketPath);
            throw;
        }
    }

    /// <summary>
    /// Playback stops at the end of the album: neither the playlist nor a single file repeats
    /// (mpv's default, also stated explicitly).
    /// </summary>
    internal static IReadOnlyList<string> LaunchArguments(string audioDevice, string socketPath) =>
    [
        // No /etc/mpv or ~/.config/mpv: a user config could add scripts or a log-file that writes
        // the authenticated stream URLs to disk. ytdl_hook would hand a URL to yt-dlp on its argv.
        "--no-config",
        "--ytdl=no",
        "--idle=yes",
        "--no-video",
        // mpv logs "Playing: <stream URL>" (with the Subsonic credentials) at info level.
        "--no-terminal",
        "--msg-level=all=no",
        "--loop-playlist=no",
        "--loop-file=no",
        $"--audio-device={audioDevice}",
        $"--input-ipc-server={socketPath}",
    ];

    private sealed class SocketOwnership { public bool Owned; }

    // Only a socket that nothing answers on is stale. One that answers belongs to someone else.
    private static void RefuseLiveSocketOrRemoveStale(string socketPath, MpvTimeouts timeouts)
    {
        if (!File.Exists(socketPath))
            return;

        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath))
                .WaitAsync(timeouts.Connect).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            TryDelete(socketPath);
            return;
        }

        throw new MpvException(MpvFailureKind.StartFailed,
            $"{socketPath} is served by another process; not starting mpv against it.");
    }

    private static async Task<Mpv> OpenAsync(
        string socketPath,
        MpvTimeouts timeouts,
        TimeSpan startTimeout,
        Process? child,
        SocketOwnership? ownership,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        Exception? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (child is { HasExited: true })
                throw new MpvException(MpvFailureKind.ProcessExited,
                    $"mpv exited with code {child.ExitCode} before its IPC was ready.");

            if (ownership is not null && !ownership.Owned && File.Exists(socketPath))
                ownership.Owned = true;

            var remaining = startTimeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new MpvException(MpvFailureKind.StartFailed,
                    $"mpv IPC was not ready within {startTimeout.TotalSeconds:0.##}s.", last);

            var attempt = new Mpv(socketPath, timeouts);
            try
            {
                await attempt.ConnectAndVerifyAsync(Min(timeouts.Connect, remaining), cancellationToken);
                // The child may have bound its socket after the sample above; the path was clear when
                // it was launched, so one that is there once we are connected is the child's. Residual
                // risk: another process replacing the path in this window is indistinguishable here.
                if (ownership is not null && !ownership.Owned && File.Exists(socketPath))
                    ownership.Owned = true;
                attempt.Adopt(child, ownership?.Owned ?? false);
                return attempt;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                attempt.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                attempt.Dispose();
                last = ex;
            }

            var pause = Min(TimeSpan.FromMilliseconds(50), startTimeout - clock.Elapsed);
            if (pause > TimeSpan.Zero)
                await Task.Delay(pause, cancellationToken);
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private async Task ConnectAndVerifyAsync(TimeSpan connectBudget, CancellationToken cancellationToken)
    {
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(connectBudget);
            try
            {
                await _socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), connect.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MpvException(MpvFailureKind.Timeout, "Timed out connecting to the mpv socket.");
            }
        }

        _stream = new NetworkStream(_socket, ownsSocket: false);
        _ = ReadLoopAsync();

        await _operations.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(["get_property", "mpv-version"], null, cancellationToken);
            await SendAsync(["observe_property", 1, "pause"], null, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    private void Adopt(Process? child, bool ownsSocket)
    {
        _ownsSocket = ownsSocket;
        if (child is null)
            return;

        _child = child;
        child.EnableRaisingEvents = true;
        child.Exited += (_, _) => Lose(new MpvException(MpvFailureKind.ProcessExited, ExitMessage(child)));
        if (child.HasExited)
            Lose(new MpvException(MpvFailureKind.ProcessExited, ExitMessage(child)));
    }

    private static string ExitMessage(Process child)
    {
        try { return $"mpv exited with code {child.ExitCode}."; }
        catch (InvalidOperationException) { return "mpv exited."; }
    }


    private static void StopChild(Process child)
    {
        try
        {
            if (!child.HasExited)
            {
                child.Kill();
                child.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException) { }
        finally { child.Dispose(); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
