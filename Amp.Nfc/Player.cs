using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

namespace Amp.Nfc;

static class Player
{
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

                var lookup = CatalogueFile.Resolve(options.CataloguePath, identity.Release);

                if (lookup.Album is not { } album)
                {
                    output.WriteLine($"{lookup.Message} Current album keeps playing.");
                    continue;
                }

                output.WriteLine(lookup.Message);

                List<Track> tracks;
                try
                {
                    tracks = await navidrome.GetTracksAsync(album.NavidromeId, cancellationToken);
                }
                catch (StaleMappingException)
                {
                    output.WriteLine($"Stale mapping: Navidrome has no album {album.NavidromeId} for release {mbid}; fix it in {options.CataloguePath}. Current album keeps playing.");
                    continue;
                }

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
