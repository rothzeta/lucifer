using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc.Tests;

/// <summary>An in-process stand-in for mpv's JSON IPC server on a unix socket.</summary>
sealed class FakeMpvPeer : IDisposable
{
    public sealed class Connection(Socket socket)
    {
        private readonly NetworkStream _stream = new(socket, ownsSocket: false);

        public Task SendLineAsync(string line) =>
            _stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n")).AsTask();

        public Task ReplyAsync(long id, string error = "success", object? data = null) =>
            SendLineAsync(JsonSerializer.Serialize(new { error, data, request_id = id }));

        public Task EventAsync(string name, object? extra = null)
        {
            var fields = new Dictionary<string, object?> { ["event"] = name };
            if (extra is not null)
                foreach (var p in extra.GetType().GetProperties())
                    fields[p.Name] = p.GetValue(extra);
            return SendLineAsync(JsonSerializer.Serialize(fields));
        }

        public void Close() => socket.Close();
    }

    public sealed record Request(long Id, JsonElement Command, JsonElement Root)
    {
        public string Name => Command[0].GetString()!;
    }

    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Request> _requests = [];
    private int _inFlight;

    public string Path { get; }
    public int MaxInFlight { get; private set; }
    public int NextEntryId;

    /// <summary>Return true when the handler replied (or deliberately did not); false for the default success reply.</summary>
    public Func<Request, Connection, Task<bool>>? Handler { get; set; }

    public FakeMpvPeer(string path)
    {
        Path = path;
        _listener.Bind(new UnixDomainSocketEndPoint(path));
        _listener.Listen(8);
        _ = AcceptLoop();
    }

    public IReadOnlyList<Request> Requests
    {
        get { lock (_requests) return _requests.ToArray(); }
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptAsync(_stop.Token);
                _ = Serve(new Connection(socket), socket);
            }
        }
        catch { }
    }

    private Connection? _last;
    public Connection? Last => _last;

    private async Task Serve(Connection conn, Socket socket)
    {
        _last = conn;
        try
        {
            using var reader = new StreamReader(new NetworkStream(socket, ownsSocket: false), Encoding.UTF8);
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                var root = JsonDocument.Parse(line).RootElement.Clone();
                var req = new Request(root.GetProperty("request_id").GetInt64(), root.GetProperty("command"), root);
                lock (_requests) _requests.Add(req);
                MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
                try
                {
                    var handled = Handler is not null && await Handler(req, conn);
                    if (!handled)
                    {
                        object? data = req.Name == "loadfile"
                            ? new { playlist_entry_id = Interlocked.Increment(ref NextEntryId) }
                            : null;
                        await conn.ReplyAsync(req.Id, data: data);
                    }
                }
                finally { Interlocked.Decrement(ref _inFlight); }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        try { File.Delete(Path); } catch { }
    }
}
