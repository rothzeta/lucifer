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

/// <summary>The server answered and refused the request; retrying the same request will not help.</summary>
class NavidromeRejectedException(string message) : Exception(message);

/// <summary>The server says the album ID does not exist: the catalogue mapping is stale.</summary>
sealed class StaleMappingException(string albumId)
    : NavidromeRejectedException($"Navidrome has no album with ID {albumId}")
{
    public string AlbumId { get; } = albumId;
}

sealed class Navidrome(HttpClient http, LuciferOptions options)
{
    public async Task<List<Track>> GetTracksAsync(
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

        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var json =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        var reply = json.RootElement.GetProperty("subsonic-response");

        if (reply.TryGetProperty("status", out var status) && status.GetString() == "failed")
        {
            var error = reply.TryGetProperty("error", out var e) ? e : default;
            var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            if (code == 70)
                throw new StaleMappingException(albumId);
            throw new NavidromeRejectedException($"Navidrome refused the request (error {code})");
        }

        var album = reply.GetProperty("album");

        return album
            .GetProperty("song")
            .EnumerateArray()
            .Select(song => new Track(
                song.GetProperty("id").GetString()!,
                song.TryGetProperty("discNumber", out var disc)
                    ? disc.GetInt32()
                    : 1,
                song.TryGetProperty("track", out var track)
                    ? track.GetInt32()
                    : 0,
                song.GetProperty("title").GetString()!))
            .OrderBy(track => track.Disc)
            .ThenBy(track => track.Number)
            .ToList();
    }

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
