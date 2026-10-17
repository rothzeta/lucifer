namespace Amp.Nfc;

/// <summary>
/// Source-boundary outcomes of resolving a Navidrome album. Messages never contain the request URL,
/// token, salt, or password. <see cref="NavidromeRejectedException"/> is permanent (retrying the same
/// request cannot help); every other <see cref="NavidromeException"/> is transient.
/// </summary>
class NavidromeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The server answered and refused the request; retrying the same request will not help.</summary>
class NavidromeRejectedException(string message) : NavidromeException(message);

/// <summary>The server says the album ID does not exist: the catalogue mapping is stale.</summary>
sealed class StaleMappingException(string albumId)
    : NavidromeRejectedException($"Navidrome has no album with ID {albumId}")
{
    public string AlbumId { get; } = albumId;
}

/// <summary>The album exists but lists no playable tracks.</summary>
sealed class NoTracksException(string albumId)
    : NavidromeRejectedException($"Navidrome album {albumId} has no tracks");

/// <summary>
/// The server rejected the credentials (or the account's rights). Transient from the controller's
/// view: the operator can fix the account without re-presenting the tag.
/// </summary>
sealed class NavidromeAuthenticationException(int code)
    : NavidromeException($"Navidrome rejected the credentials (error {code}); check NAVIDROME_USER and NAVIDROME_PASSWORD")
{
    public int Code { get; } = code;
}

/// <summary>No usable HTTP answer: connection failure, deadline, or a non-success HTTP status.</summary>
sealed class NavidromeTransportException(string message, Exception? inner = null) : NavidromeException(message, inner);

/// <summary>The answer was not a valid subsonic-response envelope or album payload.</summary>
sealed class NavidromeMalformedException(string message, Exception? inner = null) : NavidromeException(message, inner);

/// <summary>
/// One resolved album. <see cref="Tracks"/> holds each returned song exactly once.
/// <see cref="OrderedByPosition"/> is true only when every song had a valid, unique (disc, track)
/// pair and the list is sorted by it; otherwise the list is in server order and
/// <see cref="MetadataIssue"/> says why.
/// </summary>
sealed record AlbumTracks(IReadOnlyList<Track> Tracks, bool OrderedByPosition, string? MetadataIssue);
