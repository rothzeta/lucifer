using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

namespace Amp.Nfc;

record Album(
    string NavidromeId,
    string Artist,
    string Title);

static class Player
{
    static readonly Dictionary<string, Album> Albums = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a24495e4-eb70-48cd-b0fa-31a606459d26"] =
            new("0lkVdYFhXO6ubrfysEPzTd", "Mastodon", "Crack the Skye (15th anniversary deluxe edition)"),

        ["d5e2d971-e856-4342-ad09-0e6ad1b5241a"] =
            new("0ckDdmbp5fa3ESbkUwskAF", "BAND-MAID", "Brand New MAID")
    };

    public static async Task RunAsync(
        LuciferOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.IpcDirectory);

        using var http = new HttpClient { Timeout = options.HttpTimeout };
        var navidrome = new Navidrome(http, options);

        using var pn532 = new Pn532(options.NfcDevice);
        using var mpv = await Mpv.StartAsync(
            options.MpvPath,
            options.SocketPath,
            options.AudioDevice,
            options.MpvStartTimeout,
            cancellationToken);

        if (pn532.FirmwareVersion is not FirmwareVersion firmware)
            throw new InvalidOperationException("PN532 did not answer.");

        output.WriteLine("AMP");
        output.WriteLine($"PN532 {firmware.Version}");
        output.WriteLine("Waiting for album...");

        string? currentUid = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var card = DetectTag(pn532);

                if (card is null)
                {
                    if (currentUid is not null)
                    {
                        output.WriteLine("Album removed.");
                        currentUid = null;
                    }

                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                var uid = Convert.ToHexString(card.SerialNumber!);

                if (uid == currentUid)
                {
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                currentUid = uid;

                output.WriteLine($"Tag: {uid}");

                var identity = TagCodec.Read(ReadTexts(card));

                if (identity.Kind != TagReadKind.Release)
                {
                    output.WriteLine(identity.Message);
                    continue;
                }

                var mbid = identity.Release.ToString();

                if (!Albums.TryGetValue(mbid, out var album))
                {
                    output.WriteLine($"Album is not available in this AMP library: {mbid}");
                    continue;
                }

                output.WriteLine($"{album.Artist} — {album.Title}");

                var tracks = await navidrome.GetTracksAsync(album.NavidromeId, cancellationToken);

                if (tracks.Count == 0)
                {
                    output.WriteLine("Album has no tracks.");
                    continue;
                }

                foreach (var track in tracks)
                    output.WriteLine($"  {track.Disc}.{track.Number:00} {track.Title}");

                var auth = navidrome.CreateAuthentication();

                var urls = tracks
                    .Select(track => navidrome.CreateStreamUrl(track.Id, auth))
                    .ToArray();

                await mpv.LoadAlbumAsync(urls, cancellationToken);

                output.WriteLine($"Playing {tracks.Count} tracks.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                error.WriteLine($"Error: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }

        output.WriteLine("Stopped.");
    }

    static UltralightCard? DetectTag(Pn532 reader)
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

        return new UltralightCard(reader, decoded.TargetNumber)
        {
            SerialNumber = decoded.NfcId,
            ReselectAfterError = true
        };
    }

    static IEnumerable<string> ReadTexts(UltralightCard card) =>
        new UltralightTagMedium(card).TryReadTexts(out var texts) ? texts : [];
}
