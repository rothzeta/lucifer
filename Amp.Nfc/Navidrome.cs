using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc;

record Track(
    string Id,
    int Disc,
    int Number,
    string Title);

record Authentication(
    string Salt,
    string Token);

sealed class Navidrome(HttpClient http, LuciferOptions options)
{
    // Subsonic error codes 40-44 are credential/authentication problems; 50 is "not authorized".
    private static bool IsAuthenticationFailure(int code) => code is >= 40 and <= 44 or 50;

    /// <summary>
    /// Resolves an album to its tracks. Throws <see cref="NavidromeException"/> subclasses only
    /// (apart from cancellation by <paramref name="cancellationToken"/>); see NavidromeOutcomes.cs.
    /// </summary>
    public async Task<AlbumTracks> GetTracksAsync(
        string albumId,
        CancellationToken cancellationToken)
    {
        var auth = CreateAuthentication();

        var url =
            $"{options.NavidromeUrl}/rest/getAlbum.view" +
            $"?u={Esc(options.NavidromeUser)}" +
            $"&t={auth.Token}" +
            $"&s={auth.Salt}" +
            "&v=1.16.1" +
            "&c=amp-v0" +
            "&f=json" +
            $"&id={Esc(albumId)}";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.HttpTimeout);

        string body;
        try
        {
            using var response = await http.GetAsync(url, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new NavidromeTransportException($"Navidrome answered HTTP {(int)response.StatusCode}");
            body = await response.Content.ReadAsStringAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new NavidromeTransportException("Navidrome did not answer in time", ex);
        }
        catch (HttpRequestException ex)
        {
            // The exception text is not forwarded: keep authenticated URLs out of logs.
            throw new NavidromeTransportException($"Navidrome could not be reached ({ex.GetType().Name})", ex);
        }

        return Parse(body, albumId);
    }

    private static AlbumTracks Parse(string body, string albumId)
    {
        try
        {
            using var json = JsonDocument.Parse(body);

            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("subsonic-response", out var reply)
                || reply.ValueKind != JsonValueKind.Object)
                throw Malformed("no subsonic-response envelope");

            // Status and error come before any endpoint data.
            var status = reply.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (status == "failed")
                throw FailedStatus(reply, albumId);
            if (status != "ok")
                throw Malformed("envelope status is missing or unknown");

            if (!reply.TryGetProperty("album", out var album) || album.ValueKind != JsonValueKind.Object)
                throw Malformed("no album in an ok response");

            var songs = new List<NavidromeOrdering.Song>();
            if (album.TryGetProperty("song", out var list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                    throw Malformed("album song list is not an array");
                foreach (var song in list.EnumerateArray())
                    songs.Add(ReadSong(song));
            }

            if (songs.Count == 0)
                throw new NoTracksException(albumId);

            return NavidromeOrdering.Order(songs);
        }
        catch (JsonException ex)
        {
            throw new NavidromeMalformedException("Navidrome sent unparseable JSON", ex);
        }
    }

    private static NavidromeException FailedStatus(JsonElement reply, string albumId)
    {
        if (!reply.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var c)
            || c.ValueKind != JsonValueKind.Number
            || !c.TryGetInt32(out var code))
            return Malformed("failed status without an error code");

        if (code == 70)
            return new StaleMappingException(albumId);
        if (IsAuthenticationFailure(code))
            return new NavidromeAuthenticationException(code);
        return new NavidromeRejectedException($"Navidrome refused the request (error {code})");
    }

    private static NavidromeOrdering.Song ReadSong(JsonElement song)
    {
        if (song.ValueKind != JsonValueKind.Object
            || !song.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(id.GetString()))
            throw Malformed("a song has no id");

        var title = song.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!
            : id.GetString()!;

        return new(id.GetString()!, title, Position(song, "discNumber"), Position(song, "track"));
    }

    // Absent, non-integer, or below 1: no usable position.
    private static int? Position(JsonElement song, string name) =>
        song.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 1
            ? n
            : null;

    private static NavidromeMalformedException Malformed(string what) =>
        new($"Navidrome sent malformed data: {what}");

    public string CreateStreamUrl(string songId, Authentication auth) =>
        $"{options.NavidromeUrl}/rest/stream.view" +
        $"?u={Esc(options.NavidromeUser)}" +
        $"&t={auth.Token}" +
        $"&s={auth.Salt}" +
        "&v=1.16.1" +
        "&c=amp-v0" +
        "&format=raw" +
        $"&id={Esc(songId)}";

    public Authentication CreateAuthentication()
    {
        var salt = Convert
            .ToHexString(RandomNumberGenerator.GetBytes(8))
            .ToLowerInvariant();

        var input = Encoding.UTF8.GetBytes(
            options.NavidromePassword + salt);

        var token = Convert
            .ToHexString(MD5.HashData(input))
            .ToLowerInvariant();

        return new Authentication(salt, token);
    }

    private static string Esc(string value) =>
        Uri.EscapeDataString(value);
}
