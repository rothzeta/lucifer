namespace Amp.Nfc;

/// <summary>
/// The play run after the hardware is open: start mpv, scan, and supervise. Policy: a graceful stop
/// (SIGTERM, Ctrl+C) returns normally; essential mpv loss or an unrecoverable reader throws
/// <see cref="FatalRunException"/>; transient album and network failures stay selection outcomes in
/// the controller. Shutdown is bounded by <see cref="LuciferOptions.ShutdownTimeout"/>.
/// </summary>
static class PlaySession
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(150);

    public static async Task RunAsync(
        LuciferOptions options,
        ISelectionReader reader,
        IReaderHealth health,
        Func<Mpv, IAlbumApplier> createApplier,
        SupervisorPolicy policy,
        TextWriter output,
        TextWriter error,
        CancellationToken stop,
        MpvTimeouts? mpvTimeouts = null)
    {
        var hooks = new ShutdownHooks();
        var run = RunCoreAsync(options, reader, health, createApplier, policy, output, error, stop, mpvTimeouts, hooks);

        var stopped = new TaskCompletionSource();
        using var registration = stop.Register(() => stopped.TrySetResult());

        if (await Task.WhenAny(run, stopped.Task) == run)
        {
            await run;
            return;
        }

        // Stop requested: the work gets the deadline to unwind.
        if (await Task.WhenAny(run, Task.Delay(options.ShutdownTimeout)) == run)
        {
            await run;
            return;
        }

        hooks.RunAll();
        _ = run.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        throw new ShutdownTimeoutException(
            $"Shutdown did not finish within {options.ShutdownTimeout.TotalSeconds:0.##}s; abandoned it and released owned resources.");
    }

    private static async Task RunCoreAsync(
        LuciferOptions options,
        ISelectionReader reader,
        IReaderHealth health,
        Func<Mpv, IAlbumApplier> createApplier,
        SupervisorPolicy policy,
        TextWriter output,
        TextWriter error,
        CancellationToken stop,
        MpvTimeouts? mpvTimeouts,
        ShutdownHooks hooks)
    {
        Directory.CreateDirectory(options.IpcDirectory);

        // Cancellation here stops the child and removes its socket inside StartAsync.
        using var mpv = await Mpv.StartAsync(
            options.MpvPath,
            options.SocketPath,
            options.AudioDevice,
            options.MpvStartTimeout,
            stop,
            mpvTimeouts);
        hooks.Register(mpv.Dispose);

        var watch = new ReaderWatch(policy, health, TimeProvider.System);
        var controller = new SelectionController(
            new WatchedReader(reader, watch),
            createApplier(mpv),
            new SelectionPolicy(options.AbsenceConfirmation),
            TimeProvider.System,
            output.WriteLine);

        output.WriteLine("Waiting for album...");

        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (mpv.Lost is { } lost)
                    throw new FatalRunException($"mpv is gone: {lost.Message}");

                await controller.StepAsync(stop);

                if (watch.FatalReason is { } reason)
                    throw new FatalRunException(reason);

                await Task.Delay(Tick, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // graceful stop
        }
        finally
        {
            controller.Stop();
            try
            {
                await controller.WhenIdleAsync().WaitAsync(options.ShutdownTimeout);
            }
            catch (TimeoutException)
            {
                error.WriteLine("Pending work did not stop in time; abandoning it.");
            }
        }

        output.WriteLine("Stopped.");
    }
}
