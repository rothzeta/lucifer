using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class CommandLineTests
{
    private const string Release = "a24495e4-eb70-48cd-b0fa-31a606459d26";

    [Theory]
    [InlineData("play", Mode.Play)]
    [InlineData("check", Mode.Check)]
    [InlineData("help", Mode.Help)]
    [InlineData("--help", Mode.Help)]
    [InlineData("-h", Mode.Help)]
    public void Single_word_commands_parse(string arg, Mode expected)
    {
        var invocation = CommandLine.Parse([arg]);

        Assert.Null(invocation.Error);
        Assert.Equal(expected, invocation.Mode);
    }

    [Fact]
    public void Tag_read_parses()
    {
        var invocation = CommandLine.Parse(["tag", "read"]);

        Assert.Null(invocation.Error);
        Assert.Equal(Mode.TagRead, invocation.Mode);
    }

    [Fact]
    public void Tag_write_parses_release()
    {
        var invocation = CommandLine.Parse(["tag", "write", "--release", Release.ToUpperInvariant()]);

        Assert.Null(invocation.Error);
        Assert.Equal(Mode.TagWrite, invocation.Mode);
        Assert.Equal(Guid.Parse(Release), invocation.Release);
    }

    [Theory]
    [InlineData]
    [InlineData("bogus")]
    [InlineData("tag")]
    [InlineData("tag", "bogus")]
    [InlineData("tag", "write")]
    [InlineData("tag", "write", "--release")]
    [InlineData("tag", "write", "--release", "not-a-uuid")]
    [InlineData("tag", "write", "--release", "00000000-0000-0000-0000-000000000000")]
    [InlineData("tag", "write", "amp:album:1:test")]
    [InlineData("tag", "read", "extra")]
    [InlineData("play", "extra")]
    [InlineData("check", "--nope")]
    public void Invalid_invocations_report_an_error(params string[] args)
    {
        var invocation = CommandLine.Parse(args);

        Assert.False(string.IsNullOrWhiteSpace(invocation.Error));
    }

    [Fact]
    public void Help_text_lists_every_mode()
    {
        var help = CommandLine.HelpText;

        Assert.Contains("play", help);
        Assert.Contains("tag read", help);
        Assert.Contains("tag write --release <uuid>", help);
        Assert.Contains("check", help);
        Assert.Contains("help", help);
    }
}
