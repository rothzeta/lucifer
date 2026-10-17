using System.Net;
using System.Text;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

/// <summary>
/// Navidrome source boundary against a fake HTTP handler and hand-written fixtures in the shape of
/// the OpenSubsonic getAlbum response (A07, A14). These fixtures are not captured from a real
/// Navidrome: real-server envelope, error-code and metadata behaviour remain unverified until LUC-08/A22.
/// </summary>
public class NavidromeSourceTests
{
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.OriginalString);
            return reply(request, cancellationToken);
        }
    }

    private static Task<HttpResponseMessage> Ok(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static Navidrome Server(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply, out Stub stub, string? timeout = null)
    {
        stub = new Stub(reply);
        var options = LuciferOptions.Load(
            n => n switch
            {
                "NAVIDROME_URL" => "https://music.example.test",
                "NAVIDROME_USER" => "listener",
                "NAVIDROME_PASSWORD" => "hunter2-secret",
                "LUCIFER_HTTP_TIMEOUT_SECONDS" => timeout,
                _ => null,
            }, Mode.Play).Options!;
        return new Navidrome(new HttpClient(stub), options);
    }

    private static Navidrome Server(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Server((_, _) => Ok(body, status), out _);

    private static string Album(params string[] songs) =>
        """{"subsonic-response":{"status":"ok","version":"1.16.1","album":{"id":"al","name":"N","song":[SONGS]}}}""".Replace("SONGS", string.Join(",", songs));

    private static string Song(string id, int? disc, int? track, string? title = null) =>
        "{" + $"\"id\":\"{id}\",\"title\":\"{title ?? "T" + id}\"" +
        (disc is { } d ? $",\"discNumber\":{d}" : "") +
        (track is { } t ? $",\"track\":{t}" : "") + "}";

    private static string Ids(AlbumTracks r) => string.Join(",", r.Tracks.Select(t => t.Id));

    // ---- ordering: three representative data sets ------------------------------------------

    [Fact]
    public async Task Shuffled_complete_single_disc_positions_come_back_in_track_order()
    {
        var r = await Server(Album(Song("c", 1, 3), Song("a", 1, 1), Song("d", 1, 4), Song("b", 1, 2)))
            .GetTracksAsync("al", CancellationToken.None);

        Assert.Equal("a,b,c,d", Ids(r));
        Assert.True(r.OrderedByPosition);
        Assert.Null(r.MetadataIssue);
    }

    [Fact]
    public async Task Multi_disc_album_orders_disc_then_track_and_track_one_on_each_disc_is_not_a_duplicate()
    {
        var r = await Server(Album(
                Song("d2t2", 2, 2), Song("d1t2", 1, 2), Song("d2t1", 2, 1), Song("d1t1", 1, 1), Song("d3t1", 3, 1)))
            .GetTracksAsync("al", CancellationToken.None);

        Assert.Equal("d1t1,d1t2,d2t1,d2t2,d3t1", Ids(r));
        Assert.True(r.OrderedByPosition);
        Assert.Null(r.MetadataIssue);
    }

    [Fact]
    public async Task Album_with_no_disc_tags_at_all_is_single_disc_ordered_by_track()
    {
        var r = await Server(Album(Song("z", null, 2), Song("y", null, 1), Song("x", null, 10)))
            .GetTracksAsync("al", CancellationToken.None);

        Assert.Equal("y,z,x", Ids(r));
        Assert.True(r.OrderedByPosition);
    }

    // ---- documented fallback: server order, each song once, issue reported ------------------

    public static TheoryData<string, string> FallbackCases() => new()
    {
        { Album(Song("s1", 1, 3), Song("s2", 1, null), Song("s3", 1, 1)), "lack a valid" },                 // absent track
        { Album(Song("s1", 1, 3), Song("s2", 1, 0), Song("s3", 1, 1)), "lack a valid" },                    // invalid (zero) track
        { Album(Song("s1", 1, 3), Song("s2", 1, -2), Song("s3", 1, 1)), "lack a valid" },                   // invalid (negative) track
        { Album(Song("s1", 1, 3), Song("s2", 1, 1), Song("s3", 1, 1)), "more than once" },                  // duplicate pair
        { Album(Song("s1", 2, 1), Song("s2", 1, 2), Song("s3", null, 1)), "lack a valid" },                 // disc missing on some songs only
        { Album(Song("s1", 1, 1), Song("s2", 1, 2), Song("s3", 0, 3)), "lack a valid" },                    // invalid disc
        {
            """{"subsonic-response":{"status":"ok","album":{"song":[{"id":"s1","title":"a","discNumber":1,"track":2},{"id":"s2","title":"b","discNumber":1,"track":"two"},{"id":"s3","title":"c","discNumber":1,"track":1.5}]}}}""",
            "lack a valid"                                                                                     // non-integer tracks
        },
    };

    [Theory]
    [MemberData(nameof(FallbackCases))]
    public async Task Incomplete_or_duplicate_positions_keep_server_order_and_report_the_issue(string body, string issue)
    {
        var r = await Server(body).GetTracksAsync("al", CancellationToken.None);

        Assert.Equal("s1,s2,s3", Ids(r));
        Assert.False(r.OrderedByPosition);
        Assert.Contains(issue, r.MetadataIssue);
        Assert.Contains("server's album order", r.MetadataIssue);
    }

    [Fact]
    public async Task Duplicate_positions_on_one_disc_do_not_drop_or_repeat_songs()
    {
        var r = await Server(Album(Song("s1", 1, 2), Song("s2", 1, 2), Song("s3", 2, 2), Song("s4", 1, 1)))
            .GetTracksAsync("al", CancellationToken.None);

        Assert.Equal(["s1", "s2", "s3", "s4"], r.Tracks.Select(t => t.Id).Order().ToArray());
        Assert.Equal("s1,s2,s3,s4", Ids(r));
        Assert.False(r.OrderedByPosition);
    }

    // ---- envelope outcomes ------------------------------------------------------------------

    [Fact]
    public async Task Failed_status_with_http_200_is_read_before_any_album_data()
    {
        // Contains an album that would otherwise parse; status wins.
        var body = """{"subsonic-response":{"status":"failed","error":{"code":70,"message":"x"},"album":{"song":[{"id":"a","title":"t"}]}}}""";

        await Assert.ThrowsAsync<StaleMappingException>(() => Server(body).GetTracksAsync("al", CancellationToken.None));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(44)]
    [InlineData(50)]
    public async Task Failed_authentication_is_its_own_outcome_and_message_is_credential_free(int code)
    {
        var body = """{"subsonic-response":{"status":"failed","error":{"code":CODE,"message":"Wrong username or password"}}}""".Replace("CODE", code.ToString());

        var ex = await Assert.ThrowsAsync<NavidromeAuthenticationException>(() => Server(body).GetTracksAsync("al", CancellationToken.None));

        Assert.Equal(typeof(NavidromeAuthenticationException), ex.GetType());
        Assert.Contains(code.ToString(), ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Fact]
    public async Task Unknown_album_is_permanent_and_names_the_id()
    {
        var ex = await Assert.ThrowsAsync<StaleMappingException>(() => Server(
            """{"subsonic-response":{"status":"failed","error":{"code":70}}}""").GetTracksAsync("gone", CancellationToken.None));

        Assert.Equal("gone", ex.AlbumId);
        Assert.IsAssignableFrom<NavidromeRejectedException>(ex);
    }

    [Fact]
    public async Task Other_failed_codes_remain_permanent_rejections()
    {
        var ex = await Assert.ThrowsAsync<NavidromeRejectedException>(() => Server(
            """{"subsonic-response":{"status":"failed","error":{"code":10,"message":"missing"}}}""").GetTracksAsync("al", CancellationToken.None));

        Assert.Contains("10", ex.Message);
    }

    [Theory]
    [InlineData("""{"subsonic-response":{"status":"ok","album":{"id":"al"}}}""")]
    [InlineData("""{"subsonic-response":{"status":"ok","album":{"song":[]}}}""")]
    public async Task Album_with_no_songs_is_a_permanent_no_tracks_outcome(string body)
    {
        var ex = await Assert.ThrowsAsync<NoTracksException>(() => Server(body).GetTracksAsync("al", CancellationToken.None));

        Assert.IsAssignableFrom<NavidromeRejectedException>(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"subsonic-response":[]}""")]
    [InlineData("""{"subsonic-response":{}}""")]
    [InlineData("""{"subsonic-response":{"status":"weird"}}""")]
    [InlineData("""{"subsonic-response":{"status":"ok"}}""")]
    [InlineData("""{"subsonic-response":{"status":"ok","album":{"song":"nope"}}}""")]
    [InlineData("""{"subsonic-response":{"status":"ok","album":{"song":[{"title":"no id"}]}}}""")]
    [InlineData("""{"subsonic-response":{"status":"ok","album":{"song":[7]}}}""")]
    [InlineData("""{"subsonic-response":{"status":"failed"}}""")]
    [InlineData("""{"subsonic-response":{"status":"failed","error":{"code":"x"}}}""")]
    public async Task Malformed_answers_are_a_transient_malformed_outcome(string body)
    {
        var ex = await Assert.ThrowsAsync<NavidromeMalformedException>(() => Server(body).GetTracksAsync("al", CancellationToken.None));

        Assert.Equal(typeof(NavidromeMalformedException), ex.GetType());
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    // ---- transport, deadline, cancellation ---------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_success_http_status_is_a_transport_outcome(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<NavidromeTransportException>(() => Server("", status).GetTracksAsync("al", CancellationToken.None));

        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public async Task Connection_failure_is_a_transport_outcome_without_url_or_credentials_in_the_message()
    {
        var server = Server((req, _) => throw new HttpRequestException($"cannot connect to {req.RequestUri}"), out _);

        var ex = await Assert.ThrowsAsync<NavidromeTransportException>(() => server.GetTracksAsync("al", CancellationToken.None));

        Assert.DoesNotContain("music.example.test", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
        Assert.DoesNotContain("t=", ex.Message);
    }

    [Fact]
    public async Task Unanswered_request_ends_at_the_deadline_as_a_transport_outcome()
    {
        var server = Server(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; }, out _, timeout: "1");

        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<NavidromeTransportException>(() => server.GetTracksAsync("al", CancellationToken.None));

        Assert.Contains("in time", ex.Message);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_as_cancellation_not_a_source_outcome()
    {
        var server = Server(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; }, out var stub);
        using var cts = new CancellationTokenSource();

        var call = server.GetTracksAsync("al", cts.Token);
        while (stub.Urls.Count == 0)
            await Task.Delay(5);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    // ---- request shape unchanged ------------------------------------------------------------

    [Fact]
    public async Task Request_keeps_token_authentication_and_never_sends_the_password()
    {
        var server = Server((_, _) => Ok(Album(Song("a", 1, 1))), out var stub);

        await server.GetTracksAsync("al id", CancellationToken.None);

        var url = Assert.Single(stub.Urls);
        Assert.StartsWith("https://music.example.test/rest/getAlbum.view?u=listener&t=", url);
        Assert.Contains("&s=", url);
        Assert.Contains("&f=json&id=al%20id", url);
        Assert.DoesNotContain("hunter2", url);
    }
}
