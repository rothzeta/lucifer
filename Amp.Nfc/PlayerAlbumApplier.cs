namespace Amp.Nfc;

/// <summary>
/// Narrow bridge from a selected release to the player. Preparation fetches the track list and
/// leaves current audio alone and validates every track; only CommitAsync replaces the queue, as
/// one operation through the <see cref="PlaybackOwner"/> (generation check, failure policy and
/// serialization live there and in <see cref="Mpv.LoadAlbumAsync"/>).
/// </summary>
sealed class PlayerAlbumApplier(
    string cataloguePath,
    Navidrome navidrome,
    Mpv mpv,
    TextWriter output) : IAlbumApplier
{
    // The owner is stateless, so this one and the one InputControls uses over the same Mpv share its lock.
    private readonly PlaybackOwner player = new(mpv);

    public async Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken)
    {
        // The catalogue is read per selection so an edited file applies without a restart.
        var lookup = CatalogueFile.Resolve(cataloguePath, release);

        if (lookup.Album is not { } album)
            throw new SelectionRejectedException($"{lookup.Message} Current album keeps playing.");

        output.WriteLine(lookup.Message);

        AlbumTracks result;
        try
        {
            result = await navidrome.GetTracksAsync(album.NavidromeId, cancellationToken);
        }
        catch (StaleMappingException)
        {
            throw new SelectionRejectedException(
                $"Stale mapping: Navidrome has no album {album.NavidromeId} for release {release}; fix it in {cataloguePath}. Current album keeps playing.");
        }
        catch (NoTracksException)
        {
            throw new SelectionRejectedException("Album has no tracks. Current album keeps playing.");
        }
        catch (NavidromeRejectedException ex)
        {
            throw new SelectionRejectedException($"{ex.Message}. Current album keeps playing.");
        }
        // Authentication, transport and malformed-data failures propagate as transient: the
        // controller retries with backoff and logs the credential-free message.

        var tracks = result.Tracks;
        if (result.MetadataIssue is { } issue)
            output.WriteLine($"Album metadata issue: {issue}.");

        foreach (var track in tracks)
            output.WriteLine(result.OrderedByPosition
                ? $"  {track.Disc}.{track.Number:00} {LogText.Clean(track.Title)}"
                : $"  - {LogText.Clean(track.Title)}");

        // Everything is checked before the queue is touched. Messages never include the URLs.
        if (tracks.Select(track => track.Id).Distinct().Count() != tracks.Count)
            throw new SelectionRejectedException("Album lists the same track twice. Current album keeps playing.");

        var auth = navidrome.CreateAuthentication();
        var urls = tracks.Select(track => navidrome.CreateStreamUrl(track.Id, auth)).ToArray();

        return new Prepared(player, urls, tracks.Count, output);
    }

    sealed class Prepared(PlaybackOwner player, string[] urls, int count, TextWriter output) : IPreparedAlbum
    {
        // The token is the controller's "still the selection" signal; the owner checks it right
        // before the first mpv command. "Playing" here means the queue was accepted: whether audio
        // starts is reported separately through the player status.
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await player.ApplyAlbumAsync(urls, cancellationToken);
            output.WriteLine($"Queued {count} tracks; waiting for mpv to start playback.");
        }
    }
}
