using System.Text.Json;

namespace Amp.Nfc;

/// <summary>Source locator for one release plus display metadata. Only <see cref="NavidromeId"/> locates audio.</summary>
public sealed record CatalogueAlbum(string NavidromeId, string Artist, string Title);

public sealed record CatalogueResult(Catalogue? Catalogue, IReadOnlyList<FieldError> Errors);

/// <summary>
/// Release UUID to Navidrome album ID mappings read from the external catalogue file.
/// Error fields are paths into the file, e.g. <c>albums.&lt;uuid&gt;.navidromeId</c>.
/// </summary>
public sealed class Catalogue
{
    public const string SourceName = "navidrome";

    readonly Dictionary<ReleaseId, CatalogueAlbum> _albums;

    Catalogue(Dictionary<ReleaseId, CatalogueAlbum> albums) => _albums = albums;

    public int Count => _albums.Count;

    public CatalogueAlbum? Lookup(ReleaseId release) => _albums.GetValueOrDefault(release);

    public static CatalogueResult Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("catalogue", $"cannot read {path}: {ex.Message}");
        }
        return Parse(text);
    }

    public static CatalogueResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            return Fail("catalogue", $"is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("catalogue", "must be a JSON object with \"source\" and \"albums\"");

            var errors = new List<FieldError>();
            CheckSource(root, errors);

            var albums = new Dictionary<ReleaseId, CatalogueAlbum>();
            if (!root.TryGetProperty("albums", out var table))
                errors.Add(new("albums", "is required: an object keyed by release UUID"));
            else if (table.ValueKind != JsonValueKind.Object)
                errors.Add(new("albums", "must be an object keyed by release UUID"));
            else
                ReadAlbums(table, albums, errors);

            return errors.Count > 0 ? new(null, errors) : new(new Catalogue(albums), errors);
        }
    }

    static void CheckSource(JsonElement root, List<FieldError> errors)
    {
        if (!root.TryGetProperty("source", out var source))
            errors.Add(new("source", $"is required and must be \"{SourceName}\""));
        else if (source.ValueKind != JsonValueKind.String
                 || !string.Equals(source.GetString()?.Trim(), SourceName, StringComparison.OrdinalIgnoreCase))
            errors.Add(new("source", $"must be \"{SourceName}\"; this player only resolves Navidrome album IDs"));
    }

    static void ReadAlbums(JsonElement table, Dictionary<ReleaseId, CatalogueAlbum> albums, List<FieldError> errors)
    {
        foreach (var property in table.EnumerateObject())
        {
            var key = property.Name;
            var path = $"albums.{key}";

            if (!ReleaseId.TryParse(key, out var release))
            {
                errors.Add(new(path, "key must be a non-empty release UUID"));
                continue;
            }

            var entry = property.Value;
            var before = errors.Count;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new(path, "must be an object with navidromeId, artist and title"));
                continue;
            }

            var id = Text(entry, path, "navidromeId", errors);
            var artist = Text(entry, path, "artist", errors);
            var title = Text(entry, path, "title", errors);

            if (albums.ContainsKey(release))
            {
                errors.Add(new(path, $"duplicate release {release}"));
                continue;
            }

            if (errors.Count == before)
                albums[release] = new(id!, artist!, title!);
        }
    }

    static string? Text(JsonElement entry, string path, string name, List<FieldError> errors)
    {
        var field = $"{path}.{name}";
        if (!entry.TryGetProperty(name, out var value))
        {
            errors.Add(new(field, "is required"));
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add(new(field, "must be a non-blank string"));
            return null;
        }
        return value.GetString()!.Trim();
    }

    static CatalogueResult Fail(string field, string message) => new(null, [new(field, message)]);
}
