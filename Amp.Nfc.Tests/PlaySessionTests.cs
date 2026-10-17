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
}
