namespace Amp.Nfc;

public sealed record ReaderHealthResult(bool Healthy, string Detail);

/// <summary>
/// An explicit probe of the reader that does not depend on a tag being present. Used while idle,
/// because a null detection alone cannot distinguish "no tag" from some transport failures.
/// </summary>
public interface IReaderHealth
{
    ReaderHealthResult Check();
}

/// <summary>
/// Thresholds for fatal escalation. Not user options: they are policy defaults; the shutdown
/// deadline is the configurable one.
/// </summary>
public sealed class SupervisorPolicy
{
    /// <summary>Consecutive identified faults must span at least this long...</summary>
    public TimeSpan FaultWindow { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>...and number at least this many before the reader counts as unrecoverable.</summary>
    public int FaultThreshold { get; init; } = 20;

    /// <summary>How often an idle reader is probed while healthy.</summary>
    public TimeSpan HealthInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How soon to re-probe after a failed probe.</summary>
    public TimeSpan HealthRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive failed probes before the reader counts as unrecoverable.</summary>
    public int HealthFailures { get; init; } = 3;
}

/// <summary>
/// Decides when reader trouble is fatal. Evidence is only identified transport faults (a run of
/// them over a window) and failed explicit health probes. Long idle periods of null detections are
/// never evidence by themselves. A present tag proves the transport works; a null detection resets
/// the fault run but does not prove health, which is why the probe exists.
/// </summary>
public sealed class ReaderWatch(SupervisorPolicy policy, IReaderHealth health, TimeProvider clock)
{
    private readonly object _gate = new();
    private int _faults;
    private DateTimeOffset _faultsSince;
    private int _failedChecks;
    private DateTimeOffset? _nextCheck;

    public string? FatalReason { get; private set; }

    /// <summary>Records one observation; returns the fatal reason once reached, else null.</summary>
    public string? Observe(ReaderObservation observation)
    {
        lock (_gate)
        {
            if (FatalReason is not null)
                return FatalReason;

            var now = clock.GetUtcNow();
            _nextCheck ??= now + policy.HealthInterval;

            switch (observation.Kind)
            {
                case ObservationKind.Fault:
                    if (_faults++ == 0)
                        _faultsSince = now;
                    if (_faults >= policy.FaultThreshold && now - _faultsSince >= policy.FaultWindow)
                        FatalReason =
                            $"Reader transport failing: {_faults} consecutive faults over {(now - _faultsSince).TotalSeconds:0.#}s ({observation.Detail}).";
                    break;

                case ObservationKind.Present:
                    _faults = 0;
                    _failedChecks = 0;
                    _nextCheck = now + policy.HealthInterval;
                    break;

                default:
                    _faults = 0;
                    CheckHealth(now);
                    break;
            }

            return FatalReason;
        }
    }

    private void CheckHealth(DateTimeOffset now)
    {
        if (now < _nextCheck)
            return;

        ReaderHealthResult result;
        try { result = health.Check(); }
        catch (Exception ex) { result = new(false, ex.Message); }

        if (result.Healthy)
        {
            _failedChecks = 0;
            _nextCheck = now + policy.HealthInterval;
            return;
        }

        if (++_failedChecks >= policy.HealthFailures)
        {
            FatalReason = $"Reader health check failed {_failedChecks} times in a row ({result.Detail}).";
            return;
        }
        _nextCheck = now + policy.HealthRetryInterval;
    }
}

/// <summary>
/// Reports every poll to the watch. An exception from the inner reader is an identified fault, not
/// something to retry silently forever.
/// </summary>
public sealed class WatchedReader(ISelectionReader inner, ReaderWatch watch) : ISelectionReader
{
    public ReaderObservation Poll()
    {
        ReaderObservation observation;
        try { observation = inner.Poll(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            observation = ReaderObservation.Fault(ex.Message);
        }
        watch.Observe(observation);
        return observation;
    }
}
