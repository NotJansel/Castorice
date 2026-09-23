using System.Net;
using System.Text;
using System.Text.Json;
using Castorice.Core.Osu;
using Castorice.Core.Osu.Models;

namespace Castorice.Core.Tests;

public class OsuApiClientTests
{
    /// <summary>
    /// A score as the API returns it with x-api-version 20220705: mods are objects, some carrying
    /// settings, the score is total_score/legacy_total_score and the date is ended_at. Stable scores
    /// also carry the Classic marker.
    /// </summary>
    private const string CurrentFormatScores = """
        [
          {
            "id": 123,
            "accuracy": 0.9812,
            "max_combo": 1204,
            "mods": [ { "acronym": "HD" }, { "acronym": "DT", "settings": { "speed_change": 1.5 } }, { "acronym": "CL" } ],
            "passed": true,
            "pp": 412.7,
            "rank": "SH",
            "total_score": 812345,
            "legacy_total_score": 25413307,
            "legacy_perfect": false,
            "ended_at": "2026-09-20T18:04:11Z",
            "statistics": { "great": 900, "ok": 12, "meh": 1, "miss": 2 },
            "beatmap": { "id": 4, "version": "Overkill" },
            "beatmapset": { "id": 9, "artist": "Kobaryo", "title": "Ironclad" }
          }
        ]
        """;

    private const string LegacyFormatScores = """
        [ { "accuracy": 0.95, "mods": ["HD", "HR"], "passed": true, "pp": 300, "rank": "A",
            "score": 1234567, "perfect": true, "created_at": "2020-01-01T00:00:00Z" } ]
        """;

    private static OsuApiClient ClientReturning(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var client = new OsuApiClient(new HttpClient(new CannedHandler(body, status)));
        client.Configure("id", "secret");
        return client;
    }

    [Fact]
    public async Task Reads_scores_in_the_current_api_format()
    {
        // Regression: the profile view crashed the whole app on exactly this payload, because
        // mods arrived as objects while the model expected strings.
        using var client = ClientReturning(CurrentFormatScores);

        var score = Assert.Single(await client.GetUserScoresAsync(2, ScoreType.Best));

        Assert.Equal(["HD", "DT"], score.Mods);
        Assert.Equal("HDDT", score.ModsDisplay);
        Assert.Equal(25413307, score.Score);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 18, 4, 11, TimeSpan.Zero), score.PlayedAt);
        Assert.Equal("Kobaryo - Ironclad", score.Title);
    }

    [Fact]
    public async Task Still_reads_scores_in_the_legacy_format()
    {
        using var client = ClientReturning(LegacyFormatScores);

        var score = Assert.Single(await client.GetUserScoresAsync(2, ScoreType.Best));

        Assert.Equal(["HD", "HR"], score.Mods);
        Assert.Equal(1234567, score.Score);
        Assert.True(score.Perfect);
        Assert.NotNull(score.PlayedAt);
    }

    [Fact]
    public async Task An_unreadable_response_becomes_an_api_error_rather_than_a_crash()
    {
        // Mods as a number fits neither shape; this has to reach the UI as a message.
        using var client = ClientReturning("""[ { "mods": [ 42 ] } ]""");

        var ex = await Assert.ThrowsAsync<OsuApiException>(() => client.GetUserScoresAsync(2, ScoreType.Best));

        Assert.Contains("cannot read", ex.Message);
    }

    [Fact]
    public async Task A_timeout_becomes_an_api_error()
    {
        var client = new OsuApiClient(new HttpClient(new StallingHandler()));
        client.Configure("id", "secret");

        // The client sets its own 30 s timeout; the handler throws what HttpClient throws for one.
        var ex = await Assert.ThrowsAsync<OsuApiException>(() => client.GetUserScoresAsync(2, ScoreType.Best));

        Assert.Contains("in time", ex.Message);
        client.Dispose();
    }

    [Fact]
    public void A_score_set_on_lazer_falls_back_to_total_score()
    {
        var score = JsonSerializer.Deserialize<OsuScore>("""{ "total_score": 900000, "legacy_total_score": 0 }""")!;

        Assert.Equal(900000, score.Score);
    }

    [Fact]
    public void An_empty_or_missing_mod_list_reads_as_no_mods()
    {
        Assert.Empty(JsonSerializer.Deserialize<OsuScore>("""{ "mods": [] }""")!.Mods);
        Assert.Empty(JsonSerializer.Deserialize<OsuScore>("""{ "mods": null }""")!.Mods);
        Assert.Equal("NM", JsonSerializer.Deserialize<OsuScore>("{}")!.ModsDisplay);
    }

    [Fact]
    public void The_classic_marker_is_not_shown_as_a_mod()
    {
        var score = JsonSerializer.Deserialize<OsuScore>("""{ "mods": [ { "acronym": "CL" } ] }""")!;

        Assert.Equal("NM", score.ModsDisplay);
    }

    /// <summary>Answers the token request, then every API call with one canned body.</summary>
    private sealed class CannedHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal)
                ? """{ "access_token": "token", "expires_in": 86400 }"""
                : body;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Behaves like HttpClient does when its own timeout fires.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }
}
