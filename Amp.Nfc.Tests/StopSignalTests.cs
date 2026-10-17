using System.Diagnostics;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class StopSignalTests
{
    [Fact]
    public async Task Sigterm_requests_a_graceful_stop_without_killing_the_process()
    {
        using var cts = new CancellationTokenSource();
        using var registration = StopSignals.Register(cts);

        using var kill = Process.Start("kill", ["-TERM", Environment.ProcessId.ToString()])!;
        await kill.WaitForExitAsync();

        var sw = Stopwatch.StartNew();
        while (!cts.IsCancellationRequested && sw.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(20);

        Assert.True(cts.IsCancellationRequested);
    }
}
