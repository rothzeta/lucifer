using System.Text;
using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

const string Device = "/dev/ttyUSB0";

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "read";

using var pn532 = new Pn532(Device);

var firmware = pn532.FirmwareVersion
    ?? throw new InvalidOperationException("PN532 did not answer.");

Console.WriteLine(
    $"PN532 detected. Version={firmware.Version}, " +
    $"Supported={firmware.VersionSupported}");

Console.WriteLine("Place an NTAG215 on the reader...");

var tag = WaitForTag(pn532);

Console.WriteLine();
Console.WriteLine($"UID:      {Convert.ToHexString(tag.SerialNumber!)}");
Console.WriteLine($"Type:     {tag.UltralightCardType}");
Console.WriteLine($"Capacity: {tag.NdefCapacity} NDEF bytes");
Console.WriteLine($"Pages:    {tag.NumberBlocks}");

switch (command)
{
    case "read":
        ReadNdef(tag);
        break;

    case "write":
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: dotnet run -- write \"amp:album:1:test\"");
            return;
        }

        WriteNdef(tag, args[1]);
        break;

    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Commands: read, write");
        break;
}

static UltralightCard WaitForTag(Pn532 pn532)
{
    while (true)
    {
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

        Console.WriteLine(
            $"Detected ISO14443A: " +
            $"ATQA=0x{decoded.Atqa:X4}, " +
            $"SAK=0x{decoded.Sak:X2}, " +
            $"UID={Convert.ToHexString(decoded.NfcId)}");

        if (!UltralightCard.IsUltralightCard(
                decoded.Atqa,
                decoded.Sak))
        {
            Console.WriteLine(
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

static void ReadNdef(UltralightCard tag)
{
    Console.WriteLine();

    if (!tag.IsFormattedNdef())
    {
        Console.WriteLine("Tag does not currently contain an NDEF filesystem.");
        return;
    }

    if (!tag.TryReadNdefMessage(out var message))
    {
        Console.WriteLine("NDEF formatted, but message could not be read.");
        return;
    }

    if (message.Records.Count == 0)
    {
        Console.WriteLine("NDEF message is empty.");
        return;
    }

    Console.WriteLine($"NDEF records: {message.Records.Count}");

    foreach (var record in message.Records)
    {
        if (TextRecord.IsTextRecord(record))
        {
            var text = new TextRecord(record);

            Console.WriteLine(
                $"TEXT [{text.LanguageCode}]: {text.Text}");
        }
        else
        {
            Console.WriteLine(
                $"Record: {record.TypeNameFormat}, {record.Length} bytes");
        }
    }
}

static void WriteNdef(
    UltralightCard tag,
    string payload)
{
    Console.WriteLine();
    Console.WriteLine($"Writing: {payload}");

    if (!tag.IsFormattedNdef())
    {
        Console.WriteLine("Formatting tag for NDEF...");

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

    Console.WriteLine("Write succeeded.");

    if (!tag.TryReadNdefMessage(out var verification))
        throw new InvalidOperationException(
            "Could not read tag back after writing.");

    Console.WriteLine("Read-back:");

    foreach (var record in verification.Records)
    {
        if (TextRecord.IsTextRecord(record))
        {
            var text = new TextRecord(record);
            Console.WriteLine($"  {text.Text}");
        }
    }
}
