namespace Amp.Nfc;

/// <summary>
/// Narrow bridge from a selected release to the player. Preparation fetches the track list and
/// leaves current audio alone; only CommitAsync replaces the queue. LUC-07 completes the failure
/// handling after queue replacement.
/// </summary>
sealed class PlayerAlbumApplier(
    Func<string, Album?> findAlbum,
    Navidrome navidrome,
    Mpv mpv,
    TextWriter output) : IAlbumApplier
{
    public async Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken)
    {
        var mbid = release.ToString();

        if (findAlbum(mbid) is not { } album)
            throw new SelectionRejectedException($"Album is not available in this AMP library: {mbid}");

        output.WriteLine($"{album.Artist} — {album.Title}");

        var tracks = await navidrome.GetTracksAsync(album.NavidromeId, cancellationToken);

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
