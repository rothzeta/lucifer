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

        var controller = new SelectionController(
            new Pn532SelectionReader(pn532),
            new PlayerAlbumApplier(
                options.CataloguePath, navidrome, mpv, output),
            new SelectionPolicy(options.AbsenceConfirmation),
            TimeProvider.System,
            output.WriteLine);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await controller.StepAsync(cancellationToken);
                await Task.Delay(150, cancellationToken);
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

        controller.Stop();
        await controller.WhenIdleAsync();

        output.WriteLine("Stopped.");
    }
}
