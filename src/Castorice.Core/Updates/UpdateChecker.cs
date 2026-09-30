using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Castorice.Core.Updates;

/// <summary>The system and processor a build is for, which decides the installer to fetch.</summary>
public enum UpdatePlatform
{
    Unknown,
    WindowsX64,
    WindowsArm64,
    MacArm64,
    MacX64,
    LinuxX64,
    LinuxArm64,
}

public sealed class UpdateAsset
{
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>GitHub's checksum of the file, e.g. <c>sha256:4f…</c>; absent on older releases.</summary>
    public string? Digest { get; set; }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    public string? Name { get; set; }

    [JsonPropertyName("html_url")]
    public string PageUrl { get; set; } = string.Empty;

    public string? Body { get; set; }

    public bool Draft { get; set; }

    public bool Prerelease { get; set; }

    public List<UpdateAsset> Assets { get; set; } = [];
}

/// <summary>A newer release than the running one, with the file for this platform if it has one.</summary>
public sealed record UpdateInfo(ReleaseVersion Version, GitHubRelease Release, UpdateAsset? Asset)
{
    public string PageUrl => Release.PageUrl;

    public string Notes => Release.Body ?? string.Empty;
}

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Looks for a newer release on GitHub and downloads its installer. Only published releases
/// count: drafts and pre-releases are never offered.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _repository;
    private readonly string _apiBase;

    /// <param name="repository">The GitHub repository as <c>owner/name</c>.</param>
    /// <param name="apiBase">The GitHub API; only ever changed to test against a stand-in.</param>
    public UpdateChecker(string repository, HttpClient? httpClient = null, string apiBase = "https://api.github.com")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiBase);

        _repository = repository.Trim('/');
        _apiBase = apiBase.TrimEnd('/');
        _ownsHttpClient = httpClient is null;

        // Time limits come from RequestTimeout and StallTimeout per call, so a download may take as
        // long as it needs while it keeps moving. A client passed in is left as it is.
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>How long GitHub may take to answer a request, before any download starts.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a download may go without receiving anything before it is given up.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The latest release when it is newer than <paramref name="current"/>; <c>null</c> when the
    /// app is up to date or the repository has no release yet.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(
        ReleaseVersion current,
        UpdatePlatform platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_apiBase}/repos/{_repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"Castorice/{current}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        GitHubRelease? release;
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            // No published release yet — or the repository is private, which looks the same.
            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new UpdateException("GitHub is limiting requests right now; the next check will try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            release = await response.Content.ReadFromJsonAsync<GitHubRelease>(Json, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Could not reach GitHub: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new UpdateException("GitHub answered in a shape Castorice cannot read.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("GitHub did not answer in time.", ex);
        }

        if (release is null || release.Draft || release.Prerelease)
        {
            return null;
        }

        var version = ReleaseVersion.TryParse(release.TagName);
        if (version is null || version <= current)
        {
            return null;
        }

        return new UpdateInfo(version, release, PickAsset(release.Assets, platform));
    }

    /// <summary>
    /// The installer for a platform, matched on the names build/package-* give them: the setup
    /// program on Windows, the disk image on macOS and the AppImage on Linux.
    /// </summary>
    public static UpdateAsset? PickAsset(IEnumerable<UpdateAsset> assets, UpdatePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var suffix = platform switch
        {
            UpdatePlatform.WindowsX64 => "-windows-x64-setup.exe",
            UpdatePlatform.WindowsArm64 => "-windows-arm64-setup.exe",
            UpdatePlatform.MacArm64 => "-macos-arm64.dmg",
            UpdatePlatform.MacX64 => "-macos-x64.dmg",
            UpdatePlatform.LinuxX64 => "-linux-x86_64.AppImage",
            UpdatePlatform.LinuxArm64 => "-linux-aarch64.AppImage",
            _ => null,
        };

        return suffix is null
            ? null
            : assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Downloads the file to <paramref name="destination"/>, reporting progress from 0 to 1, and
    /// checks it against GitHub's checksum when the release carries one. A file that does not
    /// match is deleted and never handed on.
    /// </summary>
    public async Task DownloadAsync(
        UpdateAsset asset,
        string destination,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var partial = destination + ".part";

        // Cancelled when GitHub takes too long to answer, and later when the data stops coming.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("Castorice");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"The download failed: GitHub answered {(int)response.StatusCode}.");
            }

            var total = response.Content.Headers.ContentLength ?? asset.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long received = 0;

                while (true)
                {
                    timeout.CancelAfter(StallTimeout);
                    var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;

                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)received / total));
                    }
                }
            }

            VerifyDigest(asset, hash.GetHashAndReset());
            File.Move(partial, destination, overwrite: true);
            progress?.Report(1.0);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"The download failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("The download stopped moving and was given up; try again.", ex);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static void VerifyDigest(UpdateAsset asset, byte[] actual)
    {
        const string prefix = "sha256:";

        if (string.IsNullOrWhiteSpace(asset.Digest) ||
            !asset.Digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // Releases published before GitHub added checksums carry none; HTTPS is all there is.
            return;
        }

        var expected = asset.Digest[prefix.Length..].Trim();
        if (!string.Equals(Convert.ToHexString(actual), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException(
                $"The downloaded {asset.Name} does not match the checksum GitHub lists for it, so it was thrown away.");
        }
    }
}
