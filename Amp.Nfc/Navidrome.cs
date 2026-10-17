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

        var album = json.RootElement
            .GetProperty("subsonic-response")
            .GetProperty("album");

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
