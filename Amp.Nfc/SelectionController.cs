namespace Amp.Nfc;

/// <summary>
/// Separates three facts the old single UID latch conflated: which object is on the reader,
/// whether selecting it has succeeded, and which album is applied to the player.
/// StepAsync is called from one scan loop and never waits for preparation; preparation and commit
/// run in the background under a generation value, and a newer selection cancels and outranks an
/// older one. Transient failures retry with bounded, delayed attempts; permanent ones are
/// suppressed until the object is presented again.
/// </summary>
public sealed class SelectionController
{
    private enum Phase { Unread, Retrying, Preparing, Applied, Suppressed }

    private readonly ISelectionReader _reader;
    private readonly IAlbumApplier _applier;
    private readonly SelectionPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly Action<string> _log;

    private readonly object _gate = new();
    private ReleaseId? _appliedRelease;
    private readonly SemaphoreSlim _commit = new(1, 1);
    private readonly HashSet<Task> _running = [];

    private string? _uid;
    private Phase _phase;
    private ReleaseId? _candidate;
    private long _generation;
    private CancellationTokenSource? _preparing;
    private int _attempts;
    private DateTimeOffset _retryAt;
    private int _missed;
    private DateTimeOffset _missingSince;

    public SelectionController(
        ISelectionReader reader,
        IAlbumApplier applier,
        SelectionPolicy policy,
        TimeProvider clock,
        Action<string> log)
    {
        _reader = reader;
        _applier = applier;
        _policy = policy;
        _clock = clock;
        _log = log;
    }

    /// <summary>The object currently believed to be on the reader, or null once absence is confirmed.</summary>
    public string? ObservedUid { get { lock (_gate) return _uid; } }

    /// <summary>The album last committed to the player. Removal does not clear it.</summary>
    public ReleaseId? AppliedRelease { get { lock (_gate) return _appliedRelease; } }

    public Task StepAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observation = _reader.Poll();
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            switch (observation.Kind)
            {
                case ObservationKind.Fault:
                    break; // identified fault: neither presence nor absence changes
                case ObservationKind.Absent:
                    OnAbsent(now);
                    break;
                default:
                    OnPresent(observation, now);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Completes when no preparation is in flight. Used at shutdown and by tests.</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate) pending = [.. _running];
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending);
        }
    }

    /// <summary>Cancels in-flight preparation, for shutdown.</summary>
    public void Stop()
    {
        lock (_gate) Supersede();
    }

    private void OnAbsent(DateTimeOffset now)
    {
        if (_uid is null)
            return;
        if (_missed++ == 0)
            _missingSince = now;
        // One null is uncertain; require both repeated nulls and a measured interval.
        if (_missed < 2 || now - _missingSince < _policy.AbsenceConfirmation)
            return;

        _uid = null;
        _candidate = null;
        _phase = Phase.Unread;
        Supersede(); // a pending preparation for a withdrawn object must not start audio
        _log("Album removed.");
    }

    private void OnPresent(ReaderObservation observation, DateTimeOffset now)
    {
        _missed = 0;

        if (observation.Uid != _uid)
        {
            _uid = observation.Uid;
            _phase = Phase.Unread;
            _attempts = 0;
            _candidate = null;
            Supersede();
            _log($"Tag: {_uid}");
        }

        switch (_phase)
        {
            case Phase.Unread:
                ReadCard(observation, now);
                break;
            case Phase.Retrying when now >= _retryAt:
                if (_candidate is { } release)
                    StartPreparation(release);
                else
                    ReadCard(observation, now);
                break;
        }
    }

    private void ReadCard(ReaderObservation observation, DateTimeOffset now)
    {
        TagReadResult result;
        try
        {
            result = observation.Read!();
        }
        catch (Exception ex)
        {
            Transient($"Could not read tag: {UrlRedaction.Redact(ex.Message)}", now);
            return;
        }

        if (result.Kind != TagReadKind.Release)
        {
            _phase = Phase.Suppressed;
            _log(result.Message);
            return;
        }

        _candidate = result.Release;
        StartPreparation(result.Release);
    }

    // Caller holds the gate. Bounded: after MaxAttempts failures the object is suppressed.
    private void Transient(string message, DateTimeOffset now)
    {
        if (++_attempts >= _policy.MaxAttempts)
        {
            _phase = Phase.Suppressed;
            _log($"{message} Giving up until the tag is presented again.");
            return;
        }

        var delay = TimeSpan.FromTicks(Math.Min(
            _policy.CappedRetryDelay.Ticks,
            _policy.InitialRetryDelay.Ticks << (_attempts - 1)));
        _phase = Phase.Retrying;
        _retryAt = now + delay;
        _log($"{message} Retrying.");
    }

    // Caller holds the gate. Invalidates whatever was preparing before.
    private void Supersede()
    {
        _generation++;
        _preparing?.Cancel();
        _preparing = null;
    }

    private void StartPreparation(ReleaseId release)
    {
        Supersede();
        var generation = _generation;
        var cts = _preparing = new CancellationTokenSource();
        _phase = Phase.Preparing;

        var task = PrepareAndCommitAsync(release, generation, cts);
        _running.Add(task);
        // The source has no timer or linked registration, so it needs no disposal.
        _ = task.ContinueWith(t => { lock (_gate) _running.Remove(t); }, TaskScheduler.Default);
    }

    private async Task PrepareAndCommitAsync(ReleaseId release, long generation, CancellationTokenSource cts)
    {
        await Task.Yield();
        try
        {
            var prepared = await _applier.PrepareAsync(release, cts.Token);

            // Only one commit at a time, and a stale one never runs after a newer selection.
            await _commit.WaitAsync(cts.Token);
            try
            {
                lock (_gate)
                    if (generation != _generation)
                        return;
                await prepared.CommitAsync(cts.Token);
                lock (_gate)
                {
                    _appliedRelease = release;
                    if (generation == _generation)
                        _phase = Phase.Applied;
                }
            }
            finally { _commit.Release(); }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // superseded or stopped
        }
        catch (SelectionRejectedException ex)
        {
            lock (_gate)
                if (generation == _generation)
                {
                    _phase = Phase.Suppressed;
                    _log(ex.Message);
                }
        }
        catch (Exception ex)
        {
            lock (_gate)
                if (generation == _generation)
                    Transient($"Could not start album: {UrlRedaction.Redact(ex.Message)}", _clock.GetUtcNow());
        }
    }
}
