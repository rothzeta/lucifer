using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc;

/// <summary>
/// Owns one mpv process (when it started it) and one maintained IPC connection with a single read
/// loop. Commands are serialized; each carries an integer request_id and waits for its own reply.
/// </summary>
public sealed partial class Mpv : IDisposable
{
    private sealed record Pending(TaskCompletionSource<JsonElement> Reply, Action<JsonElement>? OnReply);

    private readonly string _socketPath;
    private readonly MpvTimeouts _timeouts;
    private readonly Socket _socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<long, Pending> _pending = [];
    private readonly List<long> _entries = [];
    private NetworkStream? _stream;
    private Process? _child;
    private bool _ownsSocket;
    private bool _disposed;
    private MpvException? _lost;
    private long _nextId;

    private MpvStatus _status = MpvStatus.Idle;
    private string? _failure;
    private bool _paused;
    private bool _selected;
    private bool _unfiltered;
    private bool _needsReconcile;
    private string[]? _lastUrls;
    private Timer? _streamTimer;
    private long _timerSeq;

    private Mpv(string socketPath, MpvTimeouts timeouts)
    {
        _socketPath = socketPath;
        _timeouts = timeouts;
    }

    public MpvStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Why the status is Failed, from mpv or from a deadline.</summary>
    public string? FailureDetail { get { lock (_gate) return _failure; } }

    /// <summary>
    /// Set once the process or IPC connection is gone for good (as opposed to a failed load, which
    /// leaves the connection usable). The supervisor treats it as the essential player dying.
    /// </summary>
    public MpvException? Lost { get { lock (_gate) return _lost; } }

    public event Action<MpvStatus>? StatusChanged;

    // ---- commands ----------------------------------------------------------

    /// <summary>
    /// Replaces the queue with <paramref name="urls"/> as one operation and leaves playback unpaused.
    /// <paramref name="generation"/> is the caller's "still wanted" token: it is honoured while
    /// waiting for the operation lock and once more immediately before the first command, never
    /// after the queue has started to change. From the first command on, the sequence runs to
    /// completion under the IPC deadlines, so a newer selection cannot leave a half-built queue.
    ///
    /// Failure policy: a rejected first command changed nothing and is rethrown as is. Any later
    /// failure (a rejected append, a lost or timed-out reply, a failed unpause) leaves the queue
    /// unknown, so the status becomes Failed ("album load incomplete"), the player is stopped to
    /// clear the queue, and an <see cref="MpvFailureKind.AlbumIncomplete"/> exception is thrown.
    /// If the stop cannot be confirmed either, the next load must first stop successfully and sends
    /// no playlist command while it cannot; a load is never replayed on top of an unknown queue.
    /// </summary>
    public async Task LoadAlbumAsync(
        IReadOnlyList<string> urls,
        CancellationToken generation)
    {
        if (urls.Count == 0)
            return;

        await _operations.WaitAsync(generation);
        try
        {
            generation.ThrowIfCancellationRequested(); // the generation check, before any mutation
            await ReplaceQueueAsync(urls);
        }
        finally { _operations.Release(); }
    }

    // Caller holds _operations.
    private async Task ReplaceQueueAsync(IReadOnlyList<string> urls)
    {
        if (_needsReconcile)
            await StopAndClearAsync(); // throws without having changed the queue

        var none = CancellationToken.None;
        var mutated = false;
        try
        {
            await SendAsync(["loadfile", urls[0], "replace"], BeginSelection, none);
            mutated = true;
            lock (_gate) _lastUrls = urls.ToArray(); // what "play again" restarts

            foreach (var url in urls.Skip(1))
                await SendAsync(["loadfile", url, "append"], AddEntry, none);

            // A paused previous album would otherwise hold the new one paused.
            await SendAsync(["set_property", "pause", false], null, none);
        }
        catch (MpvException ex) when (Lost is null && (mutated || ex.Kind != MpvFailureKind.CommandError))
        {
            await FailIncompleteAsync(ex);
            throw new MpvException(MpvFailureKind.AlbumIncomplete,
                $"album load incomplete ({ex.Message}); playback stopped", ex);
        }
    }

    // Caller holds _operations.
    private async Task FailIncompleteAsync(MpvException cause)
    {
        Update(() =>
        {
            DisarmStreamDeadline();
            _selected = false; // observations of the abandoned entries no longer count
            _needsReconcile = true;
            return (MpvStatus.Failed, $"album load incomplete: {cause.Message}");
        });

        try { await StopAndClearAsync(); }
        catch (MpvException) { /* stays _needsReconcile; the next load reconciles first */ }
    }

    // Caller holds _operations. mpv's stop also clears the playlist.
    private async Task StopAndClearAsync()
    {
        await SendAsync(["stop"], null, CancellationToken.None);
        lock (_gate) _needsReconcile = false;
    }

    // ---- teardown ----------------------------------------------------------

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timerSeq++;
            _streamTimer?.Dispose();
        }

        _life.Cancel();
        _socket.Dispose();
        FailPending(new MpvException(MpvFailureKind.ConnectionClosed, "mpv player was disposed."));

        if (_child is not null)
            StopChild(_child);

        if (_ownsSocket)
            TryDelete(_socketPath);

        _life.Dispose();
    }
}
