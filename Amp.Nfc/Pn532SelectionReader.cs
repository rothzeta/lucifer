using Iot.Device.Card.Ultralight;
using Iot.Device.Ndef;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;

namespace Amp.Nfc;

/// <summary>
/// The one owner of PN532 calls while playing. Poll reads only the UID; the full card read runs
/// lazily, once per presentation, when the controller asks. The binding returns null both for no
/// target and for some transport failures, and this adapter does not pretend to tell them apart.
/// </summary>
sealed class Pn532SelectionReader(Pn532 reader) : ISelectionReader
{
    public ReaderObservation Poll()
    {
        try
        {
            var data = reader.ListPassiveTarget(MaxTarget.One, TargetBaudRate.B106kbpsTypeA);
            if (data is null)
                return ReaderObservation.Absent;

            var decoded = reader.TryDecode106kbpsTypeA(data.AsSpan()[1..]);
            if (decoded is null)
                return ReaderObservation.Absent;

            var uid = Convert.ToHexString(decoded.NfcId);

            if (!UltralightCard.IsUltralightCard(decoded.Atqa, decoded.Sak))
                return ReaderObservation.Present(uid,
                    () => new TagReadResult(TagReadKind.Invalid, Message: "Unsupported tag type."));

            return ReaderObservation.Present(uid, () => ReadCard(decoded.TargetNumber, decoded.NfcId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ReaderObservation.Fault(ex.Message);
        }
    }

    TagReadResult ReadCard(byte targetNumber, byte[] nfcId)
    {
        var card = new UltralightCard(reader, targetNumber)
        {
            SerialNumber = nfcId,
            ReselectAfterError = true
        };

        if (!new UltralightTagMedium(card).TryReadTexts(out var texts))
            throw new IOException("NDEF read failed.");

        return TagCodec.Read(texts);
    }
}
