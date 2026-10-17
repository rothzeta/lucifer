using System.Runtime.InteropServices;

namespace Amp.Nfc;

/// <summary>
/// The supervisor policy's failure signal: the application cannot keep serving (essential mpv is
/// gone, or the reader is unrecoverable). The process exits nonzero so the service manager
/// restarts it and its owned children.
/// </summary>
public sealed class FatalRunException(string message) : Exception(message);

/// <summary>Shutdown work did not finish by the deadline and was abandoned after forced cleanup.</summary>
public sealed class ShutdownTimeoutException(string message) : Exception(message);

/// <summary>Cleanup actions for resources the process owns, run when graceful shutdown overruns.</summary>
public sealed class ShutdownHooks
{
    private readonly List<Action> _hooks = [];

    public void Register(Action hook)
    {
        lock (_hooks) _hooks.Add(hook);
    }

    public void RunAll()
    {
        Action[] hooks;
        lock (_hooks) hooks = [.. _hooks];
        foreach (var hook in Enumerable.Reverse(hooks))
        {
            try { hook(); }
            catch (Exception) { /* keep releasing the rest */ }
        }
    }
}

/// <summary>Turns SIGTERM (service stop) and Ctrl+C into a cooperative stop request.</summary>
public static class StopSignals
{
    public static IDisposable Register(CancellationTokenSource stop)
    {
        void Request()
        {
            try { stop.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        ConsoleCancelEventHandler onCancelKey = (_, e) =>
        {
            e.Cancel = true; // a first Ctrl+C is a graceful stop, not an abrupt kill
            Request();
        };
        Console.CancelKeyPress += onCancelKey;
        var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Request();
        });

        return new Registration(() =>
        {
            Console.CancelKeyPress -= onCancelKey;
            term.Dispose();
        });
    }

    private sealed class Registration(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
