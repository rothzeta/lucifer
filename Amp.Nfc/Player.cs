using Iot.Device.Pn532;

namespace Amp.Nfc;

static class Player
{
    public static async Task RunAsync(
        LuciferOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = options.HttpTimeout };
        var navidrome = new Navidrome(http, options);

        using var pn532 = new Pn532(options.NfcDevice);

        if (pn532.FirmwareVersion is not FirmwareVersion firmware)
            throw new InvalidOperationException("PN532 did not answer.");

        output.WriteLine("AMP");
        output.WriteLine($"PN532 {firmware.Version}");

        var reader = new Pn532SelectionReader(pn532);
        await PlaySession.RunAsync(
            options,
            reader,
            reader,
            mpv => new PlayerAlbumApplier(options.CataloguePath, navidrome, mpv, output),
            new SupervisorPolicy(),
            output,
            error,
            cancellationToken);
    }
}
