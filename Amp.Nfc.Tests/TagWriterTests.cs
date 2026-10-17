using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class TagWriterTests
{
    static readonly ReleaseId Release = ReleaseId.Parse("a24495e4-eb70-48cd-b0fa-31a606459d26");
    static readonly ReleaseId Other = ReleaseId.Parse("d5e2d971-e856-4342-ad09-0e6ad1b5241a");

    sealed class FakeTag : ITagMedium
    {
        public bool Formatted = true;
        public bool FormatSucceeds = true;
        public bool WriteSucceeds = true;
        public Exception? WriteThrows;
        public bool ReadBackAvailable = true;
        public string[]? ReadBackOverride;
        public string[] Stored = [];
        public List<string> Calls = [];

        public bool IsFormatted() { Calls.Add("isformatted"); return Formatted; }
        public bool Format() { Calls.Add("format"); if (FormatSucceeds) Formatted = true; return FormatSucceeds; }

        public bool WriteText(string text)
        {
            Calls.Add("write");
            if (WriteThrows is not null) throw WriteThrows;
            if (!WriteSucceeds) return false;
            Stored = [text];
            return true;
        }

        public bool TryReadTexts(out IReadOnlyList<string> texts)
        {
            Calls.Add("read");
            texts = ReadBackOverride ?? Stored;
            return ReadBackAvailable;
        }
    }

    static string Run(FakeTag tag, ReleaseId release, out StringWriter output)
    {
        output = new StringWriter();
        TagWriter.Write(tag, release, output);
        return output.ToString();
    }

    [Fact]
    public void Successful_write_is_read_back_compared_and_reported_verified()
    {
        var tag = new FakeTag();

        var text = Run(tag, Release, out _);

        Assert.Equal(["amp:album:1:mbid:a24495e4-eb70-48cd-b0fa-31a606459d26"], tag.Stored);
        Assert.Contains("Verified", text);
        Assert.True(tag.Calls.LastIndexOf("read") > tag.Calls.IndexOf("write"));
    }

    [Fact]
    public void Unformatted_tag_is_formatted_before_writing()
    {
        var tag = new FakeTag { Formatted = false };

        Run(tag, Release, out _);

        Assert.True(tag.Calls.IndexOf("format") < tag.Calls.IndexOf("write"));
    }

    [Fact]
    public void Formatted_tag_is_not_reformatted()
    {
        var tag = new FakeTag();

        Run(tag, Release, out _);

        Assert.DoesNotContain("format", tag.Calls);
    }

    [Fact]
    public void Invalid_identity_performs_no_write_or_format_request()
    {
        var tag = new FakeTag { Formatted = false };

        Assert.Throws<ArgumentException>(() => Run(tag, default, out _));

        Assert.Empty(tag.Calls);
    }

    [Fact]
    public void Failed_format_is_a_failure_without_a_write()
    {
        var tag = new FakeTag { Formatted = false, FormatSucceeds = false };

        Assert.Throws<TagWriteException>(() => Run(tag, Release, out _));

        Assert.DoesNotContain("write", tag.Calls);
    }

    [Fact]
    public void Write_returning_false_is_a_failure_without_verified_message()
    {
        var tag = new FakeTag { WriteSucceeds = false };
        var output = new StringWriter();

        Assert.Throws<TagWriteException>(() => TagWriter.Write(tag, Release, output));

        Assert.DoesNotContain("Verified", output.ToString());
    }

    [Fact]
    public void Interrupted_write_is_a_failure_without_verified_message()
    {
        var tag = new FakeTag { WriteThrows = new IOException("tag removed") };
        var output = new StringWriter();

        Assert.ThrowsAny<Exception>(() => TagWriter.Write(tag, Release, output));

        Assert.DoesNotContain("Verified", output.ToString());
        Assert.DoesNotContain("read", tag.Calls);
    }

    [Fact]
    public void Removed_tag_during_read_back_is_a_failure()
    {
        var tag = new FakeTag { ReadBackAvailable = false };
        var output = new StringWriter();

        var ex = Assert.Throws<TagWriteException>(() => TagWriter.Write(tag, Release, output));

        Assert.Contains("read", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verified", output.ToString());
    }

    [Fact]
    public void Empty_read_back_is_a_failure()
    {
        var tag = new FakeTag { ReadBackOverride = [] };
        var output = new StringWriter();

        Assert.Throws<TagWriteException>(() => TagWriter.Write(tag, Release, output));

        Assert.DoesNotContain("Verified", output.ToString());
    }

    [Fact]
    public void Mismatched_identity_on_read_back_is_a_failure()
    {
        var tag = new FakeTag { ReadBackOverride = [TagCodec.Format(Other)] };
        var output = new StringWriter();

        var ex = Assert.Throws<TagWriteException>(() => TagWriter.Write(tag, Release, output));

        Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verified", output.ToString());
    }

    [Fact]
    public void Unparseable_or_conflicting_read_back_is_a_failure()
    {
        foreach (var back in new[]
                 {
                     new[] { "garbage" },
                     new[] { "amp:album:1:mbid:zzz" },
                     new[] { TagCodec.Format(Release), TagCodec.Format(Other) },
                 })
        {
            var tag = new FakeTag { ReadBackOverride = back };
            var output = new StringWriter();

            Assert.Throws<TagWriteException>(() => TagWriter.Write(tag, Release, output));

            Assert.DoesNotContain("Verified", output.ToString());
        }
    }

    [Fact]
    public void Tag_is_never_locked_during_ordinary_programming()
    {
        Assert.DoesNotContain(
            typeof(ITagMedium).GetMethods(),
            m => m.Name.Contains("lock", StringComparison.OrdinalIgnoreCase));
    }
}
