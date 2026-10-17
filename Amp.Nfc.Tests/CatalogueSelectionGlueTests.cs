using System.Net;
using System.Text;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

/// <summary>Catalogue and server outcomes mapped onto the selection controller's rejected-versus-transient semantics.</summary>
public class CatalogueSelectionGlueTests : IDisposable
{
    private const string A = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    private const string C = "11111111-2222-4333-8444-555555555555";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lucifer-glue-" + Guid.NewGuid().ToString("N"));
    private readonly string _catalogue;

    public CatalogueSelectionGlueTests()
    {
        Directory.CreateDirectory(_dir);
        _catalogue = Path.Combine(_dir, "catalogue.json");
        File.WriteAllText(_catalogue,
            $$"""{ "source": "navidrome", "albums": { "{{A}}": { "navidromeId": "srv", "artist": "Ar", "title": "Ti" } } }""");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Stub(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply());
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Prepare never touches mpv, so none is needed to observe the outcome.
    private PlayerAlbumApplier Applier(Func<HttpResponseMessage> reply)
    {
        var options = LuciferOptions.Load(
            n => n switch { "NAVIDROME_URL" => "https://music.example.test", "NAVIDROME_USER" => "u", "NAVIDROME_PASSWORD" => "p", _ => null },
            Mode.Play).Options!;
        return new PlayerAlbumApplier(_catalogue, new Navidrome(new HttpClient(new Stub(reply)), options), null!, new StringWriter());
    }

    private const string Album = """{"subsonic-response":{"status":"ok","album":{"song":[{"id":"t1","title":"One","track":1}]}}}""";

    [Fact]
    public async Task Unknown_release_is_a_permanent_rejection()
    {
        var ex = await Assert.ThrowsAsync<SelectionRejectedException>(
            () => Applier(() => Json(Album)).PrepareAsync(ReleaseId.Parse(C), CancellationToken.None));

        Assert.Contains("not in the catalogue", ex.Message);
    }

    [Fact]
    public async Task Invalid_catalogue_file_is_a_permanent_rejection()
    {
        File.WriteAllText(_catalogue, "{ nope");

        var ex = await Assert.ThrowsAsync<SelectionRejectedException>(
            () => Applier(() => Json(Album)).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.Contains("invalid", ex.Message);
    }

    [Fact]
    public async Task Stale_mapping_is_a_permanent_rejection_naming_the_id()
    {
        var ex = await Assert.ThrowsAsync<SelectionRejectedException>(() => Applier(() => Json(
            """{"subsonic-response":{"status":"failed","error":{"code":70,"message":"x"}}}""")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.Contains("Stale mapping", ex.Message);
        Assert.Contains("srv", ex.Message);
    }

    [Fact]
    public async Task Other_server_refusal_is_a_permanent_rejection()
    {
        await Assert.ThrowsAsync<SelectionRejectedException>(() => Applier(() => Json(
            """{"subsonic-response":{"status":"failed","error":{"code":40,"message":"x"}}}""")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));
    }

    [Fact]
    public async Task Server_unavailable_is_transient_not_a_rejection()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => Applier(() => Json("", HttpStatusCode.ServiceUnavailable)).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.IsNotType<SelectionRejectedException>(ex);
    }

    [Fact]
    public async Task Transport_failure_is_transient_not_a_rejection()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => Applier(() => throw new HttpRequestException("down")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.IsNotType<SelectionRejectedException>(ex);
    }

    [Fact]
    public async Task Album_without_tracks_is_rejected_and_good_album_prepares()
    {
        await Assert.ThrowsAsync<SelectionRejectedException>(() => Applier(() => Json(
            """{"subsonic-response":{"status":"ok","album":{"song":[]}}}""")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.NotNull(await Applier(() => Json(Album)).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));
    }
}
