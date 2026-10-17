using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class TagCodecTests
{
    const string Id = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    const string Other = "d5e2d971-e856-4342-ad09-0e6ad1b5241a";

    static string Rec(string id) => $"amp:album:1:mbid:{id}";

    [Fact]
    public void Release_identity_formats_to_the_canonical_lowercase_record()
    {
        var release = ReleaseId.Parse(Id.ToUpperInvariant());

        Assert.Equal(Rec(Id), TagCodec.Format(release));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Invalid_release_identity_is_refused(string text)
    {
        Assert.False(ReleaseId.TryParse(text, out _));
        Assert.Throws<FormatException>(() => ReleaseId.Parse(text));
    }

    [Fact]
    public void Default_identity_cannot_be_formatted()
    {
        Assert.Throws<ArgumentException>(() => TagCodec.Format(default));
    }

    [Fact]
    public void Canonical_record_is_recognised()
    {
        var result = TagCodec.Read([Rec(Id)]);

        Assert.Equal(TagReadKind.Release, result.Kind);
        Assert.Equal(ReleaseId.Parse(Id), result.Release);
    }

    [Theory]
    [InlineData("AMP:ALBUM:1:MBID:A24495E4-EB70-48CD-B0FA-31A606459D26")]
    [InlineData("Amp:Album:1:Mbid:a24495E4-eb70-48cd-b0fa-31a606459d26")]
    public void Case_differences_are_accepted_and_normalised(string text)
    {
        var result = TagCodec.Read([text]);

        Assert.Equal(TagReadKind.Release, result.Kind);
        Assert.Equal(Id, result.Release.ToString());
    }

    [Theory]
    [InlineData("amp:album:1:mbid:")]
    [InlineData("amp:album:1:mbid:zzz")]
    [InlineData("amp:album:1:mbid:a24495e4eb7048cdb0fa31a606459d26")]
    [InlineData("amp:album:1:mbid:00000000-0000-0000-0000-000000000000")]
    [InlineData("amp:album:1:mbid:a24495e4-eb70-48cd-b0fa-31a606459d26 ")]
    [InlineData("amp:album:1:foo:a24495e4-eb70-48cd-b0fa-31a606459d26")]
    [InlineData("amp:album:1")]
    public void Malformed_records_are_rejected(string text)
    {
        Assert.Equal(TagReadKind.Invalid, TagCodec.Read([text]).Kind);
    }

    [Theory]
    [InlineData("amp:album:2:mbid:a24495e4-eb70-48cd-b0fa-31a606459d26")]
    [InlineData("amp:album:x:mbid:a24495e4-eb70-48cd-b0fa-31a606459d26")]
    public void Unknown_versions_are_rejected(string text)
    {
        var result = TagCodec.Read([text]);

        Assert.Equal(TagReadKind.Invalid, result.Kind);
        Assert.Contains("version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unrelated_text_before_the_identity_is_skipped()
    {
        var result = TagCodec.Read(["hello", "https://example.com", Rec(Id)]);

        Assert.Equal(TagReadKind.Release, result.Kind);
        Assert.Equal(ReleaseId.Parse(Id), result.Release);
    }

    [Fact]
    public void Tag_without_a_supported_record_reports_none()
    {
        Assert.Equal(TagReadKind.None, TagCodec.Read([]).Kind);
        Assert.Equal(TagReadKind.None, TagCodec.Read(["hello", "amp:other:1"]).Kind);
    }

    [Fact]
    public void Conflicting_identities_are_rejected_in_either_order()
    {
        Assert.Equal(TagReadKind.Conflict, TagCodec.Read([Rec(Id), Rec(Other)]).Kind);
        Assert.Equal(TagReadKind.Conflict, TagCodec.Read([Rec(Other), "x", Rec(Id)]).Kind);
    }

    [Fact]
    public void Repeated_identical_identity_is_not_a_conflict()
    {
        var result = TagCodec.Read([Rec(Id), Rec(Id.ToUpperInvariant())]);

        Assert.Equal(TagReadKind.Release, result.Kind);
    }

    [Fact]
    public void A_valid_record_beside_a_malformed_supported_record_is_rejected()
    {
        Assert.Equal(TagReadKind.Invalid, TagCodec.Read([Rec(Id), "amp:album:1:mbid:zzz"]).Kind);
        Assert.Equal(TagReadKind.Invalid, TagCodec.Read(["amp:album:9:mbid:" + Id, Rec(Id)]).Kind);
    }
}
