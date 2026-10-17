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
            """{"subsonic-response":{"status":"failed","error":{"code":10,"message":"x"}}}""")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));
    }

    [Fact]
    public async Task Failed_authentication_is_transient_and_credential_free()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Applier(() => Json(
            """{"subsonic-response":{"status":"failed","error":{"code":40,"message":"x"}}}""")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.IsNotType<SelectionRejectedException>(ex);
        Assert.IsType<NavidromeAuthenticationException>(ex);
        Assert.DoesNotContain("t=", ex.Message);
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

    [Fact]
    public async Task Malformed_server_data_is_transient_not_a_rejection()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => Applier(() => Json("<html>proxy error</html>")).PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.IsNotType<SelectionRejectedException>(ex);
        Assert.IsType<NavidromeMalformedException>(ex);
    }

    [Fact]
    public async Task Metadata_fallback_is_reported_to_the_operator_and_still_prepares()
    {
        var output = new StringWriter();
        var options = LuciferOptions.Load(
            n => n switch { "NAVIDROME_URL" => "https://music.example.test", "NAVIDROME_USER" => "u", "NAVIDROME_PASSWORD" => "p", _ => null },
            Mode.Play).Options!;
        var applier = new PlayerAlbumApplier(_catalogue, new Navidrome(new HttpClient(new Stub(() => Json(
            """{"subsonic-response":{"status":"ok","album":{"song":[{"id":"x","title":"X","track":1},{"id":"y","title":"Y","track":1}]}}}"""))), options), null!, output);

        Assert.NotNull(await applier.PrepareAsync(ReleaseId.Parse(A), CancellationToken.None));

        Assert.Contains("metadata issue", output.ToString());
        Assert.DoesNotContain("t=", output.ToString());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class HeldTag : ISelectionReader
    {
        public ReaderObservation Poll() => ReaderObservation.Present("04AA", () => new TagReadResult(TagReadKind.Release, ReleaseId.Parse(A)));
    }

    // Auth failure goes through the real controller: it is retried with backoff (not suppressed like
    // a stale mapping) and logs a clear credential-free line. The fake clock stands in for time.
    [Fact]
    public async Task Controller_retries_authentication_failure_with_backoff_but_not_stale_mapping()
    {
        foreach (var (code, retried) in new[] { (40, true), (70, false) })
        {
            var requests = 0;
            var log = new List<string>();
            var clock = new Clock();
            var applier = Applier(() =>
            {
                requests++;
                return Json("""{"subsonic-response":{"status":"failed","error":{"code":CODE,"message":"x"}}}""".Replace("CODE", code.ToString()));
            });
            var controller = new SelectionController(
                new HeldTag(), applier,
                new SelectionPolicy(TimeSpan.FromSeconds(2), 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)),
                clock, log.Add);

            for (var i = 0; i < 40; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                await controller.StepAsync(CancellationToken.None);
                await controller.WhenIdleAsync();
            }

            if (retried)
            {
                Assert.Equal(4, requests); // bounded by MaxAttempts, spaced by backoff
                Assert.Contains(log, l => l.Contains("rejected the credentials") && l.Contains("Retrying"));
            }
            else
                Assert.Equal(1, requests);
            Assert.DoesNotContain(log, l => l.Contains("t=") || l.Contains("s="));
        }
    }
}
