using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Castorice.Core.MappoolBuilder;

/// <summary>Where the Mappool Builder lives and the API token to use there, if any.</summary>
public sealed record MappoolBuilderConnection(string BaseUrl, string? Token = null)
{
    public const string DefaultBaseUrl = "https://pools.jansel.dev";

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    /// <summary>
    /// The base URL as an absolute origin without a trailing slash. A bare host such as
    /// <c>pools.jansel.dev</c> is taken as https.
    /// </summary>
    public Uri Origin
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
            if (!text.Contains("://", StringComparison.Ordinal))
            {
                text = "https://" + text;
            }

            if (!Uri.TryCreate(text.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                throw new MappoolBuilderException($"\"{BaseUrl}\" is not a web address.");
            }

            return uri;
        }
    }

    /// <summary>The host alone, for messages such as "Imported from pools.jansel.dev".</summary>
    public string Host => Origin.Authority;
}

public sealed class MappoolBuilderException(string message, HttpStatusCode? statusCode = null)
    : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>
/// Reads pools from the Mappool Builder. Castorice only ever reads, so a <c>read</c> token is all
/// it needs, and public pools need none at all.
/// </summary>
public sealed partial class MappoolBuilderClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly string UserAgent =
        $"Castorice/{typeof(MappoolBuilderClient).Assembly.GetName().Version?.ToString(3) ?? "0"}";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public MappoolBuilderClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>
    /// Who the token belongs to, or <c>null</c> without a token. Used as the connection test.
    /// </summary>
    public async Task<RemoteUser?> GetMeAsync(
        MappoolBuilderConnection connection,
        CancellationToken cancellationToken = default)
    {
        var session = await GetAsync<RemoteSession>(connection, "api/me", cancellationToken).ConfigureAwait(false);
        return session.User;
    }

    /// <summary>The caller's own pools (with a token) and recently changed public ones.</summary>
    public Task<RemotePoolList> ListPoolsAsync(
        MappoolBuilderConnection connection,
        CancellationToken cancellationToken = default) =>
        GetAsync<RemotePoolList>(connection, "api/pools", cancellationToken);

    /// <summary>One pool with its slots. Takes a pool id or any link that contains one.</summary>
    public Task<RemotePool> GetPoolAsync(
        MappoolBuilderConnection connection,
        string poolIdOrLink,
        CancellationToken cancellationToken = default)
    {
        var id = ParsePoolId(poolIdOrLink)
            ?? throw new MappoolBuilderException(
                $"\"{poolIdOrLink.Trim()}\" is not a pool id or pool link. A pool id has 8 characters, e.g. Xb3kQ9aZ.");

        return GetAsync<RemotePool>(connection, $"api/pools/{Uri.EscapeDataString(id)}", cancellationToken);
    }

    /// <summary>
    /// Pulls the pool id out of what a user pastes: the id itself, the pool's page
    /// (<c>…/pools/Xb3kQ9aZ</c>) or its API address (<c>…/api/pools/Xb3kQ9aZ</c>).
    /// </summary>
    public static string? ParsePoolId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (PoolIdPattern().IsMatch(text))
        {
            return text;
        }

        var match = PoolLinkPattern().Match(text);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Every failure surfaces as <see cref="MappoolBuilderException"/> with a message fit for the
    /// status bar. The token never appears in one.
    /// </summary>
    private async Task<T> GetAsync<T>(
        MappoolBuilderConnection connection,
        string path,
        CancellationToken cancellationToken)
        where T : class
    {
        var origin = connection.Origin;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Error messages and defaults come back in English, matching the rest of the app.
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        if (connection.HasToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token!.Trim());
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorFromAsync(response, connection, cancellationToken).ConfigureAwait(false);
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
                ?? throw new MappoolBuilderException($"{connection.Host} sent an empty answer.");
        }
        catch (JsonException ex)
        {
            throw new MappoolBuilderException(
                $"{connection.Host} answered in a shape Castorice cannot read ({ex.Path ?? "unknown field"}). " +
                "Is the address right?");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation nobody asked for.
            throw new MappoolBuilderException($"{connection.Host} did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new MappoolBuilderException($"Could not reach {connection.Host}: {ex.Message}");
        }
    }

    private static async Task<MappoolBuilderException> ErrorFromAsync(
        HttpResponseMessage response,
        MappoolBuilderConnection connection,
        CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        var serverMessage = await ReadServerMessageAsync(response, cancellationToken).ConfigureAwait(false);

        var message = status switch
        {
            HttpStatusCode.Unauthorized =>
                "The API token was rejected — it may have been revoked. Create a new one under API tokens " +
                $"on {connection.Host}, or clear the token to read public pools only.",
            HttpStatusCode.Forbidden =>
                $"{connection.Host} refused the request with this token.",
            HttpStatusCode.NotFound =>
                "No such pool — or it is private and the API token does not belong to its owner.",
            HttpStatusCode.BadGateway =>
                $"{connection.Host} could not reach the osu! API. Try again in a moment.",
            _ when (int)status >= 500 =>
                $"{connection.Host} had a problem ({(int)status}). Try again in a moment.",
            _ => serverMessage ?? $"{connection.Host} answered {(int)status} {response.ReasonPhrase}.",
        };

        return new MappoolBuilderException(message, status);
    }

    private static async Task<string?> ReadServerMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<RemoteError>(Json, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(error?.StatusMessage) ? null : error.StatusMessage;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // A proxy's HTML error page, say. The status code alone will have to do.
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8}$")]
    private static partial Regex PoolIdPattern();

    [GeneratedRegex(@"/pools/(?<id>[A-Za-z0-9_-]{8})(?:[/?#]|$)")]
    private static partial Regex PoolLinkPattern();
}
