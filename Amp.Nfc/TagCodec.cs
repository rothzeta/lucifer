namespace Amp.Nfc;

public enum TagReadKind { None, Release, Invalid, Conflict }

public readonly record struct TagReadResult(TagReadKind Kind, ReleaseId Release = default, string Message = "");

/// <summary>
/// The tag text contract shared by the writer and the player:
/// <c>amp:album:1:mbid:&lt;release UUID&gt;</c>. The prefix is matched without regard to case; the
/// UUID must be in D format and is normalised on read.
/// </summary>
public static class TagCodec
{
    const string Namespace = "amp:album:";
    const string SupportedVersion = "1";
    const string Kind = "mbid:";

    public static string Format(ReleaseId release)
    {
        if (!release.IsValid)
            throw new ArgumentException("Release identity is not valid.", nameof(release));

        return $"{Namespace}{SupportedVersion}:{Kind}{release}";
    }

    /// <summary>
    /// Search NDEF text for the supported namespace. Unrelated text is skipped. A malformed or
    /// unknown-version record in the namespace, or two different identities, is rejected rather
    /// than resolved by record order.
    /// </summary>
    public static TagReadResult Read(IEnumerable<string> texts)
    {
        var found = new List<ReleaseId>();
        string? problem = null;

        foreach (var text in texts)
        {
            if (!text.StartsWith(Namespace, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = text[Namespace.Length..];
            var colon = rest.IndexOf(':');
            var version = colon < 0 ? rest : rest[..colon];

            if (version != SupportedVersion)
            {
                problem ??= $"Unsupported tag version '{version}'.";
                continue;
            }

            var body = colon < 0 ? "" : rest[(colon + 1)..];

            if (!body.StartsWith(Kind, StringComparison.OrdinalIgnoreCase)
                || body.Length - Kind.Length != 36
                || !Guid.TryParseExact(body[Kind.Length..], "D", out var guid)
                || !ReleaseId.TryFrom(guid, out var release))
            {
                problem ??= "Malformed album identity in tag.";
                continue;
            }

            found.Add(release);
        }

        if (problem is not null)
            return new(TagReadKind.Invalid, Message: problem);

        var distinct = found.Distinct().ToList();

        return distinct.Count switch
        {
            0 => new(TagReadKind.None, Message: "No album identity on tag."),
            1 => new(TagReadKind.Release, distinct[0]),
            _ => new(TagReadKind.Conflict, Message: "Tag holds conflicting album identities."),
        };
    }
}
