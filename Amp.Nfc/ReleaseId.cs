namespace Amp.Nfc;

/// <summary>
/// MusicBrainz release identity. It is neither a reader UID nor a server album ID.
/// The default value is the empty identity and is never valid.
/// </summary>
public readonly record struct ReleaseId
{
    readonly Guid _value;

    ReleaseId(Guid value) => _value = value;

    public bool IsValid => _value != Guid.Empty;

    public Guid Value => _value;

    public static bool TryFrom(Guid value, out ReleaseId release)
    {
        release = new ReleaseId(value);
        return release.IsValid;
    }

    public static ReleaseId From(Guid value) =>
        TryFrom(value, out var release) ? release : throw new FormatException("A release UUID must not be empty.");

    public static bool TryParse(string? text, out ReleaseId release)
    {
        release = default;
        return Guid.TryParse(text?.Trim(), out var guid) && TryFrom(guid, out release);
    }

    public static ReleaseId Parse(string? text) =>
        TryParse(text, out var release) ? release : throw new FormatException("Not a valid non-empty release UUID.");

    /// <summary>Lowercase D format.</summary>
    public override string ToString() => _value.ToString("D");
}
