using Amp.Nfc;

namespace Amp.Nfc.Tests;

/// <summary>Raw pause/resume/next commands for the IPC-layer tests; production code goes through <see cref="Mpv.TransportAsync"/>.</summary>
static class MpvCommandExtensions
{
    public static Task PauseAsync(this Mpv mpv, CancellationToken cancellationToken) =>
        mpv.CommandAsync(["set_property", "pause", true], cancellationToken);

    public static Task ResumeAsync(this Mpv mpv, CancellationToken cancellationToken) =>
        mpv.CommandAsync(["set_property", "pause", false], cancellationToken);

    public static Task NextAsync(this Mpv mpv, CancellationToken cancellationToken) =>
        mpv.CommandAsync(["playlist-next", "force"], cancellationToken);
}
