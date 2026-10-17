using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc;

// IPC transport: command send, read loop, reply matching and connection loss.
public sealed partial class Mpv : IDisposable
{
    /// <summary>One raw command under the operation lock. Used by tests to exercise the IPC layer; the transport policy is <see cref="TransportAsync"/>.</summary>
    internal async Task CommandAsync(object[] command, CancellationToken cancellationToken)
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
            var token = _life.Token; // taken once: Dispose disposes _life while this loop may still be unwinding
            using var reader = new StreamReader(_stream!, Encoding.UTF8, leaveOpen: true);
            while (await reader.ReadLineAsync(token) is { } line)
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
            pending.Reply.TrySetException(new MpvException(MpvFailureKind.CommandError, $"mpv: {UrlRedaction.Redact(error.GetString() ?? "")}"));
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
}
