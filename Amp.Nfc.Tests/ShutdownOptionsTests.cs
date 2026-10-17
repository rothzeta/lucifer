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
    public void Invalid_shutdown_deadlines_are_rejected_by_field(string value)
    {
        var result = Load(value);

        Assert.Null(result.Options);
        Assert.Contains(result.Errors, e => e.Field == "LUCIFER_SHUTDOWN_TIMEOUT_SECONDS");
    }
}
