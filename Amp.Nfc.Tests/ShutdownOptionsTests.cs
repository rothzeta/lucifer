using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class ShutdownOptionsTests
{
    private static OptionsResult Load(string? timeout)
    {
        var env = new Dictionary<string, string>
        {
            ["NAVIDROME_URL"] = "https://music.example.test",
            ["NAVIDROME_USER"] = "u",
            ["NAVIDROME_PASSWORD"] = "pw",
        };
        if (timeout is not null) env["LUCIFER_SHUTDOWN_TIMEOUT_SECONDS"] = timeout;
        return LuciferOptions.Load(k => env.GetValueOrDefault(k), Mode.Play);
    }

    [Fact]
    public void Shutdown_deadline_defaults_to_ten_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Load(null).Options!.ShutdownTimeout);
    }

    [Fact]
    public void Shutdown_deadline_is_capped_below_the_units_stop_timeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(25), Load("25").Options!.ShutdownTimeout);
        Assert.Contains(Load("26").Errors, e => e.Field == "LUCIFER_SHUTDOWN_TIMEOUT_SECONDS" && e.Message.Contains("25"));
    }

    [Fact]
    public void Help_text_lists_the_shutdown_and_absence_settings()
    {
        Assert.Contains("LUCIFER_SHUTDOWN_TIMEOUT_SECONDS", CommandLine.HelpText);
        Assert.Contains("LUCIFER_ABSENCE_CONFIRM_MS", CommandLine.HelpText);
    }

    [Fact]
    public void Shutdown_deadline_can_be_configured()
    {
        Assert.Equal(TimeSpan.FromSeconds(25), Load("25").Options!.ShutdownTimeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("301")]
    [InlineData("300")]
    [InlineData("26")]
    public void Invalid_shutdown_deadlines_are_rejected_by_field(string value)
    {
        var result = Load(value);

        Assert.Null(result.Options);
        Assert.Contains(result.Errors, e => e.Field == "LUCIFER_SHUTDOWN_TIMEOUT_SECONDS");
    }
}
