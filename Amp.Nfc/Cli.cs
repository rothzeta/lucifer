namespace Amp.Nfc;

/// <summary>
/// Command dispatch. Order matters: parse arguments, validate configuration, take reader
/// ownership, and only then open the reader.
/// </summary>
public static class Cli
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public static async Task<int> RunAsync(
        string[] args,
        Func<string, string?> env,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var invocation = CommandLine.Parse(args);

        if (invocation.Error is not null)
        {
            error.WriteLine($"Error: {invocation.Error}");
            error.WriteLine();
            error.WriteLine(CommandLine.HelpText);
            return Usage;
        }

        if (invocation.Mode == Mode.Help)
        {
            output.WriteLine(CommandLine.HelpText);
            return Ok;
        }

        var loaded = LuciferOptions.Load(env, invocation.Mode);

        if (loaded.Options is not { } options)
        {
            error.WriteLine("Configuration errors:");
            foreach (var e in loaded.Errors)
                error.WriteLine($"  {e.Field}: {e.Message}");
            return invocation.Mode == Mode.Check ? Failed : Usage;
        }

        if (invocation.Mode == Mode.Check)
        {
            output.WriteLine("Configuration is valid.");
            output.WriteLine($"  reader:    {options.NfcDevice}");
            output.WriteLine($"  ipc dir:   {options.IpcDirectory}");
            output.WriteLine($"  mpv:       {options.MpvPath} ({options.AudioDevice})");
            output.WriteLine($"  server:    {options.NavidromeUrl} (credentials set)");
            output.WriteLine("No reader or server was contacted.");
            return Ok;
        }

        try
        {
            using var owner = ReaderLock.Acquire(options.IpcDirectory, options.NfcDevice);

            switch (invocation.Mode)
            {
                case Mode.Play:
                    await Player.RunAsync(options, output, error, cancellationToken);
                    break;
                case Mode.TagRead:
                    TagCommands.Read(options, output, cancellationToken);
                    break;
                case Mode.TagWrite:
                    TagCommands.Write(options, ReleaseId.From(invocation.Release!.Value), output, cancellationToken);
                    break;
            }

            return Ok;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Ok;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            return Failed;
        }
    }
}
