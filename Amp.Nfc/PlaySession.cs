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
        MpvTimeouts? mpvTimeouts = null,
        ControlsSetup? controls = null)
    {
        var hooks = new ShutdownHooks();
        var run = RunCoreAsync(options, reader, health, createApplier, policy, output, error, stop, mpvTimeouts, hooks, controls);

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

    private static InputControls? OpenControls(ControlsSetup? controls, PlaybackOwner owner, Action<string> log, CancellationToken stop)
    {
        if (controls is null || ControlsSetup.TryOpen(controls, log) is not { } device)
            return null;

        var input = new InputControls(owner, controls.Mapping, device, controls.Debounce, TimeProvider.System, log);
        input.Start(stop);
        return input;
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
        ShutdownHooks hooks,
        ControlsSetup? controls)
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

        // Player status comes from mpv evidence, so this is where a failed or finished album shows.
        mpv.StatusChanged += status => output.WriteLine(status == MpvStatus.Failed
            ? $"Player: failed — {mpv.FailureDetail}"
            : $"Player: {status.ToString().ToLowerInvariant()}");

        var watch = new ReaderWatch(policy, health, TimeProvider.System);
        var controller = new SelectionController(
            new WatchedReader(reader, watch),
            createApplier(mpv),
            new SelectionPolicy(options.AbsenceConfirmation),
            TimeProvider.System,
            output.WriteLine);

        // Controls go through the same owner (operation lock) as album replacement.
        using var input = OpenControls(controls, new PlaybackOwner(mpv), output.WriteLine, stop);

        output.WriteLine("Waiting for album...");

        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (mpv.Lost is { } lost)
                    throw new FatalRunException($"mpv is gone: {lost.Message}");

                if (input?.LostReason is { } gone && controls!.Required)
                    throw new FatalRunException($"Required input device lost: {gone}");

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
