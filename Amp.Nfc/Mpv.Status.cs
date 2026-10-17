using System.Text.Json;

namespace Amp.Nfc;

// Status derived from mpv events, and the stream-start deadline.
public sealed partial class Mpv
{
    // ---- observations ------------------------------------------------------

    private void BeginSelection(JsonElement reply)
    {
        Update(() =>
        {
            _entries.Clear();
            _selected = true;
            _unfiltered = false;
            AddEntryLocked(reply);
            ArmStreamDeadline();
            return (MpvStatus.Loading, null);
        });
    }

    private void AddEntry(JsonElement reply)
    {
        lock (_gate) AddEntryLocked(reply);
    }

    private void AddEntryLocked(JsonElement reply)
    {
        if (reply.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("playlist_entry_id", out var id) && id.TryGetInt64(out var entry))
            _entries.Add(entry);
        else
            _unfiltered = true; // mpv did not say which entry; accept observations unfiltered
    }

    private void HandleEvent(string name, JsonElement ev)
    {
        long? entry = ev.TryGetProperty("playlist_entry_id", out var e) && e.TryGetInt64(out var v) ? v : null;

        Update(() =>
        {
            // The pause flag is player state, not selection state: a paused player stays paused
            // across a load until mpv reports otherwise.
            if (name == "property-change" && ev.TryGetProperty("name", out var pausedName) && pausedName.GetString() == "pause"
                && ev.TryGetProperty("data", out var pausedData) && pausedData.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return PauseTransition(pausedData.GetBoolean());
            }

            if (!_selected || (entry is { } id && !_unfiltered && !_entries.Contains(id)))
                return null; // not about the current selection

            switch (name)
            {
                case "start-file":
                    ArmStreamDeadline();
                    return (MpvStatus.Loading, null);

                case "file-loaded":
                    DisarmStreamDeadline();
                    return (_paused ? MpvStatus.Paused : MpvStatus.Playing, null);

                case "end-file":
                    var reason = ev.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    var last = entry is { } x && _entries.Count > 0 && _entries[^1] == x;
                    switch (reason)
                    {
                        case "error":
                            DisarmStreamDeadline();
                            var why = ev.TryGetProperty("file_error", out var fe) ? fe.GetString() : null;
                            return (MpvStatus.Failed, $"load failed: {UrlRedaction.Redact(why ?? "mpv reported an error")}");
                        case "eof" when last:
                            DisarmStreamDeadline();
                            return (MpvStatus.Ended, null);
                        case "stop" or "quit" when last:
                            DisarmStreamDeadline();
                            // The album was ended deliberately (next on the last track): stay Ended.
                            return (_status == MpvStatus.Ended ? MpvStatus.Ended : MpvStatus.Idle, null);
                    }
                    return null;

            }

            return null;
        });
    }

    // Records the pause flag and the status change it implies. Called with the lock held.
    private (MpvStatus Status, string? Detail)? PauseTransition(bool paused)
    {
        _paused = paused;
        if (!_selected) return null;
        if (_status == MpvStatus.Playing && paused) return (MpvStatus.Paused, null);
        if (_status == MpvStatus.Paused && !paused) return (MpvStatus.Playing, null);
        return null;
    }

    // Called with the lock held.
    private void ArmStreamDeadline()
    {
        var seq = ++_timerSeq;
        _streamTimer?.Dispose();
        _streamTimer = new Timer(_ => OnStreamDeadline(seq), null, _timeouts.StreamStart, Timeout.InfiniteTimeSpan);
    }

    private void DisarmStreamDeadline()
    {
        _timerSeq++;
        _streamTimer?.Dispose();
        _streamTimer = null;
    }

    private void OnStreamDeadline(long seq) =>
        Update(() => seq == _timerSeq && _status == MpvStatus.Loading
            ? (MpvStatus.Failed,
                $"load failed: mpv accepted the playlist but nothing started within {_timeouts.StreamStart.TotalSeconds:0.##}s")
            : null);

    // Decide and apply a transition under the lock; raise the event after releasing it.
    private void Update(Func<(MpvStatus Status, string? Detail)?> decide)
    {
        MpvStatus? raised = null;
        lock (_gate)
        {
            if (_disposed || decide() is not { } next)
                return;
            _failure = next.Status == MpvStatus.Failed ? next.Detail : null;
            if (next.Status != _status)
            {
                _status = next.Status;
                raised = next.Status;
            }
        }

        if (raised is { } s)
            StatusChanged?.Invoke(s);
    }
}
