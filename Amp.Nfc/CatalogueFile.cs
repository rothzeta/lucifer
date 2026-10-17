namespace Amp.Nfc;

public enum CatalogueLookupKind { Found, Unknown, Invalid }

public sealed record CatalogueLookup(CatalogueLookupKind Kind, CatalogueAlbum? Album, string Message);

/// <summary>
/// Resolves a release against the catalogue file as it is now, so an edited file takes effect
/// on the next tap without a restart or rebuild.
/// </summary>
public static class CatalogueFile
{
    public static CatalogueLookup Resolve(string path, ReleaseId release)
    {
        var loaded = Catalogue.Load(path);

        if (loaded.Catalogue is not { } catalogue)
            return new(CatalogueLookupKind.Invalid, null, Describe(path, loaded.Errors));

        return catalogue.Lookup(release) is { } album
            ? new(CatalogueLookupKind.Found, album, $"{album.Artist} — {album.Title}")
            : new(CatalogueLookupKind.Unknown, null, $"Release {release} is not in the catalogue {path}.");
    }

    public static string Describe(string path, IReadOnlyList<FieldError> errors) =>
        $"Catalogue {path} is invalid: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}"));
}
