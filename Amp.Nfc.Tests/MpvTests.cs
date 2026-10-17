using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace Amp.Nfc.Tests;

public class MpvTests : IDisposable
{
    private static readonly MpvTimeouts Fast = new(
        Connect: TimeSpan.FromMilliseconds(300),
        Send: TimeSpan.FromMilliseconds(300),
        Reply: TimeSpan.FromMilliseconds(300),
        StreamStart: TimeSpan.FromMilliseconds(300));

    private readonly string _dir = Path.Combine("/tmp", "lt-" + Guid.NewGuid().ToString("N")[..8]);
    private string Socket => Path.Combine(_dir, "m.sock");
    private readonly List<IDisposable> _disposables = [];

    public MpvTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_disposables)) d.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private FakeMpvPeer Peer()
    {
        var p = new FakeMpvPeer(Socket);
        _disposables.Add(p);
        return p;
    }

    private async Task<Mpv> Connected(FakeMpvPeer peer, MpvTimeouts? t = null)
    {
        var m = await Mpv.ConnectAsync(peer.Path, t ?? Fast, TimeSpan.FromSeconds(2), CancellationToken.None);
        _disposables.Add(m);
        return m;
    }

    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> ok, int ms = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            var v = read();
            if (ok(v)) return v;
            await Task.Delay(10);
        }
        return read();
    }

    // ---- reply correlation -------------------------------------------------

    [Fact]
    public async Task Event_and_unrelated_reply_before_the_matching_reply_do_not_fail_the_command()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "set_property") return false;
            await conn.EventAsync("tracks-changed");
            await conn.ReplyAsync(req.Id + 1000, error: "property unavailable"); // someone else's reply
            await conn.ReplyAsync(req.Id);
            return true;
        };

        await mpv.PauseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Matching_error_reply_fails_that_command_with_mpvs_message()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "set_property") return false;
            await conn.ReplyAsync(req.Id, error: "property not found");
            return true;
        };

        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));
        Assert.Equal(MpvFailureKind.CommandError, ex.Kind);
        Assert.Contains("property not found", ex.Message);
    }

    [Fact]
    public async Task Every_command_carries_a_distinct_integer_request_id()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.PauseAsync(CancellationToken.None);
        await mpv.ResumeAsync(CancellationToken.None);

        Assert.All(peer.Requests, r => Assert.Equal(JsonValueKind.Number, r.Root.GetProperty("request_id").ValueKind));
        Assert.Equal(peer.Requests.Count, peer.Requests.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_commands_are_serialized()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "set_property") return false;
            await Task.Delay(50);
            await conn.ReplyAsync(req.Id);
            return true;
        };

        await Task.WhenAll(
            mpv.PauseAsync(CancellationToken.None),
            mpv.ResumeAsync(CancellationToken.None),
            mpv.NextAsync(CancellationToken.None));

        Assert.Equal(1, peer.MaxInFlight);
    }

    // ---- bounded waits -----------------------------------------------------

    [Fact]
    public async Task Missing_reply_ends_within_the_reply_deadline()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = (req, _) => Task.FromResult(req.Name == "set_property");

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));

        Assert.Equal(MpvFailureKind.Timeout, ex.Kind);
        Assert.InRange(sw.ElapsedMilliseconds, 250, 1500);
    }

    [Fact]
    public async Task Late_reply_after_a_timeout_does_not_disturb_the_next_command()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        FakeMpvPeer.Connection? held = null;
        long heldId = 0;
        peer.Handler = (req, conn) =>
        {
            if (req.Name != "set_property" || held is not null) return Task.FromResult(false);
            held = conn; heldId = req.Id;
            return Task.FromResult(true);
        };

        await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));
        await held!.ReplyAsync(heldId); // arrives too late
        await mpv.ResumeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Malformed_reply_fails_the_pending_command_promptly()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "set_property") return false;
            await conn.SendLineAsync("this is { not json");
            return true;
        };

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));

        Assert.Equal(MpvFailureKind.Malformed, ex.Kind);
        Assert.True(sw.ElapsedMilliseconds < 250, "should not wait for the reply deadline");
    }

    [Fact]
    public async Task Matching_reply_without_an_error_field_is_malformed()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "set_property") return false;
            await conn.SendLineAsync($"{{\"request_id\":{req.Id}}}");
            return true;
        };

        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));
        Assert.Equal(MpvFailureKind.Malformed, ex.Kind);
    }

    [Fact]
    public async Task Closed_socket_fails_the_pending_command_and_the_player_status()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = (req, conn) =>
        {
            if (req.Name != "set_property") return Task.FromResult(false);
            conn.Close();
            return Task.FromResult(true);
        };

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));

        Assert.Equal(MpvFailureKind.ConnectionClosed, ex.Kind);
        Assert.True(sw.ElapsedMilliseconds < 250);
        Assert.Equal(MpvStatus.Failed, mpv.Status);

        // Later commands fail fast too, they do not hang.
        var later = await Assert.ThrowsAsync<MpvException>(() => mpv.ResumeAsync(CancellationToken.None));
        Assert.Equal(MpvFailureKind.ConnectionClosed, later.Kind);
    }

    [Fact]
    public async Task Connecting_to_a_socket_nobody_serves_ends_at_the_start_deadline()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() =>
            Mpv.ConnectAsync(Socket, Fast, TimeSpan.FromMilliseconds(400), CancellationToken.None));

        Assert.Equal(MpvFailureKind.StartFailed, ex.Kind);
        Assert.InRange(sw.ElapsedMilliseconds, 350, 1500);
    }

    [Fact]
    public async Task A_peer_that_accepts_but_never_answers_the_readiness_request_is_not_ready()
    {
        var peer = Peer();
        peer.Handler = (_, _) => Task.FromResult(true);

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() =>
            Mpv.ConnectAsync(Socket, Fast, TimeSpan.FromMilliseconds(500), CancellationToken.None));

        Assert.Equal(MpvFailureKind.StartFailed, ex.Kind);
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }

    // ---- playback status ---------------------------------------------------

    private static readonly string[] Album = ["http://x/1", "http://x/2"];

    [Fact]
    public async Task Accepted_playlist_command_is_loading_not_playing()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        Assert.Equal(MpvStatus.Idle, mpv.Status);

        await mpv.LoadAlbumAsync(Album, CancellationToken.None);

        Assert.Equal(MpvStatus.Loading, mpv.Status);
        Assert.Equal(["loadfile", "loadfile"], peer.Requests.Where(r => r.Name == "loadfile").Select(r => r.Name));
        Assert.Equal("replace", peer.Requests.First(r => r.Name == "loadfile").Command[2].GetString());
        Assert.Equal("append", peer.Requests.Last(r => r.Name == "loadfile").Command[2].GetString());
    }

    [Fact]
    public async Task Accepted_command_with_no_load_or_terminal_event_becomes_a_load_failure_at_the_stream_deadline()
    {
        var peer = Peer();
        var mpv = await Connected(peer);

        await mpv.LoadAlbumAsync(Album, CancellationToken.None);
        var statuses = new List<MpvStatus>();
        mpv.StatusChanged += statuses.Add;

        var final = await Eventually(() => mpv.Status, s => s == MpvStatus.Failed);

        Assert.Equal(MpvStatus.Failed, final);
        Assert.NotNull(mpv.FailureDetail);
        Assert.DoesNotContain(MpvStatus.Playing, statuses);
    }

    [Fact]
    public async Task Successful_load_before_the_deadline_plays_and_the_deadline_then_does_nothing()
    {
        var peer = Peer();
        var mpv = await Connected(peer);

        await mpv.LoadAlbumAsync(Album, CancellationToken.None);
        await peer.Last!.EventAsync("start-file", new { playlist_entry_id = 1 });
        await peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => mpv.Status, s => s == MpvStatus.Playing));

        await Task.Delay(500); // past the stream-start deadline
        Assert.Equal(MpvStatus.Playing, mpv.Status);
    }

    [Fact]
    public async Task Pause_property_changes_move_between_playing_and_paused()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.LoadAlbumAsync(Album, CancellationToken.None);
        await peer.Last!.EventAsync("file-loaded");
        await Eventually(() => mpv.Status, s => s == MpvStatus.Playing);

        await peer.Last!.EventAsync("property-change", new { name = "pause", data = true });
        Assert.Equal(MpvStatus.Paused, await Eventually(() => mpv.Status, s => s == MpvStatus.Paused));

        await peer.Last!.EventAsync("property-change", new { name = "pause", data = false });
        Assert.Equal(MpvStatus.Playing, await Eventually(() => mpv.Status, s => s == MpvStatus.Playing));
    }

    [Fact]
    public async Task End_of_file_error_is_a_load_failure_with_mpvs_reason()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.LoadAlbumAsync(Album, CancellationToken.None);

        await peer.Last!.EventAsync("end-file", new { reason = "error", playlist_entry_id = 1, file_error = "loading failed" });

        Assert.Equal(MpvStatus.Failed, await Eventually(() => mpv.Status, s => s == MpvStatus.Failed));
        Assert.Contains("loading failed", mpv.FailureDetail);
    }

    [Fact]
    public async Task Eof_of_the_last_track_ends_playback_but_eof_of_an_earlier_track_does_not()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.LoadAlbumAsync(Album, CancellationToken.None); // entries 1 and 2
        await peer.Last!.EventAsync("file-loaded");
        await Eventually(() => mpv.Status, s => s == MpvStatus.Playing);

        await peer.Last!.EventAsync("end-file", new { reason = "eof", playlist_entry_id = 1 });
        await peer.Last!.EventAsync("start-file", new { playlist_entry_id = 2 });
        await peer.Last!.EventAsync("file-loaded");
        await Task.Delay(100);
        Assert.Equal(MpvStatus.Playing, mpv.Status);

        await peer.Last!.EventAsync("end-file", new { reason = "eof", playlist_entry_id = 2 });
        Assert.Equal(MpvStatus.Ended, await Eventually(() => mpv.Status, s => s == MpvStatus.Ended));
    }

    [Fact]
    public async Task Observations_of_a_replaced_selection_are_ignored()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.LoadAlbumAsync(["http://old/1"], CancellationToken.None); // entry 1
        await mpv.LoadAlbumAsync(["http://new/1"], CancellationToken.None); // entry 2

        // mpv reports the replaced entry ending, with an error, after the new load was accepted.
        await peer.Last!.EventAsync("end-file", new { reason = "error", playlist_entry_id = 1, file_error = "old" });
        await peer.Last!.EventAsync("end-file", new { reason = "stop", playlist_entry_id = 1 });
        await Task.Delay(100);

        Assert.Equal(MpvStatus.Loading, mpv.Status);
        await peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => mpv.Status, s => s == MpvStatus.Playing));
    }

    [Fact]
    public async Task Connection_loss_while_playing_is_a_failure()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        await mpv.LoadAlbumAsync(Album, CancellationToken.None);
        await peer.Last!.EventAsync("file-loaded");
        await Eventually(() => mpv.Status, s => s == MpvStatus.Playing);

        peer.Last!.Close();

        Assert.Equal(MpvStatus.Failed, await Eventually(() => mpv.Status, s => s == MpvStatus.Failed));
    }

    [Fact]
    public async Task Rejected_playlist_command_fails_without_loading_state()
    {
        var peer = Peer();
        var mpv = await Connected(peer);
        peer.Handler = async (req, conn) =>
        {
            if (req.Name != "loadfile") return false;
            await conn.ReplyAsync(req.Id, error: "invalid parameter");
            return true;
        };

        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.LoadAlbumAsync(Album, CancellationToken.None));

        Assert.Equal(MpvFailureKind.CommandError, ex.Kind);
        Assert.NotEqual(MpvStatus.Loading, mpv.Status);
        Assert.NotEqual(MpvStatus.Playing, mpv.Status);
    }

    // ---- child ownership ---------------------------------------------------

    private string Script(string name, string body)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static bool Alive(int pid) => Directory.Exists($"/proc/{pid}") &&
        !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z");

    private static async Task<int> ReadPid(string pidFile)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid)) return pid;
            await Task.Delay(20);
        }
        throw new TimeoutException("child never wrote its pid");
    }

    [Fact]
    public async Task Child_that_exits_early_is_reported_promptly_and_leaves_no_socket()
    {
        var mpvPath = Script("early.sh", "exit 3");

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<MpvException>(() =>
            Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(5), CancellationToken.None, Fast));

        Assert.Equal(MpvFailureKind.ProcessExited, ex.Kind);
        Assert.Contains("3", ex.Message);
        Assert.True(sw.ElapsedMilliseconds < 3000, "must not wait for the whole start timeout");
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Child_that_never_becomes_ready_is_killed_at_the_start_deadline()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var mpvPath = Script("hang.sh", $"echo $$ > {pidFile}\nexec sleep 30");

        var ex = await Assert.ThrowsAsync<MpvException>(() =>
            Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromMilliseconds(500), CancellationToken.None, Fast));

        Assert.Equal(MpvFailureKind.StartFailed, ex.Kind);
        Assert.False(Alive(await ReadPid(pidFile)));
    }

    [Fact]
    public async Task Cancelling_startup_stops_the_child_and_removes_its_socket()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var mpvPath = Script("hang.sh", $"echo $$ > {pidFile}\nexec sleep 30");
        using var cts = new CancellationTokenSource();

        var start = Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(10), cts.Token, Fast);
        var pid = await ReadPid(pidFile);
        Assert.True(Alive(pid));
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
    }

    [Fact]
    public async Task Start_adopts_the_child_and_dispose_stops_it_and_removes_only_its_socket()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var mpvPath = Script("ok.sh", $"echo $$ > {pidFile}\nexec sleep 30");

        // "mpv" starts serving its socket a little after it is launched.
        FakeMpvPeer? peer = null;
        var late = Task.Run(async () =>
        {
            await ReadPid(pidFile);
            await Task.Delay(200);
            peer = new FakeMpvPeer(Socket);
        });

        var mpv = await Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(5), CancellationToken.None, Fast);
        await late;
        var pid = await ReadPid(pidFile);
        Assert.True(Alive(pid));
        Assert.True(File.Exists(Socket));

        mpv.Dispose();

        Assert.False(Alive(pid));
        Assert.False(File.Exists(Socket));
        peer!.Dispose();
    }

    [Fact]
    public async Task Child_exit_after_startup_fails_the_player_status()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var mpvPath = Script("ok.sh", $"echo $$ > {pidFile}\nexec sleep 30");
        FakeMpvPeer? peer = null;
        var late = Task.Run(async () =>
        {
            await ReadPid(pidFile);
            await Task.Delay(100);
            peer = new FakeMpvPeer(Socket);
            _disposables.Add(peer);
        });
        var mpv = await Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(5), CancellationToken.None, Fast);
        _disposables.Add(mpv);
        await late;

        Process.GetProcessById(await ReadPid(pidFile)).Kill();

        Assert.Equal(MpvStatus.Failed, await Eventually(() => mpv.Status, s => s == MpvStatus.Failed));
        var ex = await Assert.ThrowsAsync<MpvException>(() => mpv.PauseAsync(CancellationToken.None));
        Assert.Equal(MpvFailureKind.ProcessExited, ex.Kind);
    }

    [Fact]
    public async Task Socket_served_by_another_live_instance_is_neither_taken_nor_deleted()
    {
        var other = Peer();
        var marker = Path.Combine(_dir, "launched");
        var mpvPath = Script("never.sh", $"touch {marker}\nexec sleep 30");

        var ex = await Assert.ThrowsAsync<MpvException>(() =>
            Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(2), CancellationToken.None, Fast));

        Assert.Equal(MpvFailureKind.StartFailed, ex.Kind);
        Assert.True(File.Exists(other.Path));
        Assert.False(File.Exists(marker), "no child should be started against a live socket");
    }

    [Fact]
    public async Task Stale_socket_file_from_a_dead_instance_is_replaced()
    {
        // A process that bound a socket and died leaves the file behind, as a killed mpv would.
        using (var binder = Process.Start(new ProcessStartInfo("python3")
        {
            ArgumentList = { "-I", "-c", "import socket,sys; socket.socket(socket.AF_UNIX).bind(sys.argv[1])", Socket },
        })!)
            binder.WaitForExit();
        Assert.True(File.Exists(Socket));
        var pidFile = Path.Combine(_dir, "pid");
        var mpvPath = Script("ok.sh", $"echo $$ > {pidFile}\nexec sleep 30");
        FakeMpvPeer? peer = null;
        var late = Task.Run(async () =>
        {
            await ReadPid(pidFile);
            Assert.False(File.Exists(Socket), "stale file must be gone before the new mpv serves");
            peer = new FakeMpvPeer(Socket);
            _disposables.Add(peer);
        });

        var mpv = await Mpv.StartAsync(mpvPath, Socket, "null", TimeSpan.FromSeconds(5), CancellationToken.None, Fast);
        _disposables.Add(mpv);
        await late;
    }
}
