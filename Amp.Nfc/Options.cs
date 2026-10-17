namespace Amp.Nfc;

public sealed record FieldError(string Field, string Message);

public sealed record OptionsResult(LuciferOptions? Options, IReadOnlyList<FieldError> Errors);

/// <summary>Validated settings. Field names in errors are the environment variable names.</summary>
public sealed class LuciferOptions
{
    public string NfcDevice { get; private init; } = "";
    public string MpvPath { get; private init; } = "";
    public string AudioDevice { get; private init; } = "";
    public string IpcDirectory { get; private init; } = "";
    public string CataloguePath { get; private init; } = "";
    public TimeSpan ReaderTimeout { get; private init; }
    public TimeSpan MpvStartTimeout { get; private init; }
    public TimeSpan HttpTimeout { get; private init; }

    /// <summary>Deadline for graceful shutdown after a stop request. Default 10 s, 1 to 25 s (below the unit's 30 s TimeoutStopSec).</summary>
    public TimeSpan ShutdownTimeout { get; private init; }

    /// <summary>How long the reader must report no tag before the object counts as removed. Default 1.5 s.</summary>
    public TimeSpan AbsenceConfirmation { get; private init; }

    /// <summary>Linux input device for the transport controls; null disables controls. No default.</summary>
    public string? InputDevice { get; private init; }

    /// <summary>Key mapping file for the transport controls; null disables controls. No default codes exist.</summary>
    public string? InputMapPath { get; private init; }

    /// <summary>When true, a device that cannot be opened (or is lost) is fatal instead of a logged degradation.</summary>
    public bool InputRequired { get; private init; }

    /// <summary>Presses of one key closer together than this count once. Default 250 ms, 0 to 5000.</summary>
    public TimeSpan InputDebounce { get; private init; }
    public string NavidromeUrl { get; private init; } = "";
    public string NavidromeUser { get; private init; } = "";
    public string NavidromePassword { get; private init; } = "";

    public string SocketPath => Path.Combine(IpcDirectory, "amp-mpv.sock");

    // Never render credentials by accident.
    public override string ToString() => "LuciferOptions";

    public static OptionsResult Load(Func<string, string?> env, Mode mode)
    {
        var errors = new List<FieldError>();
        var usesServer = mode is Mode.Play or Mode.Check;

        string Text(string name, string fallback) => Optional(env, name, fallback, errors);
        TimeSpan Seconds(string name, int fallback) => Deadline(env, name, fallback, errors);

        var nfc = Text("LUCIFER_NFC_DEVICE", "/dev/ttyUSB0");
        var ipc = Text("LUCIFER_IPC_DIR", "/tmp");
        var readerTimeout = Seconds("LUCIFER_READER_TIMEOUT_SECONDS", 30);

        string mpv = "", audio = "", catalogue = "", url = "", user = "", password = "";
        var mpvTimeout = TimeSpan.Zero;
        var httpTimeout = TimeSpan.Zero;
        var absence = TimeSpan.Zero;
        var shutdown = TimeSpan.Zero;

        if (usesServer)
        {
            mpv = Text("LUCIFER_MPV_PATH", "mpv");
            audio = Text("LUCIFER_AUDIO_DEVICE", "alsa/default:CARD=Pro");
            catalogue = Text("LUCIFER_CATALOGUE_PATH", "catalogue.json");
            mpvTimeout = Seconds("LUCIFER_MPV_START_TIMEOUT_SECONDS", 5);
            httpTimeout = Seconds("LUCIFER_HTTP_TIMEOUT_SECONDS", 15);
            absence = Milliseconds("LUCIFER_ABSENCE_CONFIRM_MS", 1500, 100, 60_000, errors, env);
            shutdown = ShutdownDeadline(env, errors);
            url = ServerUrl(env, errors);
            user = Required(env, "NAVIDROME_USER", errors);
            password = Required(env, "NAVIDROME_PASSWORD", errors);
        }

        string? inputDevice = null, inputMap = null;
        var inputRequired = false;
        var inputDebounce = TimeSpan.Zero;

        if (usesServer || mode == Mode.InputInspect)
            inputDevice = InputPath(env, "LUCIFER_INPUT_DEVICE", errors);

        if (usesServer)
        {
            inputMap = OptionalText(env, "LUCIFER_INPUT_MAP", errors);
            inputRequired = Flag(env, "LUCIFER_INPUT_REQUIRED", errors);
            inputDebounce = Milliseconds("LUCIFER_INPUT_DEBOUNCE_MS", 250, 0, 5000, errors, env);
            if (inputRequired && inputDevice is null && env("LUCIFER_INPUT_DEVICE") is null)
                errors.Add(new("LUCIFER_INPUT_DEVICE", "is required when LUCIFER_INPUT_REQUIRED is true"));
            if (inputRequired && inputMap is null && env("LUCIFER_INPUT_MAP") is null)
                errors.Add(new("LUCIFER_INPUT_MAP", "is required when LUCIFER_INPUT_REQUIRED is true"));
        }

        if (errors.Count > 0)
            return new(null, errors);

        return new(new LuciferOptions
        {
            NfcDevice = nfc,
            MpvPath = mpv,
            AudioDevice = audio,
            IpcDirectory = ipc,
            CataloguePath = catalogue,
            ReaderTimeout = readerTimeout,
            MpvStartTimeout = mpvTimeout,
            HttpTimeout = httpTimeout,
            AbsenceConfirmation = absence,
            ShutdownTimeout = shutdown,
            InputDevice = inputDevice,
            InputMapPath = inputMap,
            InputRequired = inputRequired,
            InputDebounce = inputDebounce,
            NavidromeUrl = url,
            NavidromeUser = user,
            NavidromePassword = password,
        }, errors);
    }

    private static string Optional(Func<string, string?> env, string name, string fallback, List<FieldError> errors)
    {
        var value = env(name);
        if (value is null)
            return fallback;
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(name, "must not be blank (unset it to use the default)"));
            return fallback;
        }
        return value;
    }

    private static string? InputPath(Func<string, string?> env, string name, List<FieldError> errors)
    {
        var value = env(name);
        if (value is null)
            return null;
        if (InputDevicePath.Validate(value) is { } why)
        {
            errors.Add(new(name, why));
            return null;
        }
        return value;
    }

    private static string? OptionalText(Func<string, string?> env, string name, List<FieldError> errors)
    {
        var value = env(name);
        if (value is null)
            return null;
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(name, "must not be blank (unset it to disable)"));
            return null;
        }
        return value;
    }

    private static bool Flag(Func<string, string?> env, string name, List<FieldError> errors)
    {
        var value = env(name);
        if (value is null)
            return false;
        if (value is "true" or "false")
            return value == "true";
        errors.Add(new(name, "must be true or false"));
        return false;
    }

    private static string Required(Func<string, string?> env, string name, List<FieldError> errors)
    {
        var value = env(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(name, value is null ? "is required" : "must not be blank"));
            return "";
        }
        return value;
    }

    private static TimeSpan Deadline(Func<string, string?> env, string name, int fallback, List<FieldError> errors)
    {
        var value = env(name);
        if (value is null)
            return TimeSpan.FromSeconds(fallback);
        if (!int.TryParse(value, out var seconds) || seconds <= 0)
        {
            errors.Add(new(name, "must be a whole number of seconds greater than zero"));
            return TimeSpan.FromSeconds(fallback);
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan ShutdownDeadline(Func<string, string?> env, List<FieldError> errors)
    {
        const string name = "LUCIFER_SHUTDOWN_TIMEOUT_SECONDS";
        var value = env(name);
        if (value is null)
            return TimeSpan.FromSeconds(10);
        if (!int.TryParse(value, out var seconds) || seconds is < 1 or > 25)
        {
            errors.Add(new(name, "must be a whole number of seconds from 1 to 25 (the service unit allows 30 s to stop)"));
            return TimeSpan.FromSeconds(10);
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan Milliseconds(
        string name, int fallback, int min, int max, List<FieldError> errors, Func<string, string?> env)
    {
        var value = env(name);
        if (value is null)
            return TimeSpan.FromMilliseconds(fallback);
        if (!int.TryParse(value, out var ms) || ms < min || ms > max)
        {
            errors.Add(new(name, $"must be a whole number of milliseconds from {min} to {max}"));
            return TimeSpan.FromMilliseconds(fallback);
        }
        return TimeSpan.FromMilliseconds(ms);
    }

    // Messages never repeat the value: it may carry credentials.
    private static string ServerUrl(Func<string, string?> env, List<FieldError> errors)
    {
        const string name = "NAVIDROME_URL";
        var value = Required(env, name, errors);
        if (value.Length == 0)
            return "";

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            errors.Add(new(name, "must be an absolute http or https URL with a host"));
            return "";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add(new(name, "must not contain credentials; use NAVIDROME_USER and NAVIDROME_PASSWORD"));
            return "";
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
