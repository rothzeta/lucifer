using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

const string NfcDevice = "/dev/ttyUSB0";
const string AudioDevice = "alsa/default:CARD=Pro";
const string SocketPath = "/tmp/amp-mpv.sock";
const string TagPrefix = "amp:album:1:mbid:";

var navidromeUrl = RequireEnvironment("NAVIDROME_URL").TrimEnd('/');
var navidromeUser = RequireEnvironment("NAVIDROME_USER");
var navidromePassword = RequireEnvironment("NAVIDROME_PASSWORD");

var albums = new Dictionary<string, Album>(StringComparer.OrdinalIgnoreCase)
{
    ["a24495e4-eb70-48cd-b0fa-31a606459d26"] =
        new("0lkVdYFhXO6ubrfysEPzTd", "Mastodon", "Crack the Skye (15th anniversary deluxe edition)"),

    ["d5e2d971-e856-4342-ad09-0e6ad1b5241a"] =
        new("0ckDdmbp5fa3ESbkUwskAF", "BAND-MAID", "Brand New MAID")
};

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var http = new HttpClient();
using var pn532 = new Pn532(NfcDevice);
using var mpv = await Mpv.StartAsync(SocketPath, AudioDevice, cancellation.Token);

if (pn532.FirmwareVersion is not FirmwareVersion firmware)
    throw new InvalidOperationException("PN532 did not answer.");

Console.WriteLine($"AMP");
Console.WriteLine($"PN532 {firmware.Version}");
Console.WriteLine("Waiting for album...");

string? currentUid = null;

while (!cancellation.IsCancellationRequested)
{
    try
    {
        var detected = DetectTag(pn532);

        if (detected is null)
        {
            if (currentUid is not null)
            {
                Console.WriteLine("Album removed.");
                currentUid = null;
            }

            await Task.Delay(150, cancellation.Token);
            continue;
        }

        var uid = Convert.ToHexString(detected.Value.Card.SerialNumber!);

        if (uid == currentUid)
        {
            await Task.Delay(150, cancellation.Token);
            continue;
        }

        currentUid = uid;

        Console.WriteLine($"Tag: {uid}");

        var payload = ReadAlbumPayload(detected.Value.Card);

        if (payload is null)
        {
            Console.WriteLine("No AMP album identity found.");
            continue;
        }

        Console.WriteLine($"Tag data: {payload}");

        if (!payload.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Unsupported AMP tag.");
            continue;
        }

        var mbid = payload[TagPrefix.Length..];

        if (!Guid.TryParse(mbid, out _))
        {
            Console.WriteLine($"Invalid MusicBrainz ID: {mbid}");
            continue;
        }

        if (!albums.TryGetValue(mbid, out var album))
        {
            Console.WriteLine($"Album is not available in this AMP library: {mbid}");
            continue;
        }

        Console.WriteLine($"{album.Artist} — {album.Title}");

        var tracks = await GetTracksAsync(album.NavidromeId, cancellation.Token);

        if (tracks.Count == 0)
        {
            Console.WriteLine("Album has no tracks.");
            continue;
        }

        foreach (var track in tracks)
            Console.WriteLine($"  {track.Disc}.{track.Number:00} {track.Title}");

        var auth = CreateAuthentication();

        var urls = tracks
            .Select(track => CreateStreamUrl(track.Id, auth))
            .ToArray();

        await mpv.LoadAlbumAsync(urls, cancellation.Token);

        Console.WriteLine($"Playing {tracks.Count} tracks.");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        await Task.Delay(500, cancellation.Token);
    }
}

Console.WriteLine("Stopped.");

DetectedTag? DetectTag(Pn532 reader)
{
    var data = reader.ListPassiveTarget(
        MaxTarget.One,
        TargetBaudRate.B106kbpsTypeA);

    if (data is null)
        return null;

    var decoded = reader.TryDecode106kbpsTypeA(data.AsSpan()[1..]);

    if (decoded is null)
        return null;

    if (!UltralightCard.IsUltralightCard(decoded.Atqa, decoded.Sak))
        return null;

    var card = new UltralightCard(reader, decoded.TargetNumber)
    {
        SerialNumber = decoded.NfcId,
        ReselectAfterError = true
    };

    return new DetectedTag(card);
}

static string? ReadAlbumPayload(UltralightCard card)
{
    if (!card.IsFormattedNdef())
        return null;

    if (!card.TryReadNdefMessage(out var message))
        return null;

    foreach (var record in message.Records)
    {
        if (!TextRecord.IsTextRecord(record))
            continue;

        return new TextRecord(record).Text;
    }

    return null;
}

async Task<List<Track>> GetTracksAsync(
    string albumId,
    CancellationToken cancellationToken)
{
    var auth = CreateAuthentication();

    var url =
        $"{navidromeUrl}/rest/getAlbum.view" +
        $"?u={Esc(navidromeUser)}" +
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

string CreateStreamUrl(string songId, Authentication auth) =>
    $"{navidromeUrl}/rest/stream.view" +
    $"?u={Esc(navidromeUser)}" +
    $"&t={auth.Token}" +
    $"&s={auth.Salt}" +
    "&v=1.16.1" +
    "&c=amp-v0" +
    "&format=raw" +
    $"&id={Esc(songId)}";

Authentication CreateAuthentication()
{
    var salt = Convert
        .ToHexString(RandomNumberGenerator.GetBytes(8))
        .ToLowerInvariant();

    var input = Encoding.UTF8.GetBytes(
        navidromePassword + salt);

    var token = Convert
        .ToHexString(MD5.HashData(input))
        .ToLowerInvariant();

    return new Authentication(salt, token);
}

static string RequireEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException(
        $"Environment variable {name} is missing.");

static string Esc(string value) =>
    Uri.EscapeDataString(value);

record Album(
    string NavidromeId,
    string Artist,
    string Title);

record Track(
    string Id,
    int Disc,
    int Number,
    string Title);

record Authentication(
    string Salt,
    string Token);

readonly record struct DetectedTag(
    UltralightCard Card);

sealed class Mpv : IDisposable
{
    private readonly Process _process;
    private readonly string _socketPath;

    private Mpv(Process process, string socketPath)
    {
        _process = process;
        _socketPath = socketPath;
    }

    public static async Task<Mpv> StartAsync(
        string socketPath,
        string audioDevice,
        CancellationToken cancellationToken)
    {
        if (File.Exists(socketPath))
            File.Delete(socketPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "mpv",
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--no-video");
        startInfo.ArgumentList.Add($"--audio-device={audioDevice}");
        startInfo.ArgumentList.Add($"--input-ipc-server={socketPath}");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start mpv.");

        for (var i = 0; i < 50; i++)
        {
            if (File.Exists(socketPath))
                return new Mpv(process, socketPath);

            await Task.Delay(
                100,
                cancellationToken);
        }

        process.Kill();

        throw new InvalidOperationException(
            "mpv IPC socket was not created.");
    }

    public async Task LoadAlbumAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            return;

        await CommandAsync(
            ["loadfile", urls[0], "replace"],
            cancellationToken);

        foreach (var url in urls.Skip(1))
        {
            await CommandAsync(
                ["loadfile", url, "append"],
                cancellationToken);
        }
    }

    public Task PauseAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["set_property", "pause", true],
            cancellationToken);

    public Task ResumeAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["set_property", "pause", false],
            cancellationToken);

    public Task NextAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["playlist-next", "force"],
            cancellationToken);

    public Task PreviousAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["playlist-prev", "force"],
            cancellationToken);

    private async Task CommandAsync(
        object[] command,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);

        await socket.ConnectAsync(
            new UnixDomainSocketEndPoint(_socketPath),
            cancellationToken);

        await using var stream =
            new NetworkStream(socket, ownsSocket: false);

        await using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true
            };

        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

        var request = JsonSerializer.Serialize(
            new { command });

        await writer.WriteLineAsync(request);

        var response =
            await reader.ReadLineAsync(cancellationToken);

        if (response is null)
            throw new InvalidOperationException(
                "mpv closed its IPC connection.");

        using var json = JsonDocument.Parse(response);

        var error = json.RootElement
            .GetProperty("error")
            .GetString();

        if (error != "success")
            throw new InvalidOperationException(
                $"mpv: {error}");
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill();

        _process.Dispose();

        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
    }
}
