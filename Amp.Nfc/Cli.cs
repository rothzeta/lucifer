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

        if (invocation.Mode == Mode.InputInspect && invocation.Device is { } given
            && InputDevicePath.Validate(given) is { } badDevice)
        {
            error.WriteLine($"Error: --device {badDevice}");
            return Usage;
        }

        var loaded = LuciferOptions.Load(env, invocation.Mode);

        if (loaded.Options is not { } options)
        {
            error.WriteLine("Configuration errors:");
            foreach (var e in loaded.Errors)
                error.WriteLine($"  {e.Field}: {e.Message}");
            return invocation.Mode == Mode.Check ? Failed : Usage;
        }

        // Reads the device only; it never needs the reader, so it takes no reader lock.
        if (invocation.Mode == Mode.InputInspect)
        {
            if ((invocation.Device ?? options.InputDevice) is not { } device)
            {
                error.WriteLine("Error: no input device; pass --device <path> or set LUCIFER_INPUT_DEVICE.");
                return Usage;
            }

            try
            {
                return await InputInspect.RunAsync(device, invocation.Count, output, error, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Ok;
            }
        }

        CatalogueResult? catalogue = null;
        if (invocation.Mode is Mode.Check or Mode.Play)
        {
            catalogue = Catalogue.Load(options.CataloguePath);
            if (catalogue.Catalogue is null)
            {
                error.WriteLine($"Catalogue errors ({options.CataloguePath}):");
                foreach (var e in catalogue.Errors)
                    error.WriteLine($"  {e.Field}: {e.Message}");
                return Failed;
            }
        }

        ControlsResolution? controls = null;
        if (invocation.Mode is Mode.Check or Mode.Play)
        {
            controls = ControlsConfig.Resolve(options);
            if (controls.Errors.Count > 0)
            {
                error.WriteLine($"Input mapping errors ({controls.MappingPath}):");
                foreach (var e in controls.Errors)
                    error.WriteLine($"  {e.Field}: {e.Message}");
                return Failed;
            }
        }

        if (invocation.Mode == Mode.Check)
        {
            output.WriteLine("Configuration is valid.");
            output.WriteLine($"  reader:    {options.NfcDevice}");
            output.WriteLine($"  ipc dir:   {options.IpcDirectory}");
            output.WriteLine($"  mpv:       {options.MpvPath} ({options.AudioDevice})");
            output.WriteLine($"  server:    {options.NavidromeUrl} (credentials set)");
            output.WriteLine($"  catalogue: {options.CataloguePath} ({catalogue!.Catalogue!.Count} albums)");
            output.WriteLine($"  controls:  {controls!.Description}");
            output.WriteLine("No reader, input device or server was contacted; album IDs are not verified against the server.");
            return Ok;
        }

        try
        {
            IpcDirectoryPolicy.Prepare(options);
            using var owner = ReaderLock.Acquire(options.IpcDirectory, options.NfcDevice);

            switch (invocation.Mode)
            {
                case Mode.Play:
                    await Player.RunAsync(options, controls!, output, error, cancellationToken);
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
