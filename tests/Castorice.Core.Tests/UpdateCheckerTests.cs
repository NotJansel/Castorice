using System.Net;
using System.Security.Cryptography;
using System.Text;
using Castorice.Core.Updates;

namespace Castorice.Core.Tests;

public class UpdateCheckerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "castorice-update-" + Guid.NewGuid().ToString("N"));

    public UpdateCheckerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ReleaseVersion V(string text) => ReleaseVersion.TryParse(text)!;

    private static string Release(string tag, bool prerelease = false, bool draft = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Castorice {{tag}}",
          "html_url": "https://github.com/NotJansel/Castorice/releases/tag/{{tag}}",
          "body": "What changed",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "Castorice-0.2.0-windows-x64-setup.exe", "browser_download_url": "https://example/setup.exe", "size": 10, "digest": "sha256:00" },
            { "name": "Castorice-0.2.0-windows-x64-portable.zip", "browser_download_url": "https://example/portable.zip", "size": 10 },
            { "name": "Castorice-0.2.0-macos-arm64.dmg", "browser_download_url": "https://example/arm.dmg", "size": 10 },
            { "name": "Castorice-0.2.0-macos-x64.dmg", "browser_download_url": "https://example/x64.dmg", "size": 10 },
            { "name": "Castorice-0.2.0-linux-x86_64.AppImage", "browser_download_url": "https://example/app.AppImage", "size": 10 },
            { "name": "Castorice-0.2.0-linux-x86_64.tar.gz", "browser_download_url": "https://example/app.tar.gz", "size": 10 }
          ]
        }
        """;

    // ---- versions ------------------------------------------------------------

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData("0.1.0+abc123", "0.1.0")]
    [InlineData("0.2.0-dev+abc123", "0.2.0-dev")]
    public void Reads_release_tags(string tag, string expected) =>
        Assert.Equal(expected, ReleaseVersion.TryParse(tag)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.x")]
    [InlineData("1.2.3.4")]
    [InlineData("1.0.0-")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("1.0.0-beta_1")]
    public void Rejects_what_is_not_a_version(string tag) => Assert.Null(ReleaseVersion.TryParse(tag));

    [Theory]
    [InlineData("0.2.0", "0.1.9")]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.9")]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-7")]
    [InlineData("0.2.0", "0.2.0-dev")]
    public void Orders_versions_the_semantic_way(string newer, string older)
    {
        Assert.True(V(newer) > V(older));
        Assert.True(V(older) < V(newer));
    }

    [Fact]
    public void Treats_equal_versions_as_equal()
    {
        Assert.Equal(V("v0.2.0"), V("0.2.0+build"));
        Assert.Equal(V("v0.2.0-beta.1").GetHashCode(), V("0.2.0-beta.1+build").GetHashCode());
    }

    // ---- checking --------------------------------------------------------------

    [Fact]
    public async Task Offers_a_newer_release_with_the_installer_for_this_platform()
    {
        var handler = new CannedHandler(Release("v0.2.0"));
        using var checker = new UpdateChecker("NotJansel/Castorice", new HttpClient(handler));

        var update = await checker.CheckAsync(V("0.1.0"), UpdatePlatform.WindowsX64);

        Assert.NotNull(update);
        Assert.Equal("0.2.0", update.Version.ToString());
        Assert.Equal("Castorice-0.2.0-windows-x64-setup.exe", update.Asset?.Name);
        Assert.Equal("https://github.com/NotJansel/Castorice/releases/tag/v0.2.0", update.PageUrl);
        Assert.Equal("https://api.github.com/repos/NotJansel/Castorice/releases/latest", handler.LastUri);
        Assert.Contains("Castorice/0.1.0", handler.LastUserAgent);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public async Task Stays_quiet_when_the_app_is_current_or_newer(string current)
    {
        using var checker = new UpdateChecker("NotJansel/Castorice", new HttpClient(new CannedHandler(Release("v0.2.0"))));

        Assert.Null(await checker.CheckAsync(V(current), UpdatePlatform.WindowsX64));
    }

    [Fact]
    public async Task Never_offers_a_pre_release_or_a_draft()
    {
        using var pre = new UpdateChecker("o/r", new HttpClient(new CannedHandler(Release("v0.3.0-beta.1", prerelease: true))));
        using var draft = new UpdateChecker("o/r", new HttpClient(new CannedHandler(Release("v0.3.0", draft: true))));

        Assert.Null(await pre.CheckAsync(V("0.1.0"), UpdatePlatform.LinuxX64));
        Assert.Null(await draft.CheckAsync(V("0.1.0"), UpdatePlatform.LinuxX64));
    }

    [Fact]
    public async Task A_repository_without_releases_is_not_an_error()
    {
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler("""{"message":"Not Found"}""", HttpStatusCode.NotFound)));

        Assert.Null(await checker.CheckAsync(V("0.1.0"), UpdatePlatform.LinuxX64));
    }

    [Fact]
    public async Task Says_when_github_limits_requests()
    {
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler("{}", HttpStatusCode.Forbidden)));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => checker.CheckAsync(V("0.1.0"), UpdatePlatform.LinuxX64));
        Assert.Contains("limiting", ex.Message);
    }

    [Theory]
    [InlineData(UpdatePlatform.WindowsX64, "Castorice-0.2.0-windows-x64-setup.exe")]
    [InlineData(UpdatePlatform.MacArm64, "Castorice-0.2.0-macos-arm64.dmg")]
    [InlineData(UpdatePlatform.MacX64, "Castorice-0.2.0-macos-x64.dmg")]
    [InlineData(UpdatePlatform.LinuxX64, "Castorice-0.2.0-linux-x86_64.AppImage")]
    [InlineData(UpdatePlatform.LinuxArm64, null)]
    [InlineData(UpdatePlatform.Unknown, null)]
    public async Task Picks_the_installer_by_platform(UpdatePlatform platform, string? expected)
    {
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler(Release("v0.2.0"))));

        var update = await checker.CheckAsync(V("0.1.0"), platform);

        Assert.Equal(expected, update?.Asset?.Name);
    }

    // ---- downloading -----------------------------------------------------------

    [Fact]
    public async Task Downloads_a_file_that_matches_its_checksum()
    {
        var content = Encoding.UTF8.GetBytes("installer bytes");
        var asset = new UpdateAsset
        {
            Name = "setup.exe",
            DownloadUrl = "https://example/setup.exe",
            Size = content.Length,
            Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        };
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler(content)));
        var target = Path.Combine(_directory, "setup.exe");
        var reported = new List<double>();

        await checker.DownloadAsync(asset, target, new SynchronousProgress(reported.Add));

        Assert.Equal(content, await File.ReadAllBytesAsync(target));
        Assert.Equal(1.0, reported[^1]);
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task Throws_away_a_download_that_does_not_match_its_checksum()
    {
        var asset = new UpdateAsset
        {
            Name = "setup.exe",
            DownloadUrl = "https://example/setup.exe",
            Digest = "sha256:" + new string('0', 64),
        };
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler(Encoding.UTF8.GetBytes("tampered"))));
        var target = Path.Combine(_directory, "setup.exe");

        var ex = await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(asset, target));

        Assert.Contains("checksum", ex.Message);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task Accepts_a_download_without_a_checksum()
    {
        var asset = new UpdateAsset { Name = "old.exe", DownloadUrl = "https://example/old.exe" };
        using var checker = new UpdateChecker("o/r", new HttpClient(new CannedHandler(Encoding.UTF8.GetBytes("bytes"))));
        var target = Path.Combine(_directory, "old.exe");

        await checker.DownloadAsync(asset, target);

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Gives_up_a_download_that_stops_moving()
    {
        var asset = new UpdateAsset { Name = "setup.exe", DownloadUrl = "https://example/setup.exe" };
        using var checker = new UpdateChecker("o/r", new HttpClient(new StallingHandler()))
        {
            StallTimeout = TimeSpan.FromMilliseconds(200),
        };
        var target = Path.Combine(_directory, "setup.exe");

        var ex = await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(asset, target));

        Assert.Contains("stopped", ex.Message);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public void Leaves_a_client_it_was_given_as_it_is()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var checker = new UpdateChecker("o/r", http);

        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Answers at once, then sends a few bytes and nothing more.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
    }

    private sealed class StallingStream : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                buffer.Span[0] = 42;
                return 1;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly HttpStatusCode _status;

        public CannedHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
            : this(Encoding.UTF8.GetBytes(body), status)
        {
        }

        public CannedHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public string? LastUri { get; private set; }

        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri?.ToString();
            LastUserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) });
        }
    }
}
