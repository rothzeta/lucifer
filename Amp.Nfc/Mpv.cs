using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc;

public enum MpvStatus { Idle, Loading, Playing, Paused, Ended, Failed }

public enum MpvFailureKind { StartFailed, ProcessExited, Timeout, ConnectionClosed, Malformed, CommandError, LoadFailed }

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

/// <summary>
/// Owns one mpv process (when it started it) and one maintained IPC connection with a single read
/// loop. Commands are serialized; each carries an integer request_id and waits for its own reply.
/// </summary>
public sealed class Mpv : IDisposable
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

    /// <summary>Connect to an mpv that is already serving <paramref name="socketPath"/>; the caller owns it.</summary>
    public static Task<Mpv> ConnectAsync(
        string socketPath,
        MpvTimeouts timeouts,
        TimeSpan startTimeout,
        CancellationToken cancellationToken) =>
        OpenAsync(socketPath, timeouts, startTimeout, null, null, cancellationToken);

    /// <summary>
    /// Start mpv and own it from the moment it exists. Ready means its socket accepted a connection
    /// and answered a harmless request. On failure or cancellation the child is stopped and only a
    /// socket this instance created is removed.
    /// </summary>
    public static async Task<Mpv> StartAsync(
        string mpvPath,
        string socketPath,
        string audioDevice,
        TimeSpan startTimeout,
        CancellationToken cancellationToken,
        MpvTimeouts? timeouts = null)
    {
        timeouts ??= MpvTimeouts.Default;
        RefuseLiveSocketOrRemoveStale(socketPath, timeouts);

        var startInfo = new ProcessStartInfo { FileName = mpvPath, UseShellExecute = false };
        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--no-video");
        startInfo.ArgumentList.Add($"--audio-device={audioDevice}");
        startInfo.ArgumentList.Add($"--input-ipc-server={socketPath}");

        Process child;
        try
        {
            child = Process.Start(startInfo)
                ?? throw new MpvException(MpvFailureKind.StartFailed, "Could not start mpv.");
        }
        catch (Exception ex) when (ex is not MpvException)
        {
            throw new MpvException(MpvFailureKind.StartFailed, $"Could not start mpv: {ex.Message}", ex);
        }

        var ownership = new SocketOwnership();
        try
        {
            return await OpenAsync(socketPath, timeouts, startTimeout, child, ownership, cancellationToken);
        }
        catch
        {
            StopChild(child);
            if (ownership.Owned)
                TryDelete(socketPath);
            throw;
        }
    }

    private sealed class SocketOwnership { public bool Owned; }

    // Only a socket that nothing answers on is stale. One that answers belongs to someone else.
    private static void RefuseLiveSocketOrRemoveStale(string socketPath, MpvTimeouts timeouts)
    {
        if (!File.Exists(socketPath))
            return;

        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath))
                .WaitAsync(timeouts.Connect).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            TryDelete(socketPath);
            return;
        }

        throw new MpvException(MpvFailureKind.StartFailed,
            $"{socketPath} is served by another process; not starting mpv against it.");
    }

    private static async Task<Mpv> OpenAsync(
        string socketPath,
        MpvTimeouts timeouts,
        TimeSpan startTimeout,
        Process? child,
        SocketOwnership? ownership,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        Exception? last = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (child is { HasExited: true })
                throw new MpvException(MpvFailureKind.ProcessExited,
                    $"mpv exited with code {child.ExitCode} before its IPC was ready.");

            if (ownership is not null && !ownership.Owned && File.Exists(socketPath))
                ownership.Owned = true;

            var remaining = startTimeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new MpvException(MpvFailureKind.StartFailed,
                    $"mpv IPC was not ready within {startTimeout.TotalSeconds:0.##}s.", last);

            var attempt = new Mpv(socketPath, timeouts);
            try
            {
                await attempt.ConnectAndVerifyAsync(Min(timeouts.Connect, remaining), cancellationToken);
                attempt.Adopt(child, ownership?.Owned ?? false);
                return attempt;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                attempt.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                attempt.Dispose();
                last = ex;
            }

            var pause = Min(TimeSpan.FromMilliseconds(50), startTimeout - clock.Elapsed);
            if (pause > TimeSpan.Zero)
                await Task.Delay(pause, cancellationToken);
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private async Task ConnectAndVerifyAsync(TimeSpan connectBudget, CancellationToken cancellationToken)
    {
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(connectBudget);
            try
            {
                await _socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), connect.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MpvException(MpvFailureKind.Timeout, "Timed out connecting to the mpv socket.");
            }
        }

        _stream = new NetworkStream(_socket, ownsSocket: false);
        _ = ReadLoopAsync();

        await _operations.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(["get_property", "mpv-version"], null, cancellationToken);
            await SendAsync(["observe_property", 1, "pause"], null, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    private void Adopt(Process? child, bool ownsSocket)
    {
        _ownsSocket = ownsSocket;
        if (child is null)
            return;

        _child = child;
        child.EnableRaisingEvents = true;
        child.Exited += (_, _) => Lose(new MpvException(MpvFailureKind.ProcessExited, ExitMessage(child)));
        if (child.HasExited)
            Lose(new MpvException(MpvFailureKind.ProcessExited, ExitMessage(child)));
    }

    private static string ExitMessage(Process child)
    {
        try { return $"mpv exited with code {child.ExitCode}."; }
        catch (InvalidOperationException) { return "mpv exited."; }
    }

    // ---- commands ----------------------------------------------------------

    public async Task LoadAlbumAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            return;

        await _operations.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(["loadfile", urls[0], "replace"], BeginSelection, cancellationToken);

            foreach (var url in urls.Skip(1))
                await SendAsync(["loadfile", url, "append"], AddEntry, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    public Task PauseAsync(CancellationToken cancellationToken) =>
        CommandAsync(["set_property", "pause", true], cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken) =>
        CommandAsync(["set_property", "pause", false], cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken) =>
        CommandAsync(["playlist-next", "force"], cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken) =>
        CommandAsync(["playlist-prev", "force"], cancellationToken);

    private async Task CommandAsync(object[] command, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken);
        try { await SendAsync(command, null, cancellationToken); }
        finally { _operations.Release(); }
    }

    // The caller holds _operations. onReply runs on the read loop before the caller resumes, so
    // entry ids are known before any later event for that entry is looked at.
    private async Task<JsonElement> SendAsync(object[] command, Action<JsonElement>? onReply, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_lost is not null)
                throw _lost;
            _pending[id] = new Pending(reply, onReply);
        }

        try
        {
            var line = JsonSerializer.Serialize(new { command, request_id = id }) + "\n";

            using (var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                send.CancelAfter(_timeouts.Send);
                try
                {
                    await _stream!.WriteAsync(Encoding.UTF8.GetBytes(line), send.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new MpvException(MpvFailureKind.Timeout, "Timed out sending a command to mpv.");
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    var lost = new MpvException(MpvFailureKind.ConnectionClosed, $"mpv connection failed: {ex.Message}", ex);
                    Lose(lost);
                    throw lost;
                }
            }

            try
            {
                return await reply.Task.WaitAsync(_timeouts.Reply, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new MpvException(MpvFailureKind.Timeout,
                    $"mpv did not reply to {command[0]} within {_timeouts.Reply.TotalSeconds:0.##}s.");
            }
        }
        finally
        {
            lock (_gate) _pending.Remove(id);
        }
    }

    // ---- read loop ---------------------------------------------------------

    private async Task ReadLoopAsync()
    {
        try
        {
            using var reader = new StreamReader(_stream!, Encoding.UTF8, leaveOpen: true);
            while (await reader.ReadLineAsync(_life.Token) is { } line)
                Handle(line);

            Lose(new MpvException(MpvFailureKind.ConnectionClosed, "mpv closed its IPC connection."));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Lose(new MpvException(MpvFailureKind.ConnectionClosed, $"mpv connection failed: {ex.Message}", ex));
        }
    }

    private void Handle(string line)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(line);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            FailPending(new MpvException(MpvFailureKind.Malformed, "mpv sent a line that is not JSON."));
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            FailPending(new MpvException(MpvFailureKind.Malformed, "mpv sent JSON that is not an object."));
            return;
        }

        if (root.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String)
        {
            HandleEvent(name.GetString()!, root);
            return;
        }

        if (!root.TryGetProperty("request_id", out var idElement) || !idElement.TryGetInt64(out var id))
            return; // not ours to interpret

        Pending? pending;
        lock (_gate) _pending.TryGetValue(id, out pending);
        if (pending is null)
            return; // a reply for nobody waiting, for example after a timeout

        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String)
        {
            pending.Reply.TrySetException(new MpvException(MpvFailureKind.Malformed, "mpv reply has no error field."));
        }
        else if (error.GetString() != "success")
        {
            pending.Reply.TrySetException(new MpvException(MpvFailureKind.CommandError, $"mpv: {error.GetString()}"));
        }
        else
        {
            pending.OnReply?.Invoke(root);
            pending.Reply.TrySetResult(root);
        }
    }

    private void FailPending(MpvException ex)
    {
        Pending[] pending;
        lock (_gate) pending = _pending.Values.ToArray();
        foreach (var p in pending)
            p.Reply.TrySetException(ex);
    }

    // The connection or process is gone. Everything pending fails now; later commands fail fast.
    private void Lose(MpvException ex)
    {
        Pending[] pending;
        bool disposed;
        lock (_gate)
        {
            _lost ??= ex;
            pending = _pending.Values.ToArray();
            disposed = _disposed;
        }

        if (!disposed)
            Update(() => (MpvStatus.Failed, ex.Message));

        foreach (var p in pending)
            p.Reply.TrySetException(ex);
    }

    // ---- observations ------------------------------------------------------

    private void BeginSelection(JsonElement reply)
    {
        Update(() =>
        {
            _entries.Clear();
            _selected = true;
            _unfiltered = false;
            _paused = false;
            AddEntryLocked(reply);
            ArmStreamDeadline();
            return (MpvStatus.Loading, null);
        });
    }

    private void AddEntry(JsonElement reply)
    {
        lock (_gate) AddEntryLocked(reply);
    }

    private void AddEntryLocked(JsonElement reply)
    {
        if (reply.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("playlist_entry_id", out var id) && id.TryGetInt64(out var entry))
            _entries.Add(entry);
        else
            _unfiltered = true; // mpv did not say which entry; accept observations unfiltered
    }

    private void HandleEvent(string name, JsonElement ev)
    {
        long? entry = ev.TryGetProperty("playlist_entry_id", out var e) && e.TryGetInt64(out var v) ? v : null;

        Update(() =>
        {
            if (!_selected || (entry is { } id && !_unfiltered && !_entries.Contains(id)))
                return null; // not about the current selection

            switch (name)
            {
                case "start-file":
                    ArmStreamDeadline();
                    return (MpvStatus.Loading, null);

                case "file-loaded":
                    DisarmStreamDeadline();
                    return (_paused ? MpvStatus.Paused : MpvStatus.Playing, null);

                case "end-file":
                    var reason = ev.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    var last = entry is { } x && _entries.Count > 0 && _entries[^1] == x;
                    switch (reason)
                    {
                        case "error":
                            DisarmStreamDeadline();
                            var why = ev.TryGetProperty("file_error", out var fe) ? fe.GetString() : null;
                            return (MpvStatus.Failed, $"load failed: {why ?? "mpv reported an error"}");
                        case "eof" when last:
                            DisarmStreamDeadline();
                            return (MpvStatus.Ended, null);
                        case "stop" or "quit" when last:
                            DisarmStreamDeadline();
                            return (MpvStatus.Idle, null);
                    }
                    return null;

                case "property-change":
                    if (ev.TryGetProperty("name", out var prop) && prop.GetString() == "pause"
                        && ev.TryGetProperty("data", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        _paused = d.GetBoolean();
                        if (_status == MpvStatus.Playing && _paused) return (MpvStatus.Paused, null);
                        if (_status == MpvStatus.Paused && !_paused) return (MpvStatus.Playing, null);
                    }
                    return null;
            }

            return null;
        });
    }

    // Called with the lock held.
    private void ArmStreamDeadline()
    {
        var seq = ++_timerSeq;
        _streamTimer?.Dispose();
        _streamTimer = new Timer(_ => OnStreamDeadline(seq), null, _timeouts.StreamStart, Timeout.InfiniteTimeSpan);
    }

    private void DisarmStreamDeadline()
    {
        _timerSeq++;
        _streamTimer?.Dispose();
        _streamTimer = null;
    }

    private void OnStreamDeadline(long seq) =>
        Update(() => seq == _timerSeq && _status == MpvStatus.Loading
            ? (MpvStatus.Failed,
                $"load failed: mpv accepted the playlist but nothing started within {_timeouts.StreamStart.TotalSeconds:0.##}s")
            : null);

    // Decide and apply a transition under the lock; raise the event after releasing it.
    private void Update(Func<(MpvStatus Status, string? Detail)?> decide)
    {
        MpvStatus? raised = null;
        lock (_gate)
        {
            if (_disposed || decide() is not { } next)
                return;
            _failure = next.Status == MpvStatus.Failed ? next.Detail : null;
            if (next.Status != _status)
            {
                _status = next.Status;
                raised = next.Status;
            }
        }

        if (raised is { } s)
            StatusChanged?.Invoke(s);
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
    }

    private static void StopChild(Process child)
    {
        try
        {
            if (!child.HasExited)
            {
                child.Kill();
                child.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException) { }
        finally { child.Dispose(); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
