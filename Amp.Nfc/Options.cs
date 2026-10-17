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

        if (usesServer)
        {
            mpv = Text("LUCIFER_MPV_PATH", "mpv");
            audio = Text("LUCIFER_AUDIO_DEVICE", "alsa/default:CARD=Pro");
            catalogue = Text("LUCIFER_CATALOGUE_PATH", "catalogue.json");
            mpvTimeout = Seconds("LUCIFER_MPV_START_TIMEOUT_SECONDS", 5);
            httpTimeout = Seconds("LUCIFER_HTTP_TIMEOUT_SECONDS", 15);
            url = ServerUrl(env, errors);
            user = Required(env, "NAVIDROME_USER", errors);
            password = Required(env, "NAVIDROME_PASSWORD", errors);
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
