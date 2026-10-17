using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class OptionsTests
{
    private const string Password = "s3cret-pass-word";

    private static Func<string, string?> Env(params (string Name, string? Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.GetValueOrDefault(name);
    }

    private static readonly (string, string?)[] Server =
    [
        ("NAVIDROME_URL", "https://music.example.test:4533/"),
        ("NAVIDROME_USER", "listener"),
        ("NAVIDROME_PASSWORD", Password),
    ];

    [Fact]
    public void Writer_modes_need_no_server_settings()
    {
        foreach (var mode in new[] { Mode.TagRead, Mode.TagWrite })
        {
            var result = LuciferOptions.Load(Env(), mode);

            Assert.Empty(result.Errors);
            Assert.NotNull(result.Options);
            Assert.Equal("/dev/ttyUSB0", result.Options!.NfcDevice);
        }
    }

    [Fact]
    public void Writer_modes_ignore_malformed_server_settings()
    {
        var result = LuciferOptions.Load(Env(("NAVIDROME_URL", "nonsense")), Mode.TagWrite);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(Mode.Play)]
    [InlineData(Mode.Check)]
    public void Server_modes_report_each_missing_credential_by_field(Mode mode)
    {
        var result = LuciferOptions.Load(Env(), mode);

        var fields = result.Errors.Select(e => e.Field).ToArray();
        Assert.Contains("NAVIDROME_URL", fields);
        Assert.Contains("NAVIDROME_USER", fields);
        Assert.Contains("NAVIDROME_PASSWORD", fields);
    }

    [Fact]
    public void Defaults_apply_when_unset_and_server_is_valid()
    {
        var result = LuciferOptions.Load(Env(Server), Mode.Play);

        Assert.Empty(result.Errors);
        var o = result.Options!;
        Assert.Equal("/dev/ttyUSB0", o.NfcDevice);
        Assert.Equal("mpv", o.MpvPath);
        Assert.Equal("alsa/default:CARD=Pro", o.AudioDevice);
        Assert.Equal("https://music.example.test:4533", o.NavidromeUrl);
        Assert.True(o.ReaderTimeout > TimeSpan.Zero);
        Assert.True(o.MpvStartTimeout > TimeSpan.Zero);
        Assert.True(o.HttpTimeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("LUCIFER_NFC_DEVICE")]
    [InlineData("LUCIFER_MPV_PATH")]
    [InlineData("LUCIFER_AUDIO_DEVICE")]
    [InlineData("LUCIFER_IPC_DIR")]
    [InlineData("LUCIFER_CATALOGUE_PATH")]
    [InlineData("NAVIDROME_USER")]
    [InlineData("NAVIDROME_PASSWORD")]
    public void Blank_values_are_rejected_by_field(string field)
    {
        var env = Env(Server.Append((field, "   ")).GroupBy(v => v.Item1).Select(g => g.Last()).ToArray());

        var result = LuciferOptions.Load(env, Mode.Play);

        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Theory]
    [InlineData("LUCIFER_READER_TIMEOUT_SECONDS")]
    [InlineData("LUCIFER_MPV_START_TIMEOUT_SECONDS")]
    [InlineData("LUCIFER_HTTP_TIMEOUT_SECONDS")]
    public void Deadlines_must_be_positive_integers(string field)
    {
        foreach (var bad in new[] { "0", "-3", "abc", "" })
        {
            var env = Env(Server.Append((field, bad)).ToArray());

            var result = LuciferOptions.Load(env, Mode.Play);

            Assert.Contains(result.Errors, e => e.Field == field);
        }
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("music.example.test")]
    [InlineData("ftp://music.example.test")]
    [InlineData("http://")]
    [InlineData("https://user:hunter2@music.example.test")]
    public void Malformed_server_urls_are_rejected_without_echoing_them(string url)
    {
        var env = Env(("NAVIDROME_URL", url), ("NAVIDROME_USER", "u"), ("NAVIDROME_PASSWORD", Password));

        var result = LuciferOptions.Load(env, Mode.Play);

        var error = Assert.Single(result.Errors, e => e.Field == "NAVIDROME_URL");
        Assert.DoesNotContain("hunter2", error.Message);
        Assert.DoesNotContain(url, error.Message);
    }

    [Fact]
    public void Options_never_render_the_password()
    {
        var options = LuciferOptions.Load(Env(Server), Mode.Play).Options!;

        Assert.DoesNotContain(Password, options.ToString());
    }
}
