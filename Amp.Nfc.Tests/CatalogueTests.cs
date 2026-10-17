using Amp.Nfc;

namespace Amp.Nfc.Tests;

public class CatalogueTests : IDisposable
{
    private const string A = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    private const string B = "d5e2d971-e856-4342-ad09-0e6ad1b5241a";
    private const string C = "11111111-2222-4333-8444-555555555555";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lucifer-cat-" + Guid.NewGuid().ToString("N"));

    public CatalogueTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Entry(string key, string id = "srv-1", string artist = "Artist", string title = "Title") =>
        $$"""  "{{key}}": { "navidromeId": "{{id}}", "artist": "{{artist}}", "title": "{{title}}" }""";

    private static string Doc(params string[] entries) =>
        "{ \"source\": \"navidrome\", \"albums\": {\n" + string.Join(",\n", entries) + "\n} }";

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "catalogue.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Valid_catalogue_resolves_by_canonical_release_regardless_of_case()
    {
        var result = Catalogue.Parse(Doc(Entry(A.ToUpperInvariant(), "x1", "Mastodon", "Crack the Skye")));

        Assert.Empty(result.Errors);
        var album = result.Catalogue!.Lookup(ReleaseId.Parse(A));
        Assert.Equal(new CatalogueAlbum("x1", "Mastodon", "Crack the Skye"), album);
        Assert.Null(result.Catalogue.Lookup(ReleaseId.Parse(B)));
        Assert.Equal(1, result.Catalogue.Count);
    }

    [Fact]
    public void A_third_mapping_added_at_run_time_resolves_without_rebuild()
    {
        var path = Write(Doc(Entry(A), Entry(B)));
        Assert.Equal(CatalogueLookupKind.Unknown, CatalogueFile.Resolve(path, ReleaseId.Parse(C)).Kind);

        Write(Doc(Entry(A), Entry(B), Entry(C, "third-id", "New", "Album")));

        var found = CatalogueFile.Resolve(path, ReleaseId.Parse(C));
        Assert.Equal(CatalogueLookupKind.Found, found.Kind);
        Assert.Equal("third-id", found.Album!.NavidromeId);
    }

    [Fact]
    public void Changing_the_provider_id_needs_only_the_mapping()
    {
        var path = Write(Doc(Entry(A, "old-id")));
        Assert.Equal("old-id", CatalogueFile.Resolve(path, ReleaseId.Parse(A)).Album!.NavidromeId);

        Write(Doc(Entry(A, "new-id")));

        Assert.Equal("new-id", CatalogueFile.Resolve(path, ReleaseId.Parse(A)).Album!.NavidromeId);
    }

    [Fact]
    public void Unknown_release_gives_a_specific_result_naming_the_release()
    {
        var path = Write(Doc(Entry(A)));

        var r = CatalogueFile.Resolve(path, ReleaseId.Parse(C));

        Assert.Equal(CatalogueLookupKind.Unknown, r.Kind);
        Assert.Contains(C, r.Message);
        Assert.Contains("not in the catalogue", r.Message);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void Malformed_or_empty_keys_are_rejected_naming_the_key(string key)
    {
        var result = Catalogue.Parse(Doc(Entry(key)));

        Assert.Null(result.Catalogue);
        var e = Assert.Single(result.Errors);
        Assert.StartsWith("albums.", e.Field);
        Assert.Contains("release UUID", e.Message);
    }

    [Fact]
    public void Duplicate_keys_are_rejected_including_case_variants()
    {
        var result = Catalogue.Parse(Doc(Entry(A, "one"), Entry(B), Entry(A.ToUpperInvariant(), "two")));

        Assert.Null(result.Catalogue);
        var e = Assert.Single(result.Errors);
        Assert.Contains(A, e.Field, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate", e.Message);
    }

    [Theory]
    [InlineData("\"navidromeId\": \"\"", "navidromeId")]
    [InlineData("\"navidromeId\": \"  \"", "navidromeId")]
    [InlineData("\"navidromeId\": 7", "navidromeId")]
    [InlineData("\"title\": \"\"", "title")]
    [InlineData("\"artist\": \" \"", "artist")]
    public void Blank_or_wrongly_typed_fields_are_rejected_by_field(string replacement, string field)
    {
        var entries = new Dictionary<string, string>
        {
            ["navidromeId"] = "\"navidromeId\": \"srv\"",
            ["artist"] = "\"artist\": \"Ar\"",
            ["title"] = "\"title\": \"Ti\"",
        };
        entries[field] = replacement;
        var json = "{ \"source\": \"navidrome\", \"albums\": { \"" + A + "\": { " + string.Join(", ", entries.Values) + " } } }";

        var result = Catalogue.Parse(json);

        Assert.Null(result.Catalogue);
        var e = Assert.Single(result.Errors);
        Assert.Equal($"albums.{A}.{field}", e.Field);
    }

    [Fact]
    public void Missing_field_is_reported_by_field()
    {
        var json = "{ \"source\": \"navidrome\", \"albums\": { \"" + A + "\": { \"artist\": \"a\", \"title\": \"t\" } } }";

        var e = Assert.Single(Catalogue.Parse(json).Errors);

        Assert.Equal($"albums.{A}.navidromeId", e.Field);
        Assert.Contains("required", e.Message);
    }

    [Theory]
    [InlineData("{ \"albums\": {} }", "source", "required")]
    [InlineData("{ \"source\": \"plex\", \"albums\": {} }", "source", "navidrome")]
    [InlineData("{ \"source\": \"navidrome\" }", "albums", "required")]
    [InlineData("{ \"source\": \"navidrome\", \"albums\": [] }", "albums", "object")]
    [InlineData("[]", "catalogue", "object")]
    [InlineData("{ not json", "catalogue", "JSON")]
    [InlineData("", "catalogue", "JSON")]
    public void Source_identity_and_structure_errors_name_the_field(string json, string field, string text)
    {
        var result = Catalogue.Parse(json);

        Assert.Null(result.Catalogue);
        var e = Assert.Single(result.Errors);
        Assert.Equal(field, e.Field);
        Assert.Contains(text, e.Message);
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var result = Catalogue.Parse(Doc(Entry("bad"), Entry(A, id: " "), Entry(B)));

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Missing_file_and_invalid_file_are_distinct_outcomes_that_name_the_path()
    {
        var missing = CatalogueFile.Resolve(Path.Combine(_dir, "nope.json"), ReleaseId.Parse(A));
        Assert.Equal(CatalogueLookupKind.Invalid, missing.Kind);
        Assert.Contains("nope.json", missing.Message);

        var bad = CatalogueFile.Resolve(Write("{ nope"), ReleaseId.Parse(A));
        Assert.Equal(CatalogueLookupKind.Invalid, bad.Kind);
        Assert.Contains("catalogue.json", bad.Message);
    }

    [Fact]
    public void The_example_catalogue_is_valid_and_holds_the_two_migrated_mappings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalogue.example.json");
        Assert.True(File.Exists(path), path);

        var result = Catalogue.Load(path);

        Assert.Empty(result.Errors);
        Assert.Equal("0lkVdYFhXO6ubrfysEPzTd", result.Catalogue!.Lookup(ReleaseId.Parse(A))!.NavidromeId);
        Assert.Equal("0ckDdmbp5fa3ESbkUwskAF", result.Catalogue.Lookup(ReleaseId.Parse(B))!.NavidromeId);
        Assert.Equal(2, result.Catalogue.Count);
    }
}
