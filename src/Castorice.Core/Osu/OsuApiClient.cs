using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Castorice.Core.Osu.Models;

namespace Castorice.Core.Osu;

public enum ScoreType
{
    Best,
    Recent,
    Firsts,
}

/// <summary>
/// A read-only osu! API v2 client using the client-credentials grant, which is all the profile
/// view needs. Tokens are refreshed automatically a minute before they expire.
/// </summary>
public sealed class OsuApiClient : IDisposable
{
    private const string BaseUrl = "https://osu.ppy.sh/api/v2/";
    private const string TokenUrl = "https://osu.ppy.sh/oauth/token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private string? _clientId;
    private string? _clientSecret;
    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public OsuApiClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    public void Configure(string clientId, string clientSecret)
    {
        _clientId = clientId?.Trim();
        _clientSecret = clientSecret?.Trim();
        _token = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }

    /// <summary>Looks a user up by username or numeric id. Returns <c>null</c> when they do not exist.</summary>
    public async Task<OsuUser?> GetUserAsync(
        string userOrId,
        string mode = "osu",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userOrId);

        var key = long.TryParse(userOrId, out _) ? "id" : "username";
        var path = $"users/{Uri.EscapeDataString(userOrId.Trim())}/{mode}?key={key}";

        return await GetAsync<OsuUser>(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OsuScore>> GetUserScoresAsync(
        long userId,
        ScoreType type,
        string mode = "osu",
        int limit = 10,
        bool includeFails = false,
        CancellationToken cancellationToken = default)
    {
        var path =
            $"users/{userId}/scores/{type.ToString().ToLowerInvariant()}" +
            $"?mode={mode}&limit={Math.Clamp(limit, 1, 100)}&include_fails={(includeFails ? 1 : 0)}";

        return await GetAsync<List<OsuScore>>(path, cancellationToken).ConfigureAwait(false) ?? [];
    }

    public Task<OsuBeatmap?> GetBeatmapAsync(long beatmapId, CancellationToken cancellationToken = default) =>
        GetAsync<OsuBeatmap>($"beatmaps/{beatmapId}", cancellationToken);

    /// <summary>
    /// Fetches several beatmaps in one request. The endpoint caps at 50 ids, so larger pools are
    /// split across calls.
    /// </summary>
    public async Task<IReadOnlyList<OsuBeatmap>> GetBeatmapsAsync(
        IEnumerable<long> beatmapIds,
        CancellationToken cancellationToken = default)
    {
        var ids = beatmapIds.Distinct().Where(id => id > 0).ToList();
        var results = new List<OsuBeatmap>(ids.Count);

        foreach (var chunk in ids.Chunk(50))
        {
            var query = string.Join('&', chunk.Select(id => $"ids[]={id}"));
            var page = await GetAsync<BeatmapListResponse>($"beatmaps?{query}", cancellationToken)
                .ConfigureAwait(false);

            if (page?.Beatmaps is { Count: > 0 })
            {
                results.AddRange(page.Beatmaps);
            }
        }

        return results;
    }

    public void Dispose()
    {
        _tokenGate.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-api-version", "20220705");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return default;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            // The cached token was rejected; drop it so the next call re-authenticates.
            _token = null;
            _tokenExpiry = DateTimeOffset.MinValue;
            throw new OsuApiException("The osu! API rejected the access token. Check the client id and secret.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OsuApiException($"The osu! API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new OsuApiException(
                "No osu! API credentials are configured. Add a client id and secret in Settings to use the profile view.");
        }

        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _token;
        }

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _token;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId!,
                ["client_secret"] = _clientSecret!,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            });

            using var response = await _http.PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new OsuApiException(
                    $"Could not obtain an osu! API token ({(int)response.StatusCode} {response.ReasonPhrase}). " +
                    "Check the client id and secret.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<TokenResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (payload?.AccessToken is not { Length: > 0 })
            {
                throw new OsuApiException("The osu! API returned an empty access token.");
            }

            _token = payload.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(payload.ExpiresIn - 60, 60));
            return _token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record BeatmapListResponse
    {
        [JsonPropertyName("beatmaps")]
        public List<OsuBeatmap>? Beatmaps { get; init; }
    }
}

public sealed class OsuApiException(string message) : Exception(message);
