using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Amp.Nfc;

sealed class Mpv : IDisposable
{
    private readonly Process _process;
    private readonly string _socketPath;

    private Mpv(Process process, string socketPath)
    {
        _process = process;
        _socketPath = socketPath;
    }

    public static async Task<Mpv> StartAsync(
        string mpvPath,
        string socketPath,
        string audioDevice,
        TimeSpan startTimeout,
        CancellationToken cancellationToken)
    {
        if (File.Exists(socketPath))
            File.Delete(socketPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = mpvPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--no-video");
        startInfo.ArgumentList.Add($"--audio-device={audioDevice}");
        startInfo.ArgumentList.Add($"--input-ipc-server={socketPath}");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start mpv.");

        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < startTimeout)
        {
            if (File.Exists(socketPath))
                return new Mpv(process, socketPath);

            await Task.Delay(
                100,
                cancellationToken);
        }

        process.Kill();

        throw new InvalidOperationException(
            "mpv IPC socket was not created.");
    }

    public async Task LoadAlbumAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            return;

        await CommandAsync(
            ["loadfile", urls[0], "replace"],
            cancellationToken);

        foreach (var url in urls.Skip(1))
        {
            await CommandAsync(
                ["loadfile", url, "append"],
                cancellationToken);
        }
    }

    public Task PauseAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["set_property", "pause", true],
            cancellationToken);

    public Task ResumeAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["set_property", "pause", false],
            cancellationToken);

    public Task NextAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["playlist-next", "force"],
            cancellationToken);

    public Task PreviousAsync(
        CancellationToken cancellationToken) =>
        CommandAsync(
            ["playlist-prev", "force"],
            cancellationToken);

    private async Task CommandAsync(
        object[] command,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);

        await socket.ConnectAsync(
            new UnixDomainSocketEndPoint(_socketPath),
            cancellationToken);

        await using var stream =
            new NetworkStream(socket, ownsSocket: false);

        await using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true
            };

        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

        var request = JsonSerializer.Serialize(
            new { command });

        await writer.WriteLineAsync(request);

        var response =
            await reader.ReadLineAsync(cancellationToken);

        if (response is null)
            throw new InvalidOperationException(
                "mpv closed its IPC connection.");

        using var json = JsonDocument.Parse(response);

        var error = json.RootElement
            .GetProperty("error")
            .GetString();

        if (error != "success")
            throw new InvalidOperationException(
                $"mpv: {error}");
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill();

        _process.Dispose();

        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
    }
}
