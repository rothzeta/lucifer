using System.Diagnostics;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

// Real Mpv class and real child processes (shell scripts standing in for mpv), an in-process fake
// mpv IPC peer, and a scripted reader. They prove the application's ownership, deadline and exit
// policy at those boundaries. They do not prove behavior of real mpv, the PN532 binding, a Pi, or
// systemd; those are device-pending (LUC-08, LUC-12).
public class PlaySessionTests : IDisposable
{
    private const string Release = "a24495e4-eb70-48cd-b0fa-31a606459d26";

    private static readonly MpvTimeouts FastIpc = new(
        Connect: TimeSpan.FromMilliseconds(300),
        Send: TimeSpan.FromMilliseconds(500),
        Reply: TimeSpan.FromSeconds(30),
        StreamStart: TimeSpan.FromSeconds(30));

    private readonly string _dir = Path.Combine("/tmp", "ls-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _disposables = [];
    private string Socket => Path.Combine(_dir, "amp-mpv.sock");
    private string PidFile => Path.Combine(_dir, "pid");

    public PlaySessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_disposables)) d.Dispose();
        try
        {
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid) && Alive(pid))
                Process.GetProcessById(pid).Kill();
        }
        catch { }
        Directory.Delete(_dir, recursive: true);
    }

    private string Script(string body)
    {
        var path = Path.Combine(_dir, "fake-mpv.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private string ChildScript() => Script($"echo $$ > {PidFile}\nexec sleep 60");

    private LuciferOptions Options(string mpvPath, int shutdownSeconds = 3)
    {
        var env = new Dictionary<string, string>
        {
            ["LUCIFER_MPV_PATH"] = mpvPath,
            ["LUCIFER_IPC_DIR"] = _dir,
            ["LUCIFER_MPV_START_TIMEOUT_SECONDS"] = "30",
            ["LUCIFER_SHUTDOWN_TIMEOUT_SECONDS"] = shutdownSeconds.ToString(),
            ["NAVIDROME_URL"] = "https://music.example.test",
            ["NAVIDROME_USER"] = "u",
            ["NAVIDROME_PASSWORD"] = "pw",
        };
        var loaded = LuciferOptions.Load(k => env.GetValueOrDefault(k), Mode.Play);
        return loaded.Options ?? throw new InvalidOperationException(string.Join(";", loaded.Errors));
    }

    private static bool Alive(int pid) => Directory.Exists($"/proc/{pid}") &&
        !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z");

    private async Task<int> ReadPid()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid)) return pid;
            await Task.Delay(20);
        }
        throw new TimeoutException("child never wrote its pid");
    }

    // "mpv" starts serving its socket shortly after it was launched.
    private void ServeSocketLater(int delayMs = 100) =>
        _ = Task.Run(async () =>
        {
            await ReadPid();
            await Task.Delay(delayMs);
            var peer = new FakeMpvPeer(Socket);
            lock (_disposables) _disposables.Add(peer);
        });

    private sealed class ScriptedReader : ISelectionReader
    {
        public Func<int, ReaderObservation> Script { get; set; } = _ => ReaderObservation.Absent;
        public int Polls;
        public ReaderObservation Poll() => Script(Interlocked.Increment(ref Polls));
    }

    private sealed class FakeHealth : IReaderHealth
    {
        public ReaderHealthResult Check() => new(true, "");
    }

    private sealed class FakeApplier(Func<CancellationToken, Task>? prepare = null, Func<CancellationToken, Task>? commit = null)
        : IAlbumApplier
    {
        public async Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken)
        {
            if (prepare is not null) await prepare(cancellationToken);
            return new Prepared(commit);
        }

        private sealed class Prepared(Func<CancellationToken, Task>? commit) : IPreparedAlbum
        {
            public Task CommitAsync(CancellationToken cancellationToken) =>
                commit?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private static ReaderObservation Tag() =>
        ReaderObservation.Present("04AABBCC", () => new TagReadResult(TagReadKind.Release, ReleaseId.From(Guid.Parse(Release))));

    private static readonly SupervisorPolicy QuickPolicy = new()
    {
        FaultWindow = TimeSpan.FromMilliseconds(300),
        FaultThreshold = 3,
        HealthInterval = TimeSpan.FromMinutes(5),
    };

    private Task Run(
        LuciferOptions options,
        ISelectionReader reader,
        IAlbumApplier applier,
        CancellationToken stop,
        SupervisorPolicy? policy = null) =>
        PlaySession.RunAsync(
            options, reader, new FakeHealth(), _ => applier, policy ?? QuickPolicy,
            TextWriter.Null, TextWriter.Null, stop, FastIpc);

    // ---- stop during startup, preparation, IPC ----------------------------

    [Fact]
    public async Task Stop_during_child_startup_exits_by_the_deadline_leaving_no_orphan_or_socket()
    {
        var options = Options(ChildScript());
        using var stop = new CancellationTokenSource();

        var run = Run(options, new ScriptedReader(), new FakeApplier(), stop.Token);
        var pid = await ReadPid();
        Assert.True(Alive(pid));

        var clock = Stopwatch.StartNew();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(clock.Elapsed < options.ShutdownTimeout, $"took {clock.Elapsed}");
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Stop_during_delayed_http_preparation_exits_by_the_deadline_and_cleans_up()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var preparing = new TaskCompletionSource();
        var applier = new FakeApplier(prepare: async ct =>
        {
            preparing.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct); // an HTTP request that never answers, but honors cancellation
        });
        var reader = new ScriptedReader { Script = _ => Tag() };

        var run = Run(options, reader, applier, stop.Token);
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pid = await ReadPid();
        Assert.True(File.Exists(Socket));

        var clock = Stopwatch.StartNew();
        stop.Cancel();
        await run; // graceful stop is not an error

        Assert.True(clock.Elapsed < options.ShutdownTimeout, $"took {clock.Elapsed}");
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Stop_while_mpv_never_answers_an_ipc_command_exits_by_the_deadline_and_cleans_up()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var loading = new TaskCompletionSource();
        // Real mpv wrapper does the IPC: the applier commits through a real Mpv connection.
        var reader = new ScriptedReader { Script = _ => Tag() };

        var run = PlaySession.RunAsync(
            options, reader, new FakeHealth(),
            mpv =>
            {
                return new FakeApplier(commit: async ct =>
                {
                    loading.TrySetResult();
                    await mpv.LoadAlbumAsync(["http://stream.example.test/1"], ct);
                });
            },
            QuickPolicy, TextWriter.Null, TextWriter.Null, stop.Token, FastIpc);

        // Wait for the peer, then make it silent for loadfile.
        await ReadPid();
        var p = await WaitForPeer();
        p.Handler = (req, conn) => Task.FromResult(req.Name == "loadfile");
        await loading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pid = await ReadPid();

        var clock = Stopwatch.StartNew();
        stop.Cancel();
        await run;

        Assert.True(clock.Elapsed < options.ShutdownTimeout, $"took {clock.Elapsed}");
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    private async Task<FakeMpvPeer> WaitForPeer()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            var peer = _disposables.OfType<FakeMpvPeer>().FirstOrDefault();
            if (peer is not null) return peer;
            await Task.Delay(20);
        }
        throw new TimeoutException("no peer");
    }

    [Fact]
    public async Task Pending_work_that_ignores_cancellation_is_abandoned_at_the_deadline_with_a_failure()
    {
        var options = Options(ChildScript(), shutdownSeconds: 1);
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var preparing = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var applier = new FakeApplier(prepare: async _ =>
        {
            preparing.TrySetResult();
            await release.Task; // ignores the token entirely
        });

        var run = Run(options, new ScriptedReader { Script = _ => Tag() }, applier, stop.Token);
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pid = await ReadPid();

        var clock = Stopwatch.StartNew();
        stop.Cancel();
        // Bounded: abandoned work does not hold the process, and owned resources are still released.
        try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { }
        catch (ShutdownTimeoutException) { }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"took {clock.Elapsed}");
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
        release.SetResult();
    }

    // ---- fatal policy ------------------------------------------------------

    [Fact]
    public async Task Essential_mpv_death_after_startup_is_a_fatal_failure()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var run = Run(options, new ScriptedReader(), new FakeApplier(), stop.Token);
        var pid = await ReadPid();
        await WaitForPeer();
        await Task.Delay(500); // let startup finish

        Process.GetProcessById(pid).Kill();

        var ex = await Assert.ThrowsAsync<FatalRunException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("mpv", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Unrecoverable_reader_faults_are_a_fatal_failure_and_release_mpv()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var reader = new ScriptedReader { Script = _ => ReaderObservation.Fault("serial port closed") };

        var run = Run(options, reader, new FakeApplier(), stop.Token);
        var ex = await Assert.ThrowsAsync<FatalRunException>(() => run.WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.Contains("serial port closed", ex.Message);
        var pid = await ReadPid();
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task A_transient_fault_burst_does_not_end_the_session()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var reader = new ScriptedReader
        {
            Script = n => n is >= 3 and <= 4 ? ReaderObservation.Fault("blip") : ReaderObservation.Absent,
        };

        var run = Run(options, reader, new FakeApplier(), stop.Token);
        await Task.Delay(1500);

        Assert.False(run.IsCompleted);
        stop.Cancel();
        await run;
    }

    [Fact]
    public async Task Idle_with_no_tag_neither_exits_nor_restarts_anything()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var reader = new ScriptedReader();

        var run = Run(options, reader, new FakeApplier(), stop.Token);
        var pid = await ReadPid();
        await Task.Delay(2000);

        Assert.False(run.IsCompleted);
        Assert.True(reader.Polls > 5);
        Assert.True(Alive(pid));
        Assert.Equal(pid, await ReadPid()); // no second launch rewrote it
        stop.Cancel();
        await run;
        Assert.False(Alive(pid));
    }

    [Fact]
    public async Task A_graceful_stop_returns_normally_after_clean_shutdown()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var run = Run(options, new ScriptedReader(), new FakeApplier(), stop.Token);
        var pid = await ReadPid();
        await WaitForPeer();
        await Task.Delay(500);

        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    // ---- review fixes: child output and stop-time mpv exit ------------------------------------

    private static string FdTarget(int pid, int fd) =>
        new FileInfo($"/proc/{pid}/fd/{fd}").LinkTarget ?? "";

    [Fact]
    public async Task Mpv_child_output_is_not_forwarded_to_the_applications_own_streams()
    {
        // A child that prints a stream URL with credentials, as mpv's "Playing:" line would.
        var script = Script(
            $"echo $$ > {PidFile}\n" +
            "echo 'Playing: https://music.example.test/rest/stream.view?u=u&t=TOKEN&s=SALT'\n" +
            "echo 'Failed: https://music.example.test/rest/stream.view?u=u&t=TOKEN&s=SALT' >&2\n" +
            "exec sleep 60");
        ServeSocketLater();
        using var mpv = await Mpv.StartAsync(script, Socket, "alsa/x", TimeSpan.FromSeconds(30), CancellationToken.None, FastIpc);
        var pid = await ReadPid();

        var ownOut = new FileInfo("/proc/self/fd/1").LinkTarget;
        var ownErr = new FileInfo("/proc/self/fd/2").LinkTarget;
        Assert.NotEqual(ownOut, FdTarget(pid, 1));
        Assert.NotEqual(ownErr, FdTarget(pid, 2));
        Assert.StartsWith("pipe:", FdTarget(pid, 1));
        Assert.StartsWith("pipe:", FdTarget(pid, 2));
    }

    [Fact]
    public async Task A_chatty_mpv_child_is_drained_and_never_blocks_on_its_output()
    {
        var done = Path.Combine(_dir, "done");
        var script = Script(
            $"echo $$ > {PidFile}\n" +
            "head -c 3000000 /dev/zero | tr '\\0' 'x'\n" +
            "head -c 3000000 /dev/zero | tr '\\0' 'y' >&2\n" +
            $"touch {done}\n" +
            "exec sleep 60");
        ServeSocketLater();
        using var mpv = await Mpv.StartAsync(script, Socket, "alsa/x", TimeSpan.FromSeconds(30), CancellationToken.None, FastIpc);

        var sw = Stopwatch.StartNew();
        while (!File.Exists(done) && sw.ElapsedMilliseconds < 10000) await Task.Delay(20);
        Assert.True(File.Exists(done), "child blocked writing to a pipe nobody reads");
    }

    [Fact]
    public async Task Mpv_exiting_after_a_stop_was_requested_is_a_clean_exit_not_a_fatal_failure()
    {
        // KillMode=mixed leaves mpv to the app, but a SIGTERM that reaches mpv first must still
        // not turn an ordinary stop into exit 1. The poll below makes the order certain: mpv is
        // dead and the player has had time to notice before the stop is requested. (The same death
        // with no stop is a fatal failure: Essential_mpv_death_after_startup_is_a_fatal_failure.)
        for (var round = 0; round < 3; round++)
        {
            var options = Options(ChildScript());
            ServeSocketLater();
            using var stop = new CancellationTokenSource();
            var killed = false;
            var reader = new ScriptedReader
            {
                Script = _ =>
                {
                    if (killed) return ReaderObservation.Absent;
                    killed = true;
                    var child = Process.GetProcessById(int.Parse(File.ReadAllText(PidFile).Trim()));
                    child.Kill();
                    child.WaitForExit();
                    Thread.Sleep(500); // the IPC connection closes and the player records the loss
                    stop.Cancel();
                    return ReaderObservation.Absent;
                },
            };

            var run = Run(options, reader, new FakeApplier(), stop.Token);
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(killed, "mpv was killed before the stop was requested");
            Assert.Equal(TaskStatus.RanToCompletion, run.Status); // not a FatalRunException
            Assert.False(File.Exists(Socket));
            Assert.False(Alive(await ReadPid()));
            foreach (var d in Enumerable.Reverse(_disposables)) d.Dispose();
            _disposables.Clear();
            File.Delete(PidFile);
        }
    }

    // ---- LUC-09: transport controls in service mode -------------------------------------------

    private static ControlsSetup Controls(string device, bool required = false, int code = 300) => new(
        InputMapping.Parse($$"""{ "keys": [ { "code": {{code}}, "action": "toggle-pause" } ] }""").Mapping!,
        () => EvdevInputDevice.Open(device),
        required,
        TimeSpan.Zero);

    [Fact]
    public async Task Controls_run_inside_the_play_session_and_reach_the_player_through_the_device()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        var fifo = Path.Combine(_dir, "event0");
        using (var mk = Process.Start("mkfifo", fifo)!) await mk.WaitForExitAsync();
        using var device = new FileStream(fifo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1);
        using var stop = new CancellationTokenSource();
        var captured = new StringWriter();
        var output = TextWriter.Synchronized(captured);
        var loaded = new TaskCompletionSource();

        var run = PlaySession.RunAsync(
            options, new ScriptedReader { Script = _ => Tag() }, new FakeHealth(),
            mpv => new FakeApplier(commit: async ct =>
            {
                await mpv.LoadAlbumAsync(["http://stream.example.test/1", "http://stream.example.test/2"], ct);
                loaded.TrySetResult();
            }),
            QuickPolicy, output, TextWriter.Null, stop.Token, FastIpc, Controls(fifo));

        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var peer = await WaitForPeer();
        await peer.Last!.EventAsync("start-file", new { playlist_entry_id = peer.NextEntryId - 1 });
        await peer.Last!.EventAsync("file-loaded");
        await Task.Delay(200);

        await device.WriteAsync(AdapterEncode(300, 1));
        await device.FlushAsync();

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000 && !peer.Requests.Any(r => r.Name == "set_property" && r.Command[2].GetBoolean()))
            await Task.Delay(20);
        Assert.Contains(peer.Requests, r => r.Name == "set_property" && r.Command[1].GetString() == "pause" && r.Command[2].GetBoolean());
        Assert.Contains("Control toggle-pause: applied", Snapshot(captured));

        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static string Snapshot(StringWriter w)
    {
        lock (w) return w.ToString();
    }

    private static byte[] AdapterEncode(ushort code, int value) =>
        InputAdapterTests.Encode(new InputEvent(InputEvent.Key, code, value), InputEventFormat.Native);

    [Fact]
    public async Task A_missing_input_device_is_a_logged_degradation_and_albums_still_play()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();
        var captured = new StringWriter();
        var output = TextWriter.Synchronized(captured);

        var run = PlaySession.RunAsync(
            options, new ScriptedReader(), new FakeHealth(), _ => new FakeApplier(), QuickPolicy,
            output, TextWriter.Null, stop.Token, FastIpc, Controls(Path.Combine(_dir, "no-such-event")));
        var pid = await ReadPid();
        await WaitForPeer();
        await Task.Delay(500);

        Assert.False(run.IsCompleted);
        Assert.Contains("controls disabled", Snapshot(captured));
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(Alive(pid));
    }

    [Fact]
    public async Task A_missing_input_device_fails_startup_when_controls_are_required_and_cleans_up()
    {
        var options = Options(ChildScript());
        ServeSocketLater();
        using var stop = new CancellationTokenSource();

        var run = PlaySession.RunAsync(
            options, new ScriptedReader(), new FakeHealth(), _ => new FakeApplier(), QuickPolicy,
            TextWriter.Null, TextWriter.Null, stop.Token, FastIpc, Controls(Path.Combine(_dir, "no-such-event"), required: true));

        var ex = await Assert.ThrowsAsync<FatalRunException>(() => run);
        Assert.Contains("no-such-event", ex.Message);
        var pid = await ReadPid();
        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Losing_a_required_input_device_after_startup_is_fatal_but_an_optional_one_is_not()
    {
        foreach (var required in new[] { true, false })
        {
            var path = Path.Combine(_dir, $"ended-{required}");
            File.WriteAllBytes(path, []); // opens fine, then ends at once: the node vanished
            var options = Options(ChildScript());
            ServeSocketLater();
            using var stop = new CancellationTokenSource();

            var run = PlaySession.RunAsync(
                options, new ScriptedReader(), new FakeHealth(), _ => new FakeApplier(), QuickPolicy,
                TextWriter.Null, TextWriter.Null, stop.Token, FastIpc, Controls(path, required));

            if (required)
            {
                await Assert.ThrowsAsync<FatalRunException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                await Task.Delay(800);
                Assert.False(run.IsCompleted);
                stop.Cancel();
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }

            foreach (var d in _disposables.OfType<FakeMpvPeer>().ToList()) { d.Dispose(); _disposables.Remove(d); }
            File.Delete(PidFile);
        }
    }
}
