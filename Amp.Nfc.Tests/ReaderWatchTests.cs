using Amp.Nfc;

namespace Amp.Nfc.Tests;

// Scripted observations and a fake clock. They exercise escalation policy only; how often a real
// PN532 binding returns null or throws on unplug is device-pending (LUC-08).
public class ReaderWatchTests
{
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class FakeHealth : IReaderHealth
    {
        public bool Healthy = true;
        public bool Throws;
        public int Checks;
        public ReaderHealthResult Check()
        {
            Checks++;
            if (Throws) throw new IOException("port gone");
            return new(Healthy, Healthy ? "" : "no answer");
        }
    }

    private static readonly SupervisorPolicy Policy = new()
    {
        FaultWindow = TimeSpan.FromSeconds(10),
        FaultThreshold = 5,
        HealthInterval = TimeSpan.FromSeconds(60),
        HealthRetryInterval = TimeSpan.FromSeconds(5),
        HealthFailures = 3,
    };

    private readonly FakeClock _clock = new();
    private readonly FakeHealth _health = new();

    private ReaderWatch Watch() => new(Policy, _health, _clock);

    [Fact]
    public void Hours_of_null_detections_with_a_healthy_reader_are_never_fatal()
    {
        var watch = Watch();
        for (var i = 0; i < 24 * 3600 / 5; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Null(watch.Observe(ReaderObservation.Absent));
        }
        Assert.True(_health.Checks > 100, "idle readers are still checked explicitly");
    }

    [Fact]
    public void Null_detections_alone_are_not_proof_without_a_failed_health_check()
    {
        var watch = Watch();
        _health.Healthy = true;
        for (var i = 0; i < 10_000; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(150));
            Assert.Null(watch.Observe(ReaderObservation.Absent));
        }
    }

    [Fact]
    public void Persistent_failed_health_checks_are_fatal_after_the_configured_count()
    {
        var watch = Watch();
        _health.Healthy = false;
        string? fatal = null;
        for (var i = 0; i < 2000 && fatal is null; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            fatal = watch.Observe(ReaderObservation.Absent);
        }

        Assert.NotNull(fatal);
        Assert.Contains("health", fatal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, _health.Checks);
    }

    [Fact]
    public void A_health_check_that_recovers_resets_the_failure_count()
    {
        var watch = Watch();
        _health.Healthy = false;
        // Probes fail at 60 s and 65 s: two of three.
        for (var i = 0; i < 66; i++) { _clock.Advance(TimeSpan.FromSeconds(1)); Assert.Null(watch.Observe(ReaderObservation.Absent)); }
        _health.Healthy = true;
        for (var i = 0; i < 20; i++) { _clock.Advance(TimeSpan.FromSeconds(1)); Assert.Null(watch.Observe(ReaderObservation.Absent)); }
        _health.Healthy = false;
        // The next probes are at 130 s, 135 s, 140 s: two fresh failures by 139 s are still not fatal.
        for (var i = 0; i < 53; i++) { _clock.Advance(TimeSpan.FromSeconds(1)); Assert.Null(watch.Observe(ReaderObservation.Absent)); }
    }

    [Fact]
    public void A_health_check_that_throws_counts_as_failed()
    {
        var watch = Watch();
        _health.Throws = true;
        string? fatal = null;
        for (var i = 0; i < 2000 && fatal is null; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            fatal = watch.Observe(ReaderObservation.Absent);
        }
        Assert.NotNull(fatal);
    }

    [Fact]
    public void A_burst_of_faults_that_ends_is_recoverable()
    {
        var watch = Watch();
        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(3));
            Assert.Null(watch.Observe(ReaderObservation.Fault("x")));
        }
        Assert.Null(watch.Observe(ReaderObservation.Absent));
        // The run restarts: four more faults are again below the threshold.
        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(3));
            Assert.Null(watch.Observe(ReaderObservation.Fault("x")));
        }
    }

    [Fact]
    public void Consecutive_faults_over_the_window_are_fatal_and_carry_the_detail()
    {
        var watch = Watch();
        string? fatal = null;
        for (var i = 0; i < 100 && fatal is null; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            fatal = watch.Observe(ReaderObservation.Fault("serial port closed"));
        }
        Assert.NotNull(fatal);
        Assert.Contains("serial port closed", fatal);
    }

    [Fact]
    public void Many_faults_in_a_short_time_are_not_yet_fatal()
    {
        var watch = Watch();
        for (var i = 0; i < 50; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Null(watch.Observe(ReaderObservation.Fault("x")));
        }
    }

    [Fact]
    public void A_present_tag_resets_faults()
    {
        var watch = Watch();
        for (var i = 0; i < 4; i++) { _clock.Advance(TimeSpan.FromSeconds(3)); watch.Observe(ReaderObservation.Fault("x")); }
        watch.Observe(ReaderObservation.Present("04AA", () => throw new InvalidOperationException()));
        for (var i = 0; i < 4; i++) { _clock.Advance(TimeSpan.FromSeconds(3)); Assert.Null(watch.Observe(ReaderObservation.Fault("x"))); }
    }

    [Fact]
    public void The_decorator_turns_a_throwing_reader_into_faults_and_records_the_verdict()
    {
        var inner = new ThrowingReader();
        var watch = Watch();
        var reader = new WatchedReader(inner, watch);

        ReaderObservation last = ReaderObservation.Absent;
        for (var i = 0; i < 100 && watch.FatalReason is null; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            last = reader.Poll();
        }

        Assert.Equal(ObservationKind.Fault, last.Kind);
        Assert.NotNull(watch.FatalReason);
        Assert.Contains("boom", watch.FatalReason);
    }

    private sealed class ThrowingReader : ISelectionReader
    {
        public ReaderObservation Poll() => throw new IOException("boom");
    }
}
