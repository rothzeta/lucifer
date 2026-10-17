namespace Amp.Nfc;

/// <summary>
/// Narrow bridge from a selected release to the player. Preparation fetches the track list and
/// leaves current audio alone; only CommitAsync replaces the queue. LUC-07 completes the failure
/// handling after queue replacement.
/// </summary>
sealed class PlayerAlbumApplier(
    string cataloguePath,
    Navidrome navidrome,
    Mpv mpv,
    TextWriter output) : IAlbumApplier
{
    public async Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken)
    {
        // The catalogue is read per selection so an edited file applies without a restart.
        var lookup = CatalogueFile.Resolve(cataloguePath, release);

        if (lookup.Album is not { } album)
            throw new SelectionRejectedException($"{lookup.Message} Current album keeps playing.");

        output.WriteLine(lookup.Message);

        List<Track> tracks;
        try
        {
            tracks = await navidrome.GetTracksAsync(album.NavidromeId, cancellationToken);
        }
        catch (StaleMappingException)
        {
            throw new SelectionRejectedException(
                $"Stale mapping: Navidrome has no album {album.NavidromeId} for release {release}; fix it in {cataloguePath}. Current album keeps playing.");
        }
        catch (NavidromeRejectedException ex)
        {
            throw new SelectionRejectedException($"{ex.Message}. Current album keeps playing.");
        }

        if (tracks.Count == 0)
            throw new SelectionRejectedException("Album has no tracks.");

        foreach (var track in tracks)
            output.WriteLine($"  {track.Disc}.{track.Number:00} {track.Title}");

        var auth = navidrome.CreateAuthentication();
        var urls = tracks.Select(track => navidrome.CreateStreamUrl(track.Id, auth)).ToArray();

        return new Prepared(mpv, urls, tracks.Count, output);
    }

    sealed class Prepared(Mpv mpv, string[] urls, int count, TextWriter output) : IPreparedAlbum
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await mpv.LoadAlbumAsync(urls, cancellationToken);
            output.WriteLine($"Playing {count} tracks.");
        }
    }
}
