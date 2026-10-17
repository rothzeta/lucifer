using System.Text;
using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

namespace Amp.Nfc;

/// <summary>Maintenance commands that read or program a tag. They own the reader while they run.</summary>
static class TagCommands
{
    public static void Read(LuciferOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        using var pn532 = OpenReader(options, output);
        var tag = WaitForTag(pn532, options.ReaderTimeout, output, cancellationToken);

        PrintTag(tag, output);
        ReadNdef(tag, output);
    }

    public static void Write(LuciferOptions options, Guid release, TextWriter output, CancellationToken cancellationToken)
    {
        using var pn532 = OpenReader(options, output);
        var tag = WaitForTag(pn532, options.ReaderTimeout, output, cancellationToken);

        PrintTag(tag, output);
        WriteNdef(tag, $"amp:album:1:mbid:{release:D}", output);
    }

    static Pn532 OpenReader(LuciferOptions options, TextWriter output)
    {
        var pn532 = new Pn532(options.NfcDevice);

        try
        {
            var firmware = pn532.FirmwareVersion
                ?? throw new InvalidOperationException("PN532 did not answer.");

            output.WriteLine(
                $"PN532 detected. Version={firmware.Version}, " +
                $"Supported={firmware.VersionSupported}");

            return pn532;
        }
        catch
        {
            pn532.Dispose();
            throw;
        }
    }

    static void PrintTag(UltralightCard tag, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"UID:      {Convert.ToHexString(tag.SerialNumber!)}");
        output.WriteLine($"Type:     {tag.UltralightCardType}");
        output.WriteLine($"Capacity: {tag.NdefCapacity} NDEF bytes");
        output.WriteLine($"Pages:    {tag.NumberBlocks}");
    }

    static UltralightCard WaitForTag(
        Pn532 pn532,
        TimeSpan timeout,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        output.WriteLine("Place an NTAG215 on the reader...");

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"No tag was presented within {timeout.TotalSeconds:0} seconds.");

            var raw = pn532.ListPassiveTarget(
                MaxTarget.One,
                TargetBaudRate.B106kbpsTypeA);

            if (raw is null)
            {
                Thread.Sleep(100);
                continue;
            }

            // Byte 0 is the number of detected targets.
            var decoded = pn532.TryDecode106kbpsTypeA(
                raw.AsSpan(1));

            if (decoded is null)
            {
                Thread.Sleep(100);
                continue;
            }

            output.WriteLine(
                $"Detected ISO14443A: " +
                $"ATQA=0x{decoded.Atqa:X4}, " +
                $"SAK=0x{decoded.Sak:X2}, " +
                $"UID={Convert.ToHexString(decoded.NfcId)}");

            if (!UltralightCard.IsUltralightCard(
                    decoded.Atqa,
                    decoded.Sak))
            {
                output.WriteLine(
                    "Tag is not an Ultralight/NTAG-compatible card.");
                Thread.Sleep(500);
                continue;
            }

            return new UltralightCard(
                pn532,
                decoded.TargetNumber)
            {
                SerialNumber = decoded.NfcId,

                // A failed card operation can put the selected tag into
                // an error state. Let the binding reselect it for us.
                ReselectAfterError = true
            };
        }
    }

    static void ReadNdef(UltralightCard tag, TextWriter output)
    {
        output.WriteLine();

        if (!tag.IsFormattedNdef())
        {
            output.WriteLine("Tag does not currently contain an NDEF filesystem.");
            return;
        }

        if (!tag.TryReadNdefMessage(out var message))
        {
            output.WriteLine("NDEF formatted, but message could not be read.");
            return;
        }

        if (message.Records.Count == 0)
        {
            output.WriteLine("NDEF message is empty.");
            return;
        }

        output.WriteLine($"NDEF records: {message.Records.Count}");

        foreach (var record in message.Records)
        {
            if (TextRecord.IsTextRecord(record))
            {
                var text = new TextRecord(record);

                output.WriteLine(
                    $"TEXT [{text.LanguageCode}]: {text.Text}");
            }
            else
            {
                output.WriteLine(
                    $"Record: {record.Header.TypeNameFormat}, {record.Length} bytes");
            }
        }
    }

    static void WriteNdef(UltralightCard tag, string payload, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"Writing: {payload}");

        if (!tag.IsFormattedNdef())
        {
            output.WriteLine("Formatting tag for NDEF...");

            if (!tag.FormatNdef())
                throw new InvalidOperationException(
                    "Could not format tag for NDEF.");
        }

        var message = new NdefMessage();

        message.Records.Add(
            new TextRecord(
                payload,
                "en",
                Encoding.UTF8));

        if (!tag.WriteNdefMessage(message))
            throw new InvalidOperationException(
                "NDEF write failed.");

        output.WriteLine("Write succeeded.");

        if (!tag.TryReadNdefMessage(out var verification))
            throw new InvalidOperationException(
                "Could not read tag back after writing.");

        output.WriteLine("Read-back:");

        foreach (var record in verification.Records)
        {
            if (TextRecord.IsTextRecord(record))
            {
                var text = new TextRecord(record);
                output.WriteLine($"  {text.Text}");
            }
        }
    }
}
