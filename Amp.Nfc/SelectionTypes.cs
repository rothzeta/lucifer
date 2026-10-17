namespace Amp.Nfc;

public enum ObservationKind { Absent, Present, Fault }

/// <summary>
/// One reader poll. Absent is a null detection: the binding returns null both for "no target" and
/// for some transport failures, so the application cannot tell them apart. Fault is only for
/// failures the reader owner positively identified. Present carries the UID; the full card read
/// is deferred until the controller asks for it.
/// </summary>
public sealed record ReaderObservation(
    ObservationKind Kind,
    string Uid = "",
    Func<TagReadResult>? Read = null,
    string Detail = "")
{
    public static ReaderObservation Absent { get; } = new(ObservationKind.Absent);

    public static ReaderObservation Present(string uid, Func<TagReadResult> read) =>
        new(ObservationKind.Present, uid, read);

    public static ReaderObservation Fault(string detail) =>
        new(ObservationKind.Fault, Detail: detail);
}

/// <summary>The single owner of PN532 access. Poll is synchronous and called from one loop.</summary>
public interface ISelectionReader
{
    ReaderObservation Poll();
}

/// <summary>An album ready to replace the player's queue. Preparation must not disturb current audio.</summary>
public interface IPreparedAlbum
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Album-apply boundary (completed in LUC-07). PrepareAsync may throw
/// <see cref="SelectionRejectedException"/> for a permanent rejection; any other exception is transient.
/// </summary>
public interface IAlbumApplier
{
    Task<IPreparedAlbum> PrepareAsync(ReleaseId release, CancellationToken cancellationToken);
}

/// <summary>Permanent rejection of a release (not in the library, no tracks). Not retried.</summary>
public sealed class SelectionRejectedException(string message) : Exception(message);

public sealed record SelectionPolicy(
    TimeSpan AbsenceConfirmation,
    int MaxAttempts = 5,
    TimeSpan? RetryDelay = null,
    TimeSpan? MaxRetryDelay = null)
{
    public TimeSpan InitialRetryDelay => RetryDelay ?? TimeSpan.FromSeconds(1);
    public TimeSpan CappedRetryDelay => MaxRetryDelay ?? TimeSpan.FromSeconds(8);
}
