using System.Diagnostics;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

// LUC-09: transport policy in PlaybackOwner/Mpv and the input path (a FIFO feeding struct
// input_event records -> InputControls -> PlaybackOwner -> Mpv) against an in-process fake mpv peer
// that keeps a playlist, a playlist position, a playback time and a pause flag.
// This proves the application's policy, ordering and repeat handling. It does NOT prove that real
// mpv answers playlist-pos / playlist-count / time-pos, seek, stop and playlist-next at the ends as
// assumed here, nor that any physical controller sends the codes used below (LUC-08, LUC-12).
public class TransportControlTests : IDisposable
{
    private static readonly MpvTimeouts Fast = new(
        Connect: TimeSpan.FromMilliseconds(500),
        Send: TimeSpan.FromMilliseconds(500),
        Reply: TimeSpan.FromMilliseconds(500),
        StreamStart: TimeSpan.FromSeconds(30));

    // Codes used only by these tests; the product has no default codes.
    private const ushort CodeToggle = 300, CodeNext = 301, CodePrevious = 302, CodePlay = 303, CodePause = 304, CodeUnmapped = 399;

    private readonly string _dir = Path.Combine("/tmp", "tc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _disposables = [];

    public TransportControlTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_disposables)) d.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() { lock (this) return _now; }
        public void Advance(TimeSpan span) { lock (this) _now += span; }
    }

    private sealed class Rig
    {
        public required FakeMpvPeer Peer;
        public required Mpv Mpv;
        public required PlaybackOwner Owner;
        public readonly object Gate = new();
        public readonly List<string> Queue = [];
        public int Pos = -1;
        public double Time;
        public bool Paused;
        public bool PositionKnown = true;

        public List<string> Names => Peer.Requests.Select(r => r.Name).ToList();

        public int Count(string name) => Peer.Requests.Count(r => r.Name == name);

        public int PauseCommands => Peer.Requests.Count(r => r.Name == "set_property" && r.Command[1].GetString() == "pause");

        public Task<TransportOutcome> Do(TransportCommand c) => Owner.TransportAsync(c, CancellationToken.None);

        // Entry id of queue item i of the most recent load.
        public long EntryId(int i) => Peer.NextEntryId - Queue.Count + 1 + i;

        public async Task Start()
        {
            await Owner.ApplyAlbumAsync(["https://x/s?id=t1", "https://x/s?id=t2", "https://x/s?id=t3"], CancellationToken.None);
            await Peer.Last!.EventAsync("start-file", new { playlist_entry_id = EntryId(0) });
            await Peer.Last!.EventAsync("file-loaded");
            await WaitFor(MpvStatus.Playing);
        }

        public async Task WaitFor(MpvStatus status) =>
            Assert.Equal(status, await Eventually(() => Mpv.Status, s => s == status));

        public async Task PauseEvent(bool paused)
        {
            lock (Gate) Paused = paused;
            await Peer.Last!.EventAsync("property-change", new { name = "pause", data = paused });
        }
    }

    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> ok, int ms = 3000)
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

    private async Task<Rig> BuildAsync()
    {
        var peer = new FakeMpvPeer(Path.Combine(_dir, "m.sock"));
        _disposables.Add(peer);
        var mpv = await Mpv.ConnectAsync(peer.Path, Fast, TimeSpan.FromSeconds(2), CancellationToken.None);
        _disposables.Add(mpv);
        var rig = new Rig { Peer = peer, Mpv = mpv, Owner = new PlaybackOwner(mpv) };

        peer.Handler = async (req, conn) =>
        {
            switch (req.Name)
            {
                case "loadfile":
                    lock (rig.Gate)
                    {
                        if (req.Command[2].GetString() == "replace") { rig.Queue.Clear(); rig.Pos = 0; rig.Time = 0; }
                        rig.Queue.Add(req.Command[1].GetString()!);
                    }
                    return false;
                case "stop":
                    lock (rig.Gate) { rig.Queue.Clear(); rig.Pos = -1; }
                    return false;
                case "playlist-next":
                    lock (rig.Gate) { rig.Pos++; rig.Time = 0; }
                    return false;
                case "playlist-prev":
                    lock (rig.Gate) { rig.Pos--; rig.Time = 0; }
                    return false;
                case "seek":
                    lock (rig.Gate) rig.Time = 0;
                    return false;
                case "get_property":
                    var name = req.Command[1].GetString();
                    object? data;
                    lock (rig.Gate)
                        data = !rig.PositionKnown ? null : name switch
                        {
                            "playlist-pos" => rig.Pos,
                            "playlist-count" => rig.Queue.Count,
                            "time-pos" => rig.Time,
                            _ => null,
                        };
                    await conn.ReplyAsync(req.Id, data: data);
                    return true;
                case "set_property" when req.Command[1].GetString() == "pause":
                    var paused = req.Command[2].GetBoolean();
                    bool changed;
                    lock (rig.Gate) { changed = rig.Paused != paused; rig.Paused = paused; }
                    await conn.ReplyAsync(req.Id);
                    if (changed)
                        await conn.EventAsync("property-change", new { name = "pause", data = paused });
                    return true;
            }
            return false;
        };
        return rig;
    }

    private static void SetPlace(Rig rig, int pos, double time)
    {
        lock (rig.Gate) { rig.Pos = pos; rig.Time = time; }
    }

    // ---- play / pause ---------------------------------------------------------------------------

    [Fact]
    public async Task Toggle_pauses_the_current_track_and_resumes_it()
    {
        var rig = await BuildAsync();
        await rig.Start();

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.TogglePause));
        await rig.WaitFor(MpvStatus.Paused);
        Assert.True(rig.Paused);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.TogglePause));
        await rig.WaitFor(MpvStatus.Playing);
        Assert.False(rig.Paused);
        Assert.Equal(0, rig.Count("playlist-next") + rig.Count("playlist-prev") + rig.Count("seek"));
    }

    [Fact]
    public async Task Explicit_play_and_pause_apply_only_when_they_change_something()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var before = rig.PauseCommands;

        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Play)); // already playing
        Assert.Equal(before, rig.PauseCommands);
        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Pause));
        await rig.WaitFor(MpvStatus.Paused);
        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Play));
        await rig.WaitFor(MpvStatus.Playing);
    }

    // ---- next -------------------------------------------------------------------------------------

    [Fact]
    public async Task Next_advances_to_the_following_track()
    {
        var rig = await BuildAsync();
        await rig.Start();

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Next));

        Assert.Equal(1, rig.Count("playlist-next"));
        Assert.Equal(0, rig.Count("stop"));
        Assert.Equal(1, rig.Pos);
    }

    [Fact]
    public async Task Next_on_the_last_track_stops_at_the_album_end_and_does_not_wrap()
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 2, time: 40);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Next));

        await rig.WaitFor(MpvStatus.Ended);
        Assert.Equal(0, rig.Count("playlist-next"));
        Assert.Equal(1, rig.Count("stop"));
        // mpv reports the end of the abandoned last entry; the album stays ended.
        await rig.Peer.Last!.EventAsync("end-file", new { reason = "stop", playlist_entry_id = rig.Peer.NextEntryId });
        await Task.Delay(100);
        Assert.Equal(MpvStatus.Ended, rig.Mpv.Status);
        // Nothing more to advance to.
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Next));
        Assert.Equal(1, rig.Count("stop"));
    }

    // ---- previous ---------------------------------------------------------------------------------

    [Fact]
    public async Task Previous_after_more_than_three_seconds_restarts_the_current_track()
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 1, time: 3.5);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Previous));

        Assert.Equal(1, rig.Count("seek"));
        Assert.Equal(0, rig.Count("playlist-prev"));
        Assert.Equal(1, rig.Pos);
        var seek = rig.Peer.Requests.Single(r => r.Name == "seek");
        Assert.Equal(0, seek.Command[1].GetDouble());
        Assert.Equal("absolute", seek.Command[2].GetString());
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(2.9)]
    [InlineData(0.0)]
    public async Task Previous_within_three_seconds_selects_the_previous_track(double played)
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 1, time: played);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Previous));

        Assert.Equal(1, rig.Count("playlist-prev"));
        Assert.Equal(0, rig.Count("seek"));
        Assert.Equal(0, rig.Pos);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(60.0)]
    public async Task Previous_on_the_first_track_restarts_it_and_never_leaves_the_album(double played)
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 0, time: played);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Previous));

        Assert.Equal(1, rig.Count("seek"));
        Assert.Equal(0, rig.Count("playlist-prev"));
        Assert.Equal(0, rig.Pos);
    }

    [Fact]
    public async Task Unreadable_position_falls_back_to_mpvs_own_next_and_previous()
    {
        var rig = await BuildAsync();
        await rig.Start();
        rig.PositionKnown = false;

        await rig.Do(TransportCommand.Next);
        await rig.Do(TransportCommand.Previous);

        Assert.Equal(1, rig.Count("playlist-next"));
        Assert.Equal(1, rig.Count("playlist-prev"));
    }

    // ---- paused ---------------------------------------------------------------------------------

    [Fact]
    public async Task Next_and_previous_while_paused_keep_it_paused_and_send_no_pause_command()
    {
        var rig = await BuildAsync();
        await rig.Start();
        await rig.Do(TransportCommand.Pause);
        await rig.WaitFor(MpvStatus.Paused);
        var pauseCommands = rig.PauseCommands;
        SetPlace(rig, pos: 1, time: 1);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Next));
        SetPlace(rig, pos: 1, time: 1);
        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Previous));
        SetPlace(rig, pos: 1, time: 10);
        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Previous));

        Assert.Equal(pauseCommands, rig.PauseCommands);
        Assert.True(rig.Paused);
        Assert.Equal(MpvStatus.Paused, rig.Mpv.Status);
    }

    // ---- loading ----------------------------------------------------------------------------------

    [Fact]
    public async Task During_loading_play_and_pause_change_the_pause_preference_and_the_track_is_then_paused_or_playing_accordingly()
    {
        var rig = await BuildAsync();
        await rig.Owner.ApplyAlbumAsync(["https://x/s?id=t1", "https://x/s?id=t2"], CancellationToken.None);
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Pause));
        Assert.True(rig.Paused);
        // A second pause changes nothing.
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Pause));
        await rig.Peer.Last!.EventAsync("start-file", new { playlist_entry_id = rig.EntryId(0) });
        await rig.Peer.Last!.EventAsync("file-loaded");
        await rig.WaitFor(MpvStatus.Paused); // the preference survived the load

        await rig.Do(TransportCommand.Play);
        await rig.WaitFor(MpvStatus.Playing);
    }

    [Fact]
    public async Task During_loading_toggle_flips_the_preference_and_next_previous_are_ignored()
    {
        var rig = await BuildAsync();
        await rig.Owner.ApplyAlbumAsync(["https://x/s?id=t1", "https://x/s?id=t2"], CancellationToken.None);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.TogglePause));
        Assert.True(rig.Paused);
        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.TogglePause));
        Assert.False(rig.Paused);
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Next));
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Previous));
        Assert.Equal(0, rig.Count("playlist-next") + rig.Count("playlist-prev") + rig.Count("seek"));
    }

    [Fact]
    public async Task A_new_album_starts_unpaused_but_a_later_explicit_pause_overrides_it()
    {
        var rig = await BuildAsync();
        await rig.Start();
        await rig.Do(TransportCommand.Pause);
        await rig.WaitFor(MpvStatus.Paused);

        await rig.Owner.ApplyAlbumAsync(["https://x/s?id=n1", "https://x/s?id=n2"], CancellationToken.None);
        Assert.False(rig.Paused);
        await rig.Peer.Last!.EventAsync("start-file", new { playlist_entry_id = rig.EntryId(0) });
        await rig.Peer.Last!.EventAsync("file-loaded");
        await rig.WaitFor(MpvStatus.Playing);

        await rig.Do(TransportCommand.Pause);
        await rig.WaitFor(MpvStatus.Paused);
        Assert.True(rig.Paused);
    }

    [Fact]
    public async Task A_control_pressed_during_replacement_runs_after_the_replacement_has_finished()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var inner = rig.Peer.Handler!;
        var midway = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        rig.Peer.Handler = async (req, conn) =>
        {
            if (req.Name == "loadfile" && req.Command[1].GetString()!.EndsWith("n2"))
            {
                midway.TrySetResult();
                await go.Task;
            }
            return await inner(req, conn);
        };

        var replace = rig.Owner.ApplyAlbumAsync(["https://x/s?id=n1", "https://x/s?id=n2"], CancellationToken.None);
        await midway.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var control = rig.Do(TransportCommand.TogglePause);
        await Task.Delay(100);
        Assert.False(control.IsCompleted);
        go.SetResult();
        await replace;
        Assert.Equal(TransportOutcome.Applied, await control);

        var names = rig.Names;
        Assert.True(names.FindLastIndex(n => n == "loadfile") < names.FindLastIndex(n => n == "set_property"));
        Assert.True(rig.Paused); // acted on the new album, which was loading unpaused
        Assert.Equal(1, rig.Peer.MaxInFlight);
    }

    // ---- ended ------------------------------------------------------------------------------------

    private static async Task EndAlbum(Rig rig)
    {
        await rig.Peer.Last!.EventAsync("end-file", new { reason = "eof", playlist_entry_id = rig.Peer.NextEntryId });
        await rig.WaitFor(MpvStatus.Ended);
    }

    [Fact]
    public async Task Play_after_the_album_ended_restarts_the_selected_album_from_its_first_track_unpaused()
    {
        var rig = await BuildAsync();
        await rig.Start();
        await EndAlbum(rig);
        var loadsBefore = rig.Count("loadfile");

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Play));

        var loads = rig.Peer.Requests.Where(r => r.Name == "loadfile").Skip(loadsBefore).ToList();
        Assert.Equal(["https://x/s?id=t1", "https://x/s?id=t2", "https://x/s?id=t3"], loads.Select(r => r.Command[1].GetString()));
        Assert.Equal(["replace", "append", "append"], loads.Select(r => r.Command[2].GetString()));
        Assert.False(rig.Paused);
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);
    }

    [Fact]
    public async Task Toggle_after_the_album_ended_also_restarts_it_but_pause_next_and_previous_are_ignored()
    {
        var rig = await BuildAsync();
        await rig.Start();
        await EndAlbum(rig);
        var before = rig.Peer.Requests.Count;

        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Pause));
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Next));
        Assert.Equal(TransportOutcome.Ignored, await rig.Do(TransportCommand.Previous));
        Assert.Equal(before, rig.Peer.Requests.Count);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.TogglePause));
        Assert.Equal(3, rig.Queue.Count);
    }

    [Fact]
    public async Task Restarting_after_next_on_the_last_track_replays_the_same_album()
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 2, time: 5);
        await rig.Do(TransportCommand.Next);
        await rig.WaitFor(MpvStatus.Ended);

        Assert.Equal(TransportOutcome.Applied, await rig.Do(TransportCommand.Play));

        Assert.Equal(["https://x/s?id=t1", "https://x/s?id=t2", "https://x/s?id=t3"], rig.Queue);
    }

    // ---- no album ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(TransportCommand.Play)]
    [InlineData(TransportCommand.Pause)]
    [InlineData(TransportCommand.TogglePause)]
    [InlineData(TransportCommand.Next)]
    [InlineData(TransportCommand.Previous)]
    public async Task With_no_applied_album_every_control_does_nothing(TransportCommand command)
    {
        var rig = await BuildAsync();
        var before = rig.Peer.Requests.Count;

        Assert.Equal(TransportOutcome.Ignored, await rig.Do(command));

        Assert.Equal(before, rig.Peer.Requests.Count);
    }

    // ---- input path -------------------------------------------------------------------------------

    private sealed class InputRig(Rig rig, FakeClock clock, FileStream writer, InputControls controls, List<string> log)
    {
        public Rig Rig => rig;
        public FakeClock Clock => clock;
        public InputControls Controls => controls;
        public List<string> Log => log;

        public async Task Send(ushort type, ushort code, int value)
        {
            var bytes = InputAdapterTests.Encode(new InputEvent(type, code, value), InputEventFormat.Native);
            await writer.WriteAsync(bytes);
            await writer.FlushAsync();
        }

        public Task Press(ushort code) => Send(InputEvent.Key, code, InputEvent.Press);
        public Task Release(ushort code) => Send(InputEvent.Key, code, InputEvent.Release);
        public Task Repeat(ushort code) => Send(InputEvent.Key, code, InputEvent.Repeat);
        public Task Tap(ushort code) => Press(code).ContinueWith(_ => Release(code)).Unwrap();
    }

    private async Task<InputRig> BuildInputAsync(Rig rig, TimeSpan? debounce = null)
    {
        var fifo = Path.Combine(_dir, "event0");
        using (var mk = Process.Start("mkfifo", fifo)!) await mk.WaitForExitAsync();
        // Read-write open: the "device" stays open without blocking on a writer, like a real node.
        var writer = new FileStream(fifo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1);
        _disposables.Add(writer);

        var mapping = InputMapping.Parse($$"""
            { "keys": [
              { "code": {{CodeToggle}}, "action": "toggle-pause" }, { "code": {{CodeNext}}, "action": "next" },
              { "code": {{CodePrevious}}, "action": "previous" }, { "code": {{CodePlay}}, "action": "play" },
              { "code": {{CodePause}}, "action": "pause" } ] }
            """).Mapping!;

        var clock = new FakeClock();
        var log = new List<string>();
        var controls = new InputControls(rig.Owner, mapping, EvdevInputDevice.Open(fifo), debounce ?? TimeSpan.FromMilliseconds(250), clock,
            line => { lock (log) log.Add(line); });
        _disposables.Add(controls);
        controls.Start(CancellationToken.None);
        return new InputRig(rig, clock, writer, controls, log);
    }

    [Fact]
    public async Task A_mapped_press_reaches_the_player_through_the_device_adapter()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig);

        await input.Tap(CodeToggle);

        await rig.WaitFor(MpvStatus.Paused);
        Assert.True(rig.Paused);
        input.Clock.Advance(TimeSpan.FromSeconds(1));
        await input.Tap(CodeToggle);
        await rig.WaitFor(MpvStatus.Playing);
    }

    [Fact]
    public async Task Release_unmapped_keys_and_non_key_events_do_nothing()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig);
        var before = rig.Peer.Requests.Count;

        await input.Release(CodeNext);
        await input.Tap(CodeUnmapped);
        await input.Send(0, 0, 0);                       // EV_SYN
        await input.Send(4, CodeNext, 1);                // EV_MSC with a mapped number
        await input.Send(InputEvent.Key, CodeNext, 7);   // a value that is not press/release/repeat
        await Task.Delay(300);

        Assert.Equal(before, rig.Peer.Requests.Count);
    }

    [Fact]
    public async Task Auto_repeat_never_advances_tracks_or_toggles_pause()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig);

        await input.Press(CodeNext);
        await input.Repeat(CodeNext);
        await input.Repeat(CodeNext);
        await input.Repeat(CodeNext);
        await input.Release(CodeNext);
        await Eventually(() => rig.Count("playlist-next"), c => c >= 1);
        await Task.Delay(300);

        Assert.Equal(1, rig.Count("playlist-next"));

        input.Clock.Advance(TimeSpan.FromSeconds(1));
        var baseline = rig.PauseCommands; // the album load itself sent an unpause
        await input.Press(CodeToggle);
        await input.Repeat(CodeToggle);
        await input.Repeat(CodeToggle);
        await Eventually(() => rig.PauseCommands, c => c > baseline);
        await Task.Delay(300);

        Assert.Equal(baseline + 1, rig.PauseCommands);
        Assert.True(rig.Paused);
    }

    [Fact]
    public async Task Presses_of_one_key_inside_the_debounce_window_collapse_to_one_and_later_presses_count()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig, debounce: TimeSpan.FromMilliseconds(250));

        await input.Press(CodeNext);
        input.Clock.Advance(TimeSpan.FromMilliseconds(100));
        await input.Press(CodeNext); // contact bounce
        await Eventually(() => rig.Count("playlist-next"), c => c >= 1);
        await Task.Delay(300);
        Assert.Equal(1, rig.Count("playlist-next"));

        input.Clock.Advance(TimeSpan.FromMilliseconds(300));
        await input.Press(CodeNext);
        await Eventually(() => rig.Count("playlist-next"), c => c >= 2);
        Assert.Equal(2, rig.Count("playlist-next"));
    }

    [Fact]
    public async Task Different_keys_are_not_debounced_against_each_other()
    {
        var rig = await BuildAsync();
        await rig.Start();
        SetPlace(rig, pos: 1, time: 1);
        var input = await BuildInputAsync(rig);

        await input.Press(CodeNext);
        await Eventually(() => rig.Count("playlist-next"), c => c >= 1);
        SetPlace(rig, pos: 1, time: 1);
        await input.Press(CodePrevious);
        await Eventually(() => rig.Count("playlist-prev"), c => c >= 1);

        Assert.Equal(1, rig.Count("playlist-next"));
        Assert.Equal(1, rig.Count("playlist-prev"));
    }

    [Fact]
    public async Task Presses_are_applied_in_the_order_they_arrive()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig, debounce: TimeSpan.Zero);

        var baseline = rig.PauseCommands;
        await input.Press(CodePause);
        await input.Press(CodePlay);
        await input.Press(CodePause);
        await Eventually(() => rig.PauseCommands, c => c >= baseline + 3);

        var pauses = rig.Peer.Requests.Where(r => r.Name == "set_property").Skip(baseline).Select(r => r.Command[2].GetBoolean()).ToList();
        Assert.Equal([true, false, true], pauses);
        await rig.WaitFor(MpvStatus.Paused);
    }

    [Fact]
    public async Task A_press_during_album_replacement_waits_and_then_applies_to_the_new_album()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var input = await BuildInputAsync(rig);
        var inner = rig.Peer.Handler!;
        var midway = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        rig.Peer.Handler = async (req, conn) =>
        {
            if (req.Name == "loadfile" && req.Command[1].GetString()!.EndsWith("n2"))
            {
                midway.TrySetResult();
                await go.Task;
            }
            return await inner(req, conn);
        };

        var replace = rig.Owner.ApplyAlbumAsync(["https://x/s?id=n1", "https://x/s?id=n2"], CancellationToken.None);
        await midway.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await input.Press(CodePause);
        await Task.Delay(150);
        Assert.Equal(0, rig.Peer.Requests.Count(r => r.Name == "set_property" && r.Command[2].GetBoolean()));
        go.SetResult();
        await replace;

        await Eventually(() => rig.Paused, p => p);
        var names = rig.Names;
        Assert.True(names.FindLastIndex(n => n == "loadfile") < names.FindLastIndex(n => n == "set_property"));
    }

    [Fact]
    public async Task A_control_pressed_with_no_album_applied_is_logged_as_ignored_and_changes_nothing()
    {
        var rig = await BuildAsync();
        var input = await BuildInputAsync(rig);
        var before = rig.Peer.Requests.Count;

        await input.Press(CodeToggle);
        await Eventually(() => input.Log.Count, c => c >= 1);

        Assert.Equal(before, rig.Peer.Requests.Count);
        Assert.Contains(input.Log, l => l.Contains("toggle-pause") && l.Contains("ignored"));
    }

    [Fact]
    public async Task The_end_of_the_device_stream_is_reported_as_the_device_being_lost()
    {
        var rig = await BuildAsync();
        await rig.Start();
        var path = Path.Combine(_dir, "ended");
        File.WriteAllBytes(path, InputAdapterTests.Encode(new InputEvent(InputEvent.Key, CodeToggle, 1), InputEventFormat.Native));
        var mapping = InputMapping.Parse($$"""{ "keys": [ { "code": {{CodeToggle}}, "action": "toggle-pause" } ] }""").Mapping!;
        var log = new List<string>();
        using var controls = new InputControls(rig.Owner, mapping, EvdevInputDevice.Open(path), TimeSpan.Zero, TimeProvider.System, log.Add);
        controls.Start(CancellationToken.None);

        await controls.Completed.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(controls.LostReason);
        await rig.WaitFor(MpvStatus.Paused); // the event before the end was still delivered
    }

    // ---- startup degradation / wiring ---------------------------------------------------------------

    [Fact]
    public void Missing_device_at_startup_is_a_logged_degradation_unless_required()
    {
        var mapping = InputMapping.Parse("""{ "keys": [ { "code": 1, "action": "next" } ] }""").Mapping!;
        var lines = new List<string>();

        var optional = ControlsSetup.TryOpen(
            new ControlsSetup(mapping, () => throw new InputDeviceException("cannot open /dev/input/event9: no such device"), false, TimeSpan.Zero),
            lines.Add);

        Assert.Null(optional);
        Assert.Contains(lines, l => l.Contains("event9") && l.Contains("controls disabled", StringComparison.OrdinalIgnoreCase));

        var required = Assert.Throws<FatalRunException>(() => ControlsSetup.TryOpen(
            new ControlsSetup(mapping, () => throw new InputDeviceException("cannot open /dev/input/event9: no such device"), true, TimeSpan.Zero),
            lines.Add));
        Assert.Contains("event9", required.Message);
    }
}
