namespace Amp.Nfc;

public enum Mode { Help, Play, TagRead, TagWrite, Check }

public sealed record Invocation(Mode Mode, Guid? Release = null, string? Error = null);

/// <summary>Parses arguments only; never touches configuration or hardware.</summary>
public static class CommandLine
{
    public const string HelpText = """
        Lucifer: NFC album player

        Usage:
          amp-nfc play                          Run the player: tap a tag to play its album.
          amp-nfc tag read                      Read the NDEF text on the tag placed on the reader.
          amp-nfc tag write --release <uuid>    Write a MusicBrainz release identity to a tag.
          amp-nfc check                         Validate configuration and catalogue; no reader or server access.
          amp-nfc help                          Show this help.

        The reader has one owner. Stop the player service before using `tag`, then start it again.

        Environment (unset values use defaults; blank values are rejected):
          NAVIDROME_URL, NAVIDROME_USER, NAVIDROME_PASSWORD   required by play and check only
          LUCIFER_NFC_DEVICE             default /dev/ttyUSB0
          LUCIFER_MPV_PATH               default mpv
          LUCIFER_AUDIO_DEVICE           default alsa/default:CARD=Pro
          LUCIFER_IPC_DIR                default /tmp (mpv socket and reader lock)
          LUCIFER_CATALOGUE_PATH         default catalogue.json (release UUID -> Navidrome album ID; see README)
          LUCIFER_READER_TIMEOUT_SECONDS     default 30 (wait for a tag in tag read/write)
          LUCIFER_MPV_START_TIMEOUT_SECONDS  default 5
          LUCIFER_HTTP_TIMEOUT_SECONDS       default 15
        """;

    public static Invocation Parse(string[] args)
    {
        if (args.Length == 0)
            return Fail("no command given");

        switch (args[0])
        {
            case "help" or "--help" or "-h":
                return args.Length == 1 ? new(Mode.Help) : Fail("help takes no arguments");
            case "play":
                return args.Length == 1 ? new(Mode.Play) : Fail("play takes no arguments");
            case "check":
                return args.Length == 1 ? new(Mode.Check) : Fail("check takes no arguments");
            case "tag":
                return ParseTag(args);
            default:
                return Fail($"unknown command '{args[0]}'");
        }
    }

    private static Invocation ParseTag(string[] args)
    {
        if (args.Length < 2)
            return Fail("tag needs a subcommand: read or write");

        switch (args[1])
        {
            case "read":
                return args.Length == 2 ? new(Mode.TagRead) : Fail("tag read takes no arguments");
            case "write":
                if (args.Length < 4 || args[2] != "--release")
                    return Fail("tag write requires --release <uuid>");
                if (args.Length > 4)
                    return Fail("tag write takes only --release <uuid>");
                if (!Guid.TryParse(args[3], out var release) || release == Guid.Empty)
                    return Fail("--release must be a non-empty UUID");
                return new(Mode.TagWrite, release);
            default:
                return Fail($"unknown tag subcommand '{args[1]}'");
        }
    }

    private static Invocation Fail(string message) => new(Mode.Help, Error: message);
}
