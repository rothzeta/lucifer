using System.Text.Json;

namespace Amp.Nfc;

// Transport policy (play, pause, next, previous) over the IPC layer.
public sealed partial class Mpv
{
    /// <summary>How long a track must have played before Previous restarts it instead of going back.</summary>
    public static readonly TimeSpan PreviousRestartAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs one transport command under the same lock as album loads, judged against the status
    /// observed while it runs. Returns false when it does not apply (nothing is sent). Policy:
    /// play/pause only change the pause preference (also while loading); next advances and, on the
    /// last track, stops at the album end (status Ended, no wrap); previous restarts the current
    /// track when it has played more than <see cref="PreviousRestartAfter"/> or is the first
    /// track, else goes back one; next/previous never change the pause state; play (or toggle)
    /// after the album ended restarts the selected album from its first track, unpaused; with no
    /// applied album, or while loading or failed, next and previous do nothing.
    /// Track position comes from mpv (playlist-pos, playlist-count, time-pos); when mpv does not
    /// report it, next/previous fall back to mpv's own playlist-next/prev.
    /// </summary>
    public async Task<bool> TransportAsync(TransportCommand command, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            MpvStatus status;
            bool paused;
            string[]? album;
            lock (_gate) { status = _status; paused = _paused; album = _lastUrls; }

            switch (command)
            {
                case TransportCommand.Pause when status is MpvStatus.Playing || (status == MpvStatus.Loading && !paused):
                    return await SetPauseAsync(true, cancellationToken);
                case TransportCommand.Play when status == MpvStatus.Paused || (status == MpvStatus.Loading && paused):
                    return await SetPauseAsync(false, cancellationToken);
                case TransportCommand.TogglePause when status is MpvStatus.Playing or MpvStatus.Paused or MpvStatus.Loading:
                    return await SetPauseAsync(status == MpvStatus.Paused ? false : status == MpvStatus.Loading ? !paused : true, cancellationToken);
                case TransportCommand.Play or TransportCommand.TogglePause when status == MpvStatus.Ended && album is not null:
                    await ReplaceQueueAsync(album);
                    return true;
                case TransportCommand.Next when status is MpvStatus.Playing or MpvStatus.Paused:
                    return await NextTrackAsync(cancellationToken);
                case TransportCommand.Previous when status is MpvStatus.Playing or MpvStatus.Paused:
                    return await PreviousTrackAsync(cancellationToken);
                default:
                    return false;
            }
        }
        finally { _operations.Release(); }
    }

    // Caller holds _operations for the helpers below.
    private async Task<bool> SetPauseAsync(bool pause, CancellationToken cancellationToken)
    {
        await SendAsync(["set_property", "pause", pause], null, cancellationToken);
        // mpv accepted it: act on it now, so the next control sees it without waiting for the property
        // event (which arrives after the reply and says the same thing).
        Update(() => PauseTransition(pause));
        return true;
    }

    private async Task<bool> NextTrackAsync(CancellationToken cancellationToken)
    {
        var position = await ReadNumberAsync("playlist-pos", cancellationToken);
        var count = await ReadNumberAsync("playlist-count", cancellationToken);

        if (position is { } p && count is { } c && c > 0 && p >= c - 1)
        {
            // Last track: end the album instead of letting mpv wrap or restart.
            await SendAsync(["stop"], null, cancellationToken);
            Update(() =>
            {
                DisarmStreamDeadline();
                return (MpvStatus.Ended, null);
            });
            return true;
        }

        await SendAsync(["playlist-next", "force"], null, cancellationToken);
        return true;
    }

    private async Task<bool> PreviousTrackAsync(CancellationToken cancellationToken)
    {
        var position = await ReadNumberAsync("playlist-pos", cancellationToken);
        var played = await ReadNumberAsync("time-pos", cancellationToken);

        var restart = played is { } seconds && seconds > PreviousRestartAfter.TotalSeconds;
        if (position is { } p && p <= 0)
            restart = true; // first track: there is nothing before it

        await SendAsync(restart ? ["seek", 0, "absolute"] : ["playlist-prev", "force"], null, cancellationToken);
        return true;
    }

    // A property mpv cannot report (nothing playing, or no such value) reads as unknown.
    private async Task<double?> ReadNumberAsync(string property, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await SendAsync(["get_property", property], null, cancellationToken);
            return reply.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Number && data.TryGetDouble(out var value)
                ? value
                : null;
        }
        catch (MpvException ex) when (ex.Kind == MpvFailureKind.CommandError)
        {
            return null;
        }
    }
}
