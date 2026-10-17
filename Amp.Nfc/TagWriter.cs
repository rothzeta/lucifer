using System.Text;
using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;

namespace Amp.Nfc;

public sealed class TagWriteException(string message) : Exception(message);

/// <summary>The tag operations the writer needs. Ordinary programming never locks the tag.</summary>
public interface ITagMedium
{
    bool IsFormatted();
    bool Format();
    bool WriteText(string text);
    bool TryReadTexts(out IReadOnlyList<string> texts);
}

/// <summary>Write the release identity, read it back, and report success only if it matches.</summary>
public static class TagWriter
{
    public static void Write(ITagMedium tag, ReleaseId release, TextWriter output)
    {
        // Validate everything before the first tag request.
        var payload = TagCodec.Format(release);

        output.WriteLine();
        output.WriteLine($"Writing: {payload}");

        if (!tag.IsFormatted())
        {
            output.WriteLine("Formatting tag for NDEF...");

            if (!tag.Format())
                throw new TagWriteException("Could not format tag for NDEF.");
        }

        if (!tag.WriteText(payload))
            throw new TagWriteException("NDEF write failed.");

        if (!tag.TryReadTexts(out var texts))
            throw new TagWriteException("Could not read tag back after writing.");

        var back = TagCodec.Read(texts);

        if (back.Kind != TagReadKind.Release)
            throw new TagWriteException($"Read-back did not yield an album identity: {back.Message}".TrimEnd());

        if (back.Release != release)
            throw new TagWriteException(
                $"Read-back mismatch: wrote {release} but the tag holds {back.Release}.");

        output.WriteLine($"Verified: tag holds {back.Release}.");
    }
}

/// <summary>NTAG/Ultralight tag through the Iot binding.</summary>
sealed class UltralightTagMedium(UltralightCard card) : ITagMedium
{
    public bool IsFormatted() => card.IsFormattedNdef();

    public bool Format() => card.FormatNdef();

    public bool WriteText(string text)
    {
        var message = new NdefMessage();
        message.Records.Add(new TextRecord(text, "en", Encoding.UTF8));
        return card.WriteNdefMessage(message);
    }

    public bool TryReadTexts(out IReadOnlyList<string> texts)
    {
        texts = [];

        if (!card.IsFormattedNdef() || !card.TryReadNdefMessage(out var message))
            return false;

        texts = message.Records
            .Where(TextRecord.IsTextRecord)
            .Select(r => new TextRecord(r).Text)
            .ToList();

        return true;
    }
}
