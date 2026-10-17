using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

// LUC-07: the selection controller, catalogue file, Navidrome source (stubbed HTTP), applier and the
// real Mpv class together, against an in-process fake mpv IPC peer that keeps a playlist and a
// pause flag. This proves the application's sequencing, generation check, failure policy and state
// derivation at those boundaries. It does NOT prove audible output, real mpv playlist semantics
// (loadfile replace/append, stop, playlist clearing, pause behaviour across loads), stream access
// to a real Navidrome, or the DAC; see the assumptions in the LUC-07 report.
public class AlbumPlaybackTests : IDisposable
{
    private const string ReleaseA = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    private const string ReleaseB = "d5e2d971-e856-4342-ad09-0e6ad1b5241a";
    private const string Unmapped = "11111111-2222-4333-8444-555555555555";

    private static readonly MpvTimeouts Fast = new(
        Connect: TimeSpan.FromMilliseconds(500),
        Send: TimeSpan.FromMilliseconds(500),
        Reply: TimeSpan.FromMilliseconds(300),
        StreamStart: TimeSpan.FromMilliseconds(400));

    private readonly string _dir = Path.Combine("/tmp", "lp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _disposables = [];

    public AlbumPlaybackTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_disposables)) d.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    // ---- rig ----------------------------------------------------------------------------------

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class ScriptedReader : ISelectionReader
    {
        public ReaderObservation Current { get; set; } = ReaderObservation.Absent;
        public ReaderObservation Poll() => Current;
    }

    private sealed class Http(Func<string, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<string> AlbumRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"]!;
            lock (AlbumRequests) AlbumRequests.Add(id);
            return reply(id, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Songs <prefix>1..<count> in order, with explicit track numbers.
    private static HttpResponseMessage Songs(string prefix, int count) =>
        Json("{\"subsonic-response\":{\"status\":\"ok\",\"album\":{\"song\":["
            + string.Join(",", Enumerable.Range(1, count).Select(n =>
                $"{{\"id\":\"{prefix}{n}\",\"title\":\"{prefix} {n}\",\"discNumber\":1,\"track\":{n}}}"))
            + "]}}}");

    private sealed class Rig
    {
        public required FakeMpvPeer Peer;
        public required Mpv Mpv;
        public required PlaybackOwner Owner;
        public required Http Http;
        public required SelectionController Controller;
        public required ScriptedReader Reader;
        public required FakeClock Clock;
        public required List<string> Log;

        // Simulated mpv state, driven by the peer's handler.
        public readonly List<string> Queue = [];
        public bool Paused;
        public readonly object Gate = new();

        public string[] QueueIds { get { lock (Gate) return Queue.Select(IdOf).ToArray(); } }

        public async Task Present(string uid, string release)
        {
            Reader.Current = ReaderObservation.Present(uid, () =>
                release == "none"
                    ? new TagReadResult(TagReadKind.None, Message: "No album identity on tag.")
                    : new TagReadResult(TagReadKind.Release, ReleaseId.Parse(release)));
            await Tick();
        }

        public async Task Tick(int ms = 150)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(ms));
            await Controller.StepAsync(CancellationToken.None);
            try { await Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromMilliseconds(100)); }
            catch (TimeoutException) { }
        }

        public async Task Ticks(int n, int ms = 150)
        {
            for (var i = 0; i < n; i++) await Tick(ms);
        }
    }

    private static string IdOf(string url) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["id"]!;

    private async Task<Rig> BuildAsync(
        Func<string, CancellationToken, Task<HttpResponseMessage>>? http = null,
        Func<FakeMpvPeer.Request, bool>? failLoad = null,
        MpvTimeouts? timeouts = null)
    {
        var catalogue = Path.Combine(_dir, "catalogue.json");
        File.WriteAllText(catalogue, $$"""
            { "source": "navidrome", "albums": {
              "{{ReleaseA}}": { "navidromeId": "srvA", "artist": "A", "title": "Album A" },
              "{{ReleaseB}}": { "navidromeId": "srvB", "artist": "B", "title": "Album B" } } }
            """);

        var peer = new FakeMpvPeer(Path.Combine(_dir, "m.sock"));
        _disposables.Add(peer);
        var mpv = await Mpv.ConnectAsync(peer.Path, timeouts ?? Fast, TimeSpan.FromSeconds(2), CancellationToken.None);
        _disposables.Add(mpv);

        var stub = new Http(http ?? ((id, _) => Task.FromResult(id switch
        {
            "srvA" => Songs("tA", 3),
            "srvB" => Songs("tB", 2),
            _ => Json("""{"subsonic-response":{"status":"failed","error":{"code":70,"message":"x"}}}"""),
        })));
        var options = LuciferOptions.Load(
            n => n switch { "NAVIDROME_URL" => "https://music.example.test", "NAVIDROME_USER" => "u", "NAVIDROME_PASSWORD" => "p", _ => null },
            Mode.Play).Options!;
        var navidrome = new Navidrome(new HttpClient(stub), options);

        var log = new List<string>();
        var owner = new PlaybackOwner(mpv);
        var applier = new PlayerAlbumApplier(catalogue, navidrome, mpv, TextWriter.Null);
        var reader = new ScriptedReader();
        var clock = new FakeClock();
        var controller = new SelectionController(
            reader, applier,
            new SelectionPolicy(TimeSpan.FromSeconds(2), MaxAttempts: 3, RetryDelay: TimeSpan.FromSeconds(1), MaxRetryDelay: TimeSpan.FromSeconds(4)),
            clock, log.Add);

        var rig = new Rig
        {
            Peer = peer, Mpv = mpv, Owner = owner, Http = stub, Controller = controller,
            Reader = reader, Clock = clock, Log = log,
        };

        peer.Handler = async (req, conn) =>
        {
            switch (req.Name)
            {
                case "loadfile":
                    if (failLoad?.Invoke(req) == true)
                    {
                        await conn.ReplyAsync(req.Id, error: "invalid parameter");
                        return true;
                    }
                    lock (rig.Gate)
                    {
                        if (req.Command[2].GetString() == "replace") rig.Queue.Clear();
                        rig.Queue.Add(req.Command[1].GetString()!);
                    }
                    return false;
                case "stop":
                    lock (rig.Gate) rig.Queue.Clear();
                    return false;
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

    // A started and its first track is playing.
    private static async Task PlayA(Rig rig)
    {
        await rig.Present("04AA", ReleaseA);
        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        await rig.Peer.Last!.EventAsync("start-file", new { playlist_entry_id = rig.Peer.NextEntryId - 2 });
        await rig.Peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Playing));
    }

    private static int MutatingCount(FakeMpvPeer peer) =>
        peer.Requests.Count(r => r.Name is "loadfile" or "stop" or "playlist-clear");

    // ---- A15: replacement -------------------------------------------------------------------

    [Fact]
    public async Task Replacing_A_with_B_leaves_all_and_only_B_in_order_exactly_once()
    {
        var rig = await BuildAsync();
        await PlayA(rig);

        await rig.Present("04BB", ReleaseB);

        Assert.Equal(["tB1", "tB2"], rig.QueueIds);
        var loads = rig.Peer.Requests.Where(r => r.Name == "loadfile").ToArray();
        Assert.Equal(["tA1", "tA2", "tA3", "tB1", "tB2"], loads.Select(r => IdOf(r.Command[1].GetString()!)));
        Assert.Equal(["replace", "append", "append", "replace", "append"], loads.Select(r => r.Command[2].GetString()));
    }

    [Fact]
    public async Task Replacing_a_paused_A_starts_B_unpaused_with_status_derived_from_mpv_events()
    {
        var rig = await BuildAsync();
        await PlayA(rig);
        await rig.Peer.Last!.EventAsync("property-change", new { name = "pause", data = true });
        rig.Paused = true;
        Assert.Equal(MpvStatus.Paused, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Paused));

        await rig.Present("04BB", ReleaseB);

        Assert.Equal(["tB1", "tB2"], rig.QueueIds);
        Assert.False(rig.Paused);
        // Unpausing is part of the apply, after the replace and before it reports success.
        var requests = rig.Peer.Requests;
        var lastReplace = requests.ToList().FindLastIndex(r => r.Name == "loadfile" && r.Command[2].GetString() == "replace");
        var unpause = requests.ToList().FindLastIndex(r => r.Name == "set_property" && !r.Command[2].GetBoolean());
        Assert.True(unpause > lastReplace);

        await rig.Peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Playing));
    }

    [Fact]
    public async Task Failed_preparation_of_B_leaves_A_in_the_queue_and_playing()
    {
        var rig = await BuildAsync((id, _) => Task.FromResult(id == "srvA"
            ? Songs("tA", 3)
            : Json("down", HttpStatusCode.ServiceUnavailable)));
        await PlayA(rig);
        var before = MutatingCount(rig.Peer);

        await rig.Present("04BB", ReleaseB);
        await rig.Ticks(30);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.Equal(MpvStatus.Playing, rig.Mpv.Status);
        Assert.Equal(before, MutatingCount(rig.Peer));
    }

    [Fact]
    public async Task Permanently_rejected_B_leaves_A_playing()
    {
        var rig = await BuildAsync((id, _) => Task.FromResult(id == "srvA"
            ? Songs("tA", 3)
            : Json("""{"subsonic-response":{"status":"ok","album":{"song":[]}}}""")));
        await PlayA(rig);
        var before = MutatingCount(rig.Peer);

        await rig.Present("04BB", ReleaseB);
        await rig.Ticks(5);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.Equal(MpvStatus.Playing, rig.Mpv.Status);
        Assert.Equal(before, MutatingCount(rig.Peer));
        Assert.Contains(rig.Log, l => l.Contains("no tracks"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData(Unmapped)]
    public async Task Invalid_or_unmapped_tag_leaves_playback_unchanged(string release)
    {
        var rig = await BuildAsync();
        await PlayA(rig);
        var before = MutatingCount(rig.Peer);

        await rig.Present("04CC", release);
        await rig.Ticks(10);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.Equal(MpvStatus.Playing, rig.Mpv.Status);
        Assert.Equal(before, MutatingCount(rig.Peer));
        Assert.Equal(["srvA"], rig.Http.AlbumRequests);
    }

    [Fact]
    public async Task Duplicate_track_ids_are_rejected_before_any_mutation()
    {
        var rig = await BuildAsync((id, _) => Task.FromResult(id == "srvA"
            ? Songs("tA", 3)
            : Json("""{"subsonic-response":{"status":"ok","album":{"song":[{"id":"x","track":1},{"id":"x","track":2}]}}}""")));
        await PlayA(rig);
        var before = MutatingCount(rig.Peer);

        await rig.Present("04BB", ReleaseB);
        await rig.Ticks(5);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.Equal(before, MutatingCount(rig.Peer));
    }

    // ---- A15: failure after mutation started ------------------------------------------------

    [Fact]
    public async Task Failure_after_the_queue_changed_is_reported_reconciled_and_a_later_valid_selection_is_accepted()
    {
        var rig = await BuildAsync(failLoad: r =>
            r.Command[2].GetString() == "append" && IdOf(r.Command[1].GetString()!) == "tA2");
        var statuses = new List<MpvStatus>();
        rig.Mpv.StatusChanged += statuses.Add;

        await rig.Present("04AA", ReleaseA);

        // The partial queue is not left playing: it is stopped and the player is explicitly Failed.
        Assert.Equal(MpvStatus.Failed, rig.Mpv.Status);
        Assert.Contains("incomplete", rig.Mpv.FailureDetail);
        Assert.Empty(rig.QueueIds);
        Assert.DoesNotContain(MpvStatus.Playing, statuses);
        Assert.Contains(rig.Log, l => l.Contains("incomplete"));

        // A late event from the abandoned entries does not turn the failure into playback.
        await rig.Peer.Last!.EventAsync("file-loaded");
        await Task.Delay(50);
        Assert.Equal(MpvStatus.Failed, rig.Mpv.Status);

        await rig.Present("04BB", ReleaseB);
        Assert.Equal(["tB1", "tB2"], rig.QueueIds);
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);
        await rig.Peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Playing));
    }

    [Fact]
    public async Task First_playlist_command_rejected_is_not_a_mutation_and_the_retry_plays_the_album_once()
    {
        var failures = 1;
        var rig = await BuildAsync(failLoad: r => r.Command[2].GetString() == "replace" && Interlocked.Decrement(ref failures) >= 0);
        await rig.Present("04AA", ReleaseA);
        await rig.Ticks(30);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.DoesNotContain(rig.Peer.Requests, r => r.Name == "stop");
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);
    }

    [Fact]
    public async Task Uncertain_outcome_is_reconciled_before_any_new_load_and_never_replayed_blindly()
    {
        var rig = await BuildAsync();
        var drop = true;
        var inner = rig.Peer.Handler!;
        rig.Peer.Handler = async (req, conn) =>
        {
            // An append is accepted by mpv but its reply is lost; stop also gets no reply while `drop`.
            if (drop && req.Name == "loadfile" && req.Command[2].GetString() == "append") { await inner(req, conn); return true; }
            if (drop && req.Name == "stop") return true;
            return await inner(req, conn);
        };
        var urls = new[] { "http://x/?id=1", "http://x/?id=2", "http://x/?id=3" };

        var first = await Assert.ThrowsAsync<MpvException>(() => rig.Mpv.LoadAlbumAsync(urls, CancellationToken.None));
        Assert.Equal(MpvFailureKind.AlbumIncomplete, first.Kind);
        Assert.Equal(MpvStatus.Failed, rig.Mpv.Status);

        // The reconcile could not be confirmed; the next load must reconcile first and, while that
        // fails, send no playlist command at all.
        var loadsBefore = rig.Peer.Requests.Count(r => r.Name == "loadfile");
        await Assert.ThrowsAsync<MpvException>(() => rig.Mpv.LoadAlbumAsync(urls, CancellationToken.None));
        Assert.Equal(loadsBefore, rig.Peer.Requests.Count(r => r.Name == "loadfile"));

        drop = false;
        await rig.Mpv.LoadAlbumAsync(urls, CancellationToken.None);

        var names = rig.Peer.Requests.Select(r => r.Name).ToList();
        var lastStop = names.LastIndexOf("stop");
        var lastReplace = rig.Peer.Requests.ToList().FindLastIndex(r => r.Name == "loadfile" && r.Command[2].GetString() == "replace");
        Assert.True(lastStop >= 0 && lastStop < lastReplace);
        Assert.Equal(["1", "2", "3"], rig.QueueIds.Select(u => u));
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);
    }

    // ---- A16: accepted but not loaded -------------------------------------------------------

    [Fact]
    public async Task Accepted_playlist_without_load_or_terminal_event_fails_at_the_stream_deadline_and_never_plays()
    {
        var rig = await BuildAsync();
        var statuses = new List<MpvStatus>();
        rig.Mpv.StatusChanged += statuses.Add;

        await rig.Present("04AA", ReleaseA);
        Assert.Equal(MpvStatus.Loading, rig.Mpv.Status);

        Assert.Equal(MpvStatus.Failed, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Failed));
        Assert.Contains("nothing started", rig.Mpv.FailureDetail);
        Assert.DoesNotContain(MpvStatus.Playing, statuses);

        // Recoverable: a later valid selection is accepted and can play.
        await rig.Present("04BB", ReleaseB);
        Assert.Equal(["tB1", "tB2"], rig.QueueIds);
        await rig.Peer.Last!.EventAsync("file-loaded");
        Assert.Equal(MpvStatus.Playing, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Playing));
    }

    [Fact]
    public async Task Stream_error_on_the_selected_album_is_a_visible_failure()
    {
        var rig = await BuildAsync();
        await rig.Present("04AA", ReleaseA);

        await rig.Peer.Last!.EventAsync("end-file", new { reason = "error", playlist_entry_id = rig.Peer.NextEntryId - 2, file_error = "loading failed" });

        Assert.Equal(MpvStatus.Failed, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Failed));
        Assert.Contains("loading failed", rig.Mpv.FailureDetail);
    }

    // ---- A17: stale preparation -------------------------------------------------------------

    [Fact]
    public async Task Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive()
    {
        var releaseA = new TaskCompletionSource();
        // The stalled request deliberately ignores cancellation, as a stuck connection might.
        var rig = await BuildAsync(async (id, _) =>
        {
            if (id != "srvA") return Songs("tB", 2);
            await releaseA.Task;
            return Songs("tA", 3);
        });

        var sw = Stopwatch.StartNew();
        rig.Reader.Current = ReaderObservation.Present("04AA", () => new(TagReadKind.Release, ReleaseId.Parse(ReleaseA)));
        await rig.Controller.StepAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(150));
        await rig.Controller.StepAsync(CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds < 500, "scanning must not wait for preparation");
        await Eventually(() => rig.Http.AlbumRequests.Count, c => c == 1);

        await rig.Present("04BB", ReleaseB);
        Assert.Equal(["tB1", "tB2"], rig.QueueIds);

        releaseA.SetResult();
        await Task.Delay(200);
        await rig.Ticks(3);

        Assert.Equal(["tB1", "tB2"], rig.QueueIds);
        Assert.Equal(ReleaseId.Parse(ReleaseB), rig.Controller.AppliedRelease);
        Assert.DoesNotContain(rig.Peer.Requests, r => r.Name == "loadfile" && IdOf(r.Command[1].GetString()!).StartsWith("tA"));
    }

    [Fact]
    public async Task Superseded_commit_is_rejected_by_the_owner_before_any_mpv_command()
    {
        var rig = await BuildAsync();
        using var superseded = new CancellationTokenSource();
        superseded.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rig.Owner.ApplyAlbumAsync(["http://x/?id=1"], superseded.Token));

        Assert.Equal(0, MutatingCount(rig.Peer));
        Assert.DoesNotContain(rig.Peer.Requests, r => r.Name == "set_property" && r.Command[1].GetString() == "pause");
        Assert.Equal(MpvStatus.Idle, rig.Mpv.Status);
    }

    [Fact]
    public async Task Supersession_during_the_apply_does_not_cut_the_queue_short()
    {
        var rig = await BuildAsync();
        using var cts = new CancellationTokenSource();
        var inner = rig.Peer.Handler!;
        rig.Peer.Handler = async (req, conn) =>
        {
            if (req.Name == "loadfile" && req.Command[2].GetString() == "append")
                cts.Cancel(); // a newer selection arrives while the queue is being built
            return await inner(req, conn);
        };

        await rig.Owner.ApplyAlbumAsync(["http://x/?id=1", "http://x/?id=2", "http://x/?id=3"], cts.Token);

        Assert.Equal(["1", "2", "3"], rig.QueueIds);
    }

    // ---- A10: transient failure before any load ---------------------------------------------

    [Fact]
    public async Task Transient_source_failure_then_success_while_present_starts_the_album_once()
    {
        var calls = 0;
        var rig = await BuildAsync((id, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? Json("down", HttpStatusCode.ServiceUnavailable)
            : Songs("tA", 3)));

        await rig.Present("04AA", ReleaseA);
        await rig.Ticks(30);

        Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
        Assert.Equal(1, rig.Peer.Requests.Count(r => r.Name == "loadfile" && r.Command[2].GetString() == "replace"));
        Assert.Equal(2, rig.Http.AlbumRequests.Count);
    }

    // ---- serialized owner / transport entry point -------------------------------------------

    [Fact]
    public async Task Transport_commands_follow_observed_status_and_are_ignored_otherwise()
    {
        var rig = await BuildAsync();

        Assert.Equal(TransportOutcome.Ignored, await rig.Owner.TransportAsync(TransportCommand.Pause, CancellationToken.None));
        Assert.DoesNotContain(rig.Peer.Requests, r => r.Name == "set_property" && r.Command[1].GetString() == "pause");

        await PlayA(rig);
        Assert.Equal(TransportOutcome.Applied, await rig.Owner.TransportAsync(TransportCommand.Pause, CancellationToken.None));
        Assert.True(rig.Paused);
        Assert.Equal(MpvStatus.Paused, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Paused));

        Assert.Equal(TransportOutcome.Applied, await rig.Owner.TransportAsync(TransportCommand.TogglePause, CancellationToken.None));
        Assert.False(rig.Paused);
        Assert.Equal(TransportOutcome.Applied, await rig.Owner.TransportAsync(TransportCommand.Next, CancellationToken.None));
        Assert.Contains(rig.Peer.Requests, r => r.Name == "playlist-next");
        Assert.Equal(TransportOutcome.Applied, await rig.Owner.TransportAsync(TransportCommand.Previous, CancellationToken.None));
        Assert.Contains(rig.Peer.Requests, r => r.Name == "playlist-prev");
    }

    [Fact]
    public async Task Transport_is_serialized_with_album_replacement()
    {
        var rig = await BuildAsync();
        await PlayA(rig);
        var inner = rig.Peer.Handler!;
        var midway = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        rig.Peer.Handler = async (req, conn) =>
        {
            if (req.Name == "loadfile" && IdOf(req.Command[1].GetString()!) == "tB2")
            {
                midway.TrySetResult();
                await go.Task; // stall inside the replacement
            }
            return await inner(req, conn);
        };

        await rig.Present("04BB", ReleaseB);
        await midway.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transport = rig.Owner.TransportAsync(TransportCommand.Pause, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(transport.IsCompleted);
        go.SetResult();
        await transport;
        await rig.Controller.WhenIdleAsync();

        var requests = rig.Peer.Requests.ToList();
        var lastLoad = requests.FindLastIndex(r => r.Name == "loadfile");
        var pauseTrue = requests.FindLastIndex(r => r.Name == "set_property" && r.Command[2].GetBoolean());
        Assert.True(pauseTrue > lastLoad);
        Assert.Equal(1, rig.Peer.MaxInFlight);
    }

    // ---- stop at album end ------------------------------------------------------------------

    [Fact]
    public async Task Last_track_ending_stops_and_issues_no_repeat()
    {
        var rig = await BuildAsync();
        await PlayA(rig);
        var before = rig.Peer.Requests.Count;
        var last = rig.Peer.NextEntryId;

        await rig.Peer.Last!.EventAsync("end-file", new { reason = "eof", playlist_entry_id = last });

        Assert.Equal(MpvStatus.Ended, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Ended));
        await rig.Ticks(10);
        Assert.Equal(before, rig.Peer.Requests.Count);
    }

    [Fact]
    public void Mpv_is_started_without_playlist_or_file_repeat()
    {
        var args = Mpv.LaunchArguments("alsa/x", "/run/s.sock");

        Assert.Contains("--loop-playlist=no", args);
        Assert.Contains("--loop-file=no", args);
        Assert.Contains("--input-ipc-server=/run/s.sock", args);
    }

    [Fact]
    public void Mpv_is_started_without_a_terminal_and_with_log_messages_off()
    {
        var args = Mpv.LaunchArguments("alsa/x", "/run/s.sock");

        Assert.Contains("--no-terminal", args);
        Assert.Contains("--msg-level=all=no", args);
    }

    // Everything the application can print about a failing stream, with the failure details
    // deliberately hostile (mpv text that carries the authenticated URL). Credentials in the URL
    // are `u`, `t` and `s`; none may reach the output.
    [Fact]
    public async Task Failing_streams_never_put_authenticated_url_parameters_into_application_output()
    {
        var rig = await BuildAsync();
        var output = new StringWriter();
        rig.Mpv.StatusChanged += status => output.WriteLine(status == MpvStatus.Failed
            ? $"Player: failed — {rig.Mpv.FailureDetail}"
            : $"Player: {status.ToString().ToLowerInvariant()}");
        await rig.Present("04AA", ReleaseA);
        var loaded = rig.Peer.Requests.First(r => r.Name == "loadfile").Command[1].GetString()!;
        Assert.Matches(@"[?&]t=", loaded); // the URL really carries the credential

        await rig.Peer.Last!.EventAsync("end-file", new
        {
            reason = "error",
            playlist_entry_id = rig.Peer.NextEntryId - 2,
            file_error = $"loading failed: {loaded}",
        });
        Assert.Equal(MpvStatus.Failed, await Eventually(() => rig.Mpv.Status, s => s == MpvStatus.Failed));

        var everything = string.Join("\n", output.ToString(), rig.Mpv.FailureDetail, string.Join("\n", rig.Log));
        Assert.DoesNotMatch(@"[?&;](t|s|u)=", everything);
        Assert.DoesNotContain("music.example.test", everything);
        Assert.Contains("loading failed", rig.Mpv.FailureDetail);
    }
}
