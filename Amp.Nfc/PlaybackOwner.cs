namespace Amp.Nfc;

public enum TransportCommand { Play, Pause, TogglePause, Next, Previous }

public enum TransportOutcome
{
    /// <summary>The command was sent and mpv acknowledged it.</summary>
    Applied,
    /// <summary>The observed player status does not allow it (for example pause while nothing plays); nothing was sent.</summary>
    Ignored,
}

/// <summary>
/// The one entry point for changing what the player does: album replacement (from the selection
/// controller via <see cref="PlayerAlbumApplier"/>) and transport commands (from <see cref="InputControls"/>).
/// Both run through the same operation lock inside <see cref="Mpv"/>, so a transport command can
/// never interleave with a half-built queue, and the observed status it checks is the one in force
/// while it runs. The owner holds no state of its own: wrapping the same <see cref="Mpv"/> twice is
/// harmless. Player status is read from <see cref="Mpv.Status"/>, which is always mpv evidence
/// (Loading/Playing/Paused/Ended/Failed), never inferred from what was last asked.
/// </summary>
public sealed class PlaybackOwner(Mpv mpv)
{
    /// <summary>
    /// Replaces the queue with the album's stream URLs. <paramref name="stillWanted"/> is cancelled when a newer
    /// selection supersedes this one; it is checked immediately before the first mpv command and is not
    /// consulted again once the queue has started to change. See <see cref="Mpv.LoadAlbumAsync"/> for the
    /// failure policy.
    /// </summary>
    public Task ApplyAlbumAsync(IReadOnlyList<string> urls, CancellationToken stillWanted) =>
        mpv.LoadAlbumAsync(urls, stillWanted);

    /// <summary>
    /// Transport entry point for play/pause, next and previous (controller events from <see cref="InputControls"/>).
    /// The policy is described on <see cref="Mpv.TransportAsync"/>: it runs under the same lock as album
    /// replacement, so a control received during a replacement runs after it, against the new album.
    /// With no applied album every command is Ignored.
    /// </summary>
    public async Task<TransportOutcome> TransportAsync(TransportCommand command, CancellationToken cancellationToken) =>
        await mpv.TransportAsync(command, cancellationToken) ? TransportOutcome.Applied : TransportOutcome.Ignored;
}
