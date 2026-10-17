using System.Threading.Channels;

namespace Amp.Nfc;

/// <summary>
/// Controls as configured for `play`: a mapping, a way to open the device, and the startup policy.
/// Opening happens in the play session, so a missing device is decided there.
/// </summary>
public sealed record ControlsSetup(InputMapping Mapping, Func<IInputDevice> Open, bool Required, TimeSpan Debounce)
{
    /// <summary>
    /// Opens the device. A missing or unreadable device is a logged degradation (the player still
    /// plays albums) unless controls are required, in which case startup fails.
    /// </summary>
    public static IInputDevice? TryOpen(ControlsSetup setup, Action<string> log)
    {
        try
        {
            return setup.Open();
        }
        catch (InputDeviceException ex)
        {
            if (setup.Required)
                throw new FatalRunException($"Required input device is unavailable: {ex.Message}");
            log($"Input device unavailable ({ex.Message}); controls disabled. Album selection continues.");
            return null;
        }
    }
}

public sealed record ControlsResolution(ControlsSetup? Setup, string Description, IReadOnlyList<FieldError> Errors, string? MappingPath = null);

public static class ControlsConfig
{
    /// <summary>
    /// Decides from validated options whether controls run. No device or no mapping disables them
    /// with a description naming what is missing; a configured but invalid mapping file is an error.
    /// The device itself is not touched.
    /// </summary>
    public static ControlsResolution Resolve(LuciferOptions options)
    {
        const string hint = "There are no default event codes; discover them with `Amp.Nfc input inspect`.";

        if (options.InputDevice is null && options.InputMapPath is null)
            return new(null, $"disabled: LUCIFER_INPUT_DEVICE and LUCIFER_INPUT_MAP are not set. {hint}", []);
        if (options.InputMapPath is not { } mapPath)
            return new(null, $"disabled: LUCIFER_INPUT_MAP is not set. {hint}", []);
        if (options.InputDevice is not { } device)
            return new(null, "disabled: LUCIFER_INPUT_DEVICE is not set.", []);

        var loaded = InputMapping.Load(mapPath);
        if (loaded.Mapping is not { } mapping)
            return new(null, "invalid mapping", loaded.Errors, mapPath);

        var keys = mapping.Count == 1 ? "1 key" : $"{mapping.Count} keys";
        return new(
            new ControlsSetup(mapping, () => EvdevInputDevice.Open(device), options.InputRequired, options.InputDebounce),
            $"enabled: {device} ({keys} from {mapPath})",
            [],
            mapPath);
    }
}

/// <summary>
/// Turns key presses from an input device into <see cref="PlaybackOwner"/> transport commands.
/// Repeat handling is deliberate: only a press (value 1) acts; auto-repeat (value 2) and release
/// (value 0) never do, so holding next does not skip through the album and holding play/pause does
/// not flicker. Presses of the same key closer together than the debounce window (measured from the
/// last accepted press, presses only) collapse to one. Accepted commands are queued and applied one
/// at a time in arrival order through the owner, so they interleave with album replacement only
/// through the owner's operation lock.
/// </summary>
public sealed class InputControls : IDisposable
{
    private const int QueueLimit = 8;

    private readonly PlaybackOwner _owner;
    private readonly InputMapping _mapping;
    private readonly IInputDevice _device;
    private readonly TimeSpan _debounce;
    private readonly TimeProvider _clock;
    private readonly Action<string> _log;
    private readonly Dictionary<int, DateTimeOffset> _lastPress = [];
    private readonly Channel<TransportCommand> _commands;
    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _linked;
    private string? _lostReason;

    public InputControls(
        PlaybackOwner owner, InputMapping mapping, IInputDevice device, TimeSpan debounce, TimeProvider clock, Action<string> log)
    {
        _owner = owner;
        _mapping = mapping;
        _device = device;
        _debounce = debounce;
        _clock = clock;
        _log = log;
        _commands = Channel.CreateBounded<TransportCommand>(
            new BoundedChannelOptions(QueueLimit) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true },
            dropped => _log($"Control {InputMapping.Name(dropped)} dropped: {QueueLimit} controls are already waiting."));
    }

    /// <summary>Completes when the device stream has ended (or stop was requested) and queued controls have been handled.</summary>
    public Task Completed { get; private set; } = Task.CompletedTask;

    /// <summary>Why the device stopped delivering events, once it has (not set for a requested stop).</summary>
    public string? LostReason => Volatile.Read(ref _lostReason);

    public void Start(CancellationToken stop)
    {
        _linked = CancellationTokenSource.CreateLinkedTokenSource(stop, _life.Token);
        var token = _linked.Token;
        Completed = Task.WhenAll(Task.Run(() => ReadAsync(token)), Task.Run(() => ApplyAsync(token)));
    }

    private async Task ReadAsync(CancellationToken token)
    {
        try
        {
            await foreach (var ev in _device.ReadAsync(token))
                Accept(ev);

            if (!token.IsCancellationRequested)
                Lose("the device stopped delivering events");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InputDeviceException or IOException)
        {
            Lose(ex.Message);
        }
        finally { _commands.Writer.TryComplete(); }
    }

    private void Lose(string reason)
    {
        Volatile.Write(ref _lostReason, reason);
        _log($"Input device lost ({reason}); transport controls stopped.");
    }

    private void Accept(InputEvent ev)
    {
        if (ev.Type != InputEvent.Key || ev.Value != InputEvent.Press)
            return; // release, auto-repeat, and every non-key event

        if (_mapping.Lookup(ev.Code) is not { } command)
            return;

        var now = _clock.GetUtcNow();
        if (_lastPress.TryGetValue(ev.Code, out var last) && now - last < _debounce)
            return;
        _lastPress[ev.Code] = now;

        _commands.Writer.TryWrite(command);
    }

    private async Task ApplyAsync(CancellationToken token)
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(token))
            {
                var name = InputMapping.Name(command);
                try
                {
                    var outcome = await _owner.TransportAsync(command, token);
                    _log($"Control {name}: {outcome.ToString().ToLowerInvariant()}");
                }
                catch (MpvException ex)
                {
                    _log($"Control {name} failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _life.Cancel();
        _device.Dispose();
        _linked?.Dispose();
        _life.Dispose();
    }
}
