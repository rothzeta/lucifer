
namespace Amp.Nfc;

public enum MpvStatus { Idle, Loading, Playing, Paused, Ended, Failed }

public enum MpvFailureKind { StartFailed, ProcessExited, Timeout, ConnectionClosed, Malformed, CommandError, LoadFailed, AlbumIncomplete }

public sealed class MpvException(MpvFailureKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public MpvFailureKind Kind { get; } = kind;
}

/// <summary>Deadlines for one IPC exchange and for a stream to start after mpv accepted a load.</summary>
public sealed record MpvTimeouts(TimeSpan Connect, TimeSpan Send, TimeSpan Reply, TimeSpan StreamStart)
{
    public static MpvTimeouts Default { get; } = new(
        Connect: TimeSpan.FromSeconds(2),
        Send: TimeSpan.FromSeconds(2),
        Reply: TimeSpan.FromSeconds(5),
        StreamStart: TimeSpan.FromSeconds(20));
}
