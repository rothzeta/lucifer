namespace Amp.Nfc;

/// <summary>
/// Orders songs by (disc, track). A position is the pair; track 1 on two discs is not a duplicate.
/// A disc number missing from every song means a single-disc album and counts as disc 1. If any
/// song has an absent or invalid position, or two songs share a pair, the server order is kept
/// unchanged and an issue is reported; nothing is moved or dropped.
/// </summary>
static class NavidromeOrdering
{
    /// <summary>Raw tags as received: null means absent or invalid (non-integer or below 1).</summary>
    public sealed record Song(string Id, string Title, int? Disc, int? Number);

    public static AlbumTracks Order(IReadOnlyList<Song> songs)
    {
        var anyDisc = songs.Any(s => s.Disc is not null);
        int Disc(Song s) => s.Disc ?? (anyDisc ? 0 : 1);

        var tracks = songs
            .Select(s => new Track(s.Id, Disc(s), s.Number ?? 0, s.Title))
            .ToList();

        var missing = songs.Count(s => Disc(s) == 0 || s.Number is null);
        if (missing > 0)
            return Fallback(tracks, $"{missing} of {songs.Count} tracks lack a valid disc or track number");

        var duplicate = tracks
            .GroupBy(t => (t.Disc, t.Number))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Fallback(tracks, $"disc {duplicate.Key.Disc} track {duplicate.Key.Number} appears more than once");

        var ordered = tracks.OrderBy(t => t.Disc).ThenBy(t => t.Number).ToList();
        return new AlbumTracks(ordered, true, null);
    }

    private static AlbumTracks Fallback(List<Track> tracks, string issue) =>
        new(tracks, false, $"{issue}; using the server's album order");
}
