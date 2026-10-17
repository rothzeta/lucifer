using System.Net;
using System.Text;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

/// <summary>Stale-mapping classification against a stubbed HTTP boundary. Real server semantics are LUC-06/LUC-12.</summary>
public class StaleMappingTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static Navidrome Server(HttpStatusCode status, string body)
    {
        var options = LuciferOptions.Load(
            name => name switch
            {
                "NAVIDROME_URL" => "https://music.example.test",
                "NAVIDROME_USER" => "u",
                "NAVIDROME_PASSWORD" => "p",
                _ => null,
            }, Mode.Play).Options!;
        return new Navidrome(new HttpClient(new Stub(status, body)), options);
    }

    [Fact]
    public async Task Server_reporting_album_not_found_is_a_stale_mapping()
    {
        var server = Server(HttpStatusCode.OK,
            """{"subsonic-response":{"status":"failed","version":"1.16.1","error":{"code":70,"message":"Album not found"}}}""");

        var ex = await Assert.ThrowsAsync<StaleMappingException>(() => server.GetTracksAsync("gone", CancellationToken.None));

        Assert.Equal("gone", ex.AlbumId);
    }

    [Fact]
    public async Task Other_server_failures_are_not_called_stale()
    {
        var server = Server(HttpStatusCode.OK,
            """{"subsonic-response":{"status":"failed","error":{"code":40,"message":"Wrong username or password"}}}""");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => server.GetTracksAsync("x", CancellationToken.None));

        Assert.IsNotType<StaleMappingException>(ex);
        Assert.Contains("40", ex.Message);
    }
}
