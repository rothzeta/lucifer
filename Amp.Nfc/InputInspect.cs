namespace Amp.Nfc;

/// <summary>
/// `input inspect`: prints the event type, code and value of every non-sync event from a device, so
/// the operator can discover the codes a controller sends. It only reads the device; it takes no
/// reader lock and needs no server settings.
/// </summary>
static class InputInspect
{
    private static string TypeName(ushort type) => type switch
    {
        0x00 => "EV_SYN",
        0x01 => "EV_KEY",
        0x02 => "EV_REL",
        0x03 => "EV_ABS",
        0x04 => "EV_MSC",
        0x05 => "EV_SW",
        0x11 => "EV_LED",
        0x12 => "EV_SND",
        0x14 => "EV_REP",
        0x15 => "EV_FF",
        _ => $"type={type}",
    };

    public static string Describe(InputEvent ev)
    {
        var line = $"{TypeName(ev.Type)} code={ev.Code} value={ev.Value}";
        if (ev.Type == InputEvent.Key)
        {
            var meaning = ev.Value switch { InputEvent.Press => "press", InputEvent.Release => "release", InputEvent.Repeat => "repeat", _ => null };
            if (meaning is not null)
                line += $" ({meaning})";
        }
        return line;
    }

    public static async Task<int> RunAsync(
        string path, int? count, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        IInputDevice device;
        try
        {
            device = EvdevInputDevice.Open(path);
        }
        catch (InputDeviceException ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            return Cli.Failed;
        }

        using (device)
        {
            output.WriteLine($"Reading input events from {path} ({InputEventFormat.RecordSize(InputEventFormat.Native)}-byte records). Press the controller's buttons; Ctrl+C to stop.");
            var seen = 0;
            try
            {
                await foreach (var ev in device.ReadAsync(cancellationToken))
                {
                    if (ev.Type == 0)
                        continue; // EV_SYN only separates event groups
                    output.WriteLine(Describe(ev));
                    if (++seen >= count)
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (InputDeviceException ex)
            {
                error.WriteLine($"Error: {ex.Message}");
                return Cli.Failed;
            }
        }

        return Cli.Ok;
    }
}
