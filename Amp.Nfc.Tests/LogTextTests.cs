namespace Amp.Nfc.Tests;

public class LogTextTests
{
    [Theory]
    [InlineData("a\u001b[31mb", "a?[31mb")]
    [InlineData("a\u202eb", "a?b")]
    [InlineData("a\u2028b\u2029c", "a?b?c")]
    [InlineData("a\U000E0041b", "a?b")] // supplementary-plane tag character: one '?' per character
    [InlineData("caf\u00e9 \U0001F3B5", "caf\u00e9 \U0001F3B5")]
    public void Clean_replaces_unsafe_characters_and_keeps_the_rest(string input, string expected) =>
        Assert.Equal(expected, LogText.Clean(input));

    [Fact]
    public void Clean_of_null_is_empty() => Assert.Equal("", LogText.Clean(null));
}
