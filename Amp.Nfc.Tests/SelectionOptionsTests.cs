using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class SelectionOptionsTests
{
    private static Func<string, string?> Env(string? confirm)
    {
        var map = new Dictionary<string, string?>
        {
            ["NAVIDROME_URL"] = "https://music.example.test",
            ["NAVIDROME_USER"] = "u",
            ["NAVIDROME_PASSWORD"] = "p",
            ["LUCIFER_ABSENCE_CONFIRM_MS"] = confirm,
        };
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void Absence_confirmation_defaults_to_1500_ms()
    {
        var result = LuciferOptions.Load(Env(null), Mode.Play);

        Assert.Empty(result.Errors);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), result.Options!.AbsenceConfirmation);
    }

    [Fact]
    public void Absence_confirmation_can_be_configured()
    {
        var result = LuciferOptions.Load(Env("3000"), Mode.Play);

        Assert.Equal(TimeSpan.FromSeconds(3), result.Options!.AbsenceConfirmation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("99")]
    [InlineData("60001")]
    public void Absence_confirmation_outside_100_to_60000_ms_is_rejected(string value)
    {
        var result = LuciferOptions.Load(Env(value), Mode.Play);

        Assert.Null(result.Options);
        Assert.Contains(result.Errors, e => e.Field == "LUCIFER_ABSENCE_CONFIRM_MS");
    }
}
