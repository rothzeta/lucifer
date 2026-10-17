using Amp.Nfc;

namespace Amp.Nfc.Tests;

// Scripted reader, fake clock and fake applier. These sequences exercise controller policy only;
// they say nothing about how a real PN532 or tag behaves in the field.
public class SelectionControllerTests
{
    private static readonly string ReleaseA = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    private static readonly string ReleaseB = "d5e2d971-e856-4342-ad09-0e6ad1b5241a";

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class ScriptedReader : ISelectionReader
    {
        public Queue<ReaderObservation> Script { get; } = new();
        public ReaderObservation Default { get; set; } = ReaderObservation.Absent;
        public int Polls { get; private set; }
        public ReaderObservation Poll()
        {
            Polls++;
            return Script.Count > 0 ? Script.Dequeue() : Default;
        }
    }

    private sealed class FakeApplier : IAlbumApplier
    {
        public List<ReleaseId> Prepared { get; } = [];
        public List<ReleaseId> Committed { get; } = [];
        public Func<ReleaseId, Task>? OnPrepare { get; set; }
        public Func<ReleaseId, Task>? OnCommit { get; set; }

        public async Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken)
        {
            Prepared.Add(release);
            if (OnPrepare is not null)
                await OnPrepare(release).WaitAsync(cancellationToken);
            return new PreparedAlbum(this, release);
        }

        private sealed class PreparedAlbum(FakeApplier owner, ReleaseId release) : IPreparedAlbum
        {
            public async Task CommitAsync(CancellationToken cancellationToken)
            {
                if (owner.OnCommit is not null)
                    await owner.OnCommit(release);
                owner.Committed.Add(release);
            }
        }
    }

    private sealed class Rig
    {
        public FakeClock Clock { get; } = new();
        public ScriptedReader Reader { get; } = new();
        public FakeApplier Applier { get; } = new();
        public List<string> Log { get; } = [];
        public SelectionPolicy Policy { get; set; } = new(
            AbsenceConfirmation: TimeSpan.FromSeconds(2),
            MaxAttempts: 3,
            RetryDelay: TimeSpan.FromSeconds(1),
            MaxRetryDelay: TimeSpan.FromSeconds(4));
        private SelectionController? _controller;
        public SelectionController Controller => _controller ??=
            new(Reader, Applier, Policy, Clock, Log.Add);

        public async Task Tick(int milliseconds = 150)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            await Controller.StepAsync(CancellationToken.None);
            await Settle();
        }

        // Waits for background work; a deliberately blocked applier just times this out.
        public async Task Settle()
        {
            try { await Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromMilliseconds(50)); }
            catch (TimeoutException) { }
        }

        public async Task Ticks(int count, int milliseconds = 150)
        {
            for (var i = 0; i < count; i++)
                await Tick(milliseconds);
        }
    }

    private static ReaderObservation Tag(string uid, string? release) =>
        ReaderObservation.Present(uid, () => release is null
            ? new TagReadResult(TagReadKind.None, Message: "No album identity on tag.")
            : new TagReadResult(TagReadKind.Release, ReleaseId.Parse(release)));

    private static ReaderObservation Tag(string uid, Func<TagReadResult> read) =>
        ReaderObservation.Present(uid, read);

    private static ReleaseId Id(string text) => ReleaseId.Parse(text);

    // ---- A08: held tag does not restart ----------------------------------------------------

    [Fact]
    public async Task Held_tag_plays_once()
    {
        var rig = new Rig { };
        rig.Reader.Default = Tag("04AA", ReleaseA);

        await rig.Ticks(40);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
        Assert.Equal([Id(ReleaseA)], rig.Applier.Prepared);
        Assert.Equal(Id(ReleaseA), rig.Controller.AppliedRelease);
    }

    [Fact]
    public async Task Full_card_is_read_once_per_presentation_not_per_poll()
    {
        var rig = new Rig();
        var reads = 0;
        rig.Reader.Default = Tag("04AA", () => { reads++; return new(TagReadKind.Release, Id(ReleaseA)); });

        await rig.Ticks(20);

        Assert.Equal(1, reads);
    }

    // ---- A09: one missed observation does not rearm ----------------------------------------

    [Fact]
    public async Task One_null_detection_does_not_rearm()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);
        rig.Reader.Script.Enqueue(ReaderObservation.Absent);

        await rig.Ticks(10);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
        Assert.Equal("04AA", rig.Controller.ObservedUid);
    }

    [Fact]
    public async Task Short_gap_below_confirmation_interval_does_not_rearm()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        for (var i = 0; i < 5; i++) // 5 * 150 ms = 750 ms < 2 s
            rig.Reader.Script.Enqueue(ReaderObservation.Absent);
        await rig.Ticks(12);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Single_long_gap_between_two_polls_is_still_only_one_observation()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);
        rig.Reader.Script.Enqueue(ReaderObservation.Absent);

        await rig.Tick(30_000); // a stalled loop, then one null reading
        await rig.Ticks(5);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Reader_fault_preserves_state_and_never_confirms_absence()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        rig.Reader.Default = ReaderObservation.Fault("serial error");
        await rig.Ticks(60); // far past the confirmation interval

        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(5);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
        Assert.Equal("04AA", rig.Controller.ObservedUid);
    }

    [Fact]
    public async Task Fault_between_nulls_does_not_count_toward_absence()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        rig.Reader.Script.Enqueue(ReaderObservation.Absent);
        rig.Reader.Script.Enqueue(ReaderObservation.Fault("x"));
        await rig.Tick(1500);
        await rig.Tick(1500);
        await rig.Tick(150);

        Assert.Equal("04AA", rig.Controller.ObservedUid);
    }

    // ---- A11 / D06: removal leaves audio, re-presentation after confirmed absence restarts once

    [Fact]
    public async Task Confirmed_removal_clears_object_but_applies_nothing_and_says_so_once()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        rig.Reader.Default = ReaderObservation.Absent;
        await rig.Ticks(30);

        Assert.Null(rig.Controller.ObservedUid);
        Assert.Equal(Id(ReleaseA), rig.Controller.AppliedRelease); // audio left playing
        Assert.Single(rig.Applier.Committed);
        Assert.Single(rig.Log, l => l == "Album removed.");
    }

    [Fact]
    public async Task Representation_after_confirmed_absence_restarts_exactly_once()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);
        rig.Reader.Default = ReaderObservation.Absent;
        await rig.Ticks(20);

        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(20);

        Assert.Equal([Id(ReleaseA), Id(ReleaseA)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Different_tag_replaces_selection_without_waiting_for_absence()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        rig.Reader.Default = Tag("04BB", ReleaseB);
        await rig.Ticks(3);

        Assert.Equal([Id(ReleaseA), Id(ReleaseB)], rig.Applier.Committed);
        Assert.Equal(Id(ReleaseB), rig.Controller.AppliedRelease);
    }

    // ---- A10: transient failure recovers while the object remains ---------------------------

    [Fact]
    public async Task Transient_read_failure_retries_after_delay_while_scanning_continues()
    {
        var rig = new Rig();
        var reads = 0;
        rig.Reader.Default = Tag("04AA", () =>
        {
            if (++reads < 3) throw new IOException("NDEF read failed");
            return new(TagReadKind.Release, Id(ReleaseA));
        });

        await rig.Tick(150);
        Assert.Equal(1, reads);

        var pollsBefore = rig.Reader.Polls;
        await rig.Ticks(3, 150); // inside the 1 s delay: scanning continues, no new read
        Assert.Equal(1, reads);
        Assert.True(rig.Reader.Polls > pollsBefore);

        await rig.Tick(1000);
        Assert.Equal(2, reads);

        await rig.Tick(2100); // delay doubled
        Assert.Equal(3, reads);

        await rig.Ticks(30);
        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task Transient_prepare_failure_recovers_and_plays_once()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        var attempts = 0;
        rig.Applier.OnPrepare = _ =>
            ++attempts == 1 ? Task.FromException(new HttpRequestException("source down")) : Task.CompletedTask;

        await rig.Ticks(3);
        Assert.Empty(rig.Applier.Committed);

        await rig.Tick(1100);
        await rig.Ticks(30);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
        Assert.Equal(2, rig.Applier.Prepared.Count);
    }

    [Fact]
    public async Task Transient_commit_failure_is_retried()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        var commits = 0;
        rig.Applier.OnCommit = _ =>
            ++commits == 1 ? Task.FromException(new IOException("player down")) : Task.CompletedTask;

        await rig.Ticks(3);
        await rig.Tick(1100);
        await rig.Ticks(30);

        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Retries_are_bounded_then_suppressed_until_new_presentation()
    {
        var rig = new Rig();
        var reads = 0;
        rig.Reader.Default = Tag("04AA", () => { reads++; throw new IOException("always"); });

        for (var i = 0; i < 100; i++)
            await rig.Tick(500);

        Assert.Equal(3, reads); // MaxAttempts
        Assert.Empty(rig.Applier.Committed);

        // New presentation after confirmed absence is a fresh start.
        rig.Reader.Default = ReaderObservation.Absent;
        await rig.Ticks(20);
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(5);
        Assert.Equal([Id(ReleaseA)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Retry_stops_when_object_is_removed()
    {
        var rig = new Rig();
        var reads = 0;
        rig.Reader.Default = Tag("04AA", () => { reads++; throw new IOException("x"); });
        await rig.Tick(150);

        rig.Reader.Default = ReaderObservation.Absent;
        for (var i = 0; i < 40; i++)
            await rig.Tick(500);

        Assert.Equal(1, reads);
    }

    // ---- A17: invalid / unsupported suppressed without flooding -----------------------------

    [Fact]
    public async Task Invalid_tag_is_logged_once_and_not_reread()
    {
        var rig = new Rig();
        var reads = 0;
        rig.Reader.Default = Tag("04AA", () =>
        {
            reads++;
            return new(TagReadKind.Invalid, Message: "Tag record is malformed.");
        });

        await rig.Ticks(200);

        Assert.Equal(1, reads);
        Assert.Single(rig.Log, l => l == "Tag record is malformed.");
        Assert.Empty(rig.Applier.Prepared);
    }

    [Fact]
    public async Task Rejected_release_is_suppressed_with_one_log_line_and_no_requests()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        rig.Applier.OnPrepare = _ => Task.FromException(new SelectionRejectedException("Album is not available."));

        await rig.Ticks(200);

        Assert.Single(rig.Applier.Prepared);
        Assert.Single(rig.Log, l => l == "Album is not available.");
    }

    [Fact]
    public async Task Suppressed_tag_is_attempted_again_after_new_presentation()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        rig.Applier.OnPrepare = _ => Task.FromException(new SelectionRejectedException("nope"));
        await rig.Ticks(10);

        rig.Reader.Default = ReaderObservation.Absent;
        await rig.Ticks(20);
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(10);

        Assert.Equal(2, rig.Applier.Prepared.Count);
    }

    [Fact]
    public async Task Invalid_tag_does_not_disturb_playing_audio()
    {
        var rig = new Rig();
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Ticks(3);

        rig.Reader.Default = Tag("04CC", () => new(TagReadKind.Conflict, Message: "conflict"));
        await rig.Ticks(20);

        Assert.Equal(Id(ReleaseA), rig.Controller.AppliedRelease);
        Assert.Single(rig.Applier.Committed);
    }

    // ---- stale preparation ------------------------------------------------------------------

    [Fact]
    public async Task Stale_preparation_cannot_replace_newer_selection()
    {
        var rig = new Rig();
        var gateA = new TaskCompletionSource();
        var ignoreCancellation = true;
        rig.Applier.OnPrepare = release =>
            release == Id(ReleaseA) && ignoreCancellation ? gateA.Task : Task.CompletedTask;

        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Controller.StepAsync(CancellationToken.None); // A starts preparing, still pending

        rig.Reader.Default = Tag("04BB", ReleaseB);
        await rig.Tick();
        await rig.Controller.WhenIdleAsync();
        Assert.Equal([Id(ReleaseB)], rig.Applier.Committed);

        gateA.SetResult();
        await rig.Controller.WhenIdleAsync();

        Assert.Equal([Id(ReleaseB)], rig.Applier.Committed);
        Assert.Equal(Id(ReleaseB), rig.Controller.AppliedRelease);
    }

    [Fact]
    public async Task Superseded_preparation_is_cancelled()
    {
        var rig = new Rig();
        // The fake applier observes cancellation through WaitAsync(cancellationToken).
        rig.Applier.OnPrepare = release =>
            release == Id(ReleaseA) ? new TaskCompletionSource().Task : Task.CompletedTask;

        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Controller.StepAsync(CancellationToken.None);
        rig.Reader.Default = Tag("04BB", ReleaseB);
        await rig.Tick();

        await rig.Controller.WhenIdleAsync(); // returns only if A's preparation was cancelled
        Assert.Equal([Id(ReleaseB)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Stale_failure_does_not_schedule_retry_for_current_object()
    {
        var rig = new Rig();
        var gateA = new TaskCompletionSource();
        rig.Applier.OnPrepare = release =>
            release == Id(ReleaseA) ? gateA.Task : Task.CompletedTask;

        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Controller.StepAsync(CancellationToken.None);
        rig.Reader.Default = Tag("04BB", ReleaseB);
        await rig.Tick();

        gateA.SetException(new IOException("late"));
        await rig.Ticks(30, 500);

        Assert.Equal([Id(ReleaseA), Id(ReleaseB)], rig.Applier.Prepared);
        Assert.Equal([Id(ReleaseB)], rig.Applier.Committed);
    }

    [Fact]
    public async Task Pending_preparation_is_cancelled_when_object_is_confirmed_removed()
    {
        var rig = new Rig();
        rig.Applier.OnPrepare = _ => new TaskCompletionSource().Task;
        rig.Reader.Default = Tag("04AA", ReleaseA);
        await rig.Controller.StepAsync(CancellationToken.None);

        rig.Reader.Default = ReaderObservation.Absent;
        await rig.Ticks(30);

        Assert.Empty(rig.Applier.Committed);
        Assert.Null(rig.Controller.ObservedUid);
    }

    [Fact]
    public async Task Step_does_not_block_on_slow_preparation()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource();
        rig.Applier.OnPrepare = _ => gate.Task;
        rig.Reader.Default = Tag("04AA", ReleaseA);

        var step = rig.Controller.StepAsync(CancellationToken.None);

        Assert.True(step.IsCompleted || await Task.WhenAny(step, Task.Delay(2000)) == step);
        var before = rig.Reader.Polls;
        await rig.Controller.StepAsync(CancellationToken.None);
        Assert.True(rig.Reader.Polls > before);
        gate.SetResult();
        await rig.Controller.WhenIdleAsync();
        Assert.Single(rig.Applier.Committed);
    }
}
