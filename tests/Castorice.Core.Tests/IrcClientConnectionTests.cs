using System.Net;
using System.Net.Sockets;
using System.Text;
using Castorice.Core.Irc;

namespace Castorice.Core.Tests;

/// <summary>
/// The client against a real socket, to pin down what happens when Bancho goes away. Before this,
/// a dropped connection left the client reporting Connected, and the next send crashed the app.
/// </summary>
public class IrcClientConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static IrcClient NewClient() =>
        new(new OutboundRateLimiter(100, TimeSpan.FromSeconds(1)));

    private static IrcCredentials Credentials(int port) =>
        new() { Username = "Referee", Password = "pw", Host = "127.0.0.1", Port = port };

    [Fact]
    public async Task A_connection_bancho_closes_is_reported_as_disconnected()
    {
        await using var server = await FakeBancho.StartAsync();
        await using var client = NewClient();

        var dropped = new TaskCompletionSource<IrcConnectionStateChanged>();
        client.StateChanged += (_, e) =>
        {
            if (e.State is IrcConnectionState.Disconnected)
            {
                dropped.TrySetResult(e);
            }
        };

        await client.ConnectAsync(Credentials(server.Port));
        Assert.True(client.IsConnected);

        server.DropClient();

        var change = await dropped.Task.WaitAsync(Wait);
        Assert.False(client.IsConnected);
        Assert.Equal(IrcConnectionState.Disconnected, client.State);
        Assert.False(string.IsNullOrEmpty(change.Detail));
    }

    [Fact]
    public async Task Sending_after_the_connection_dropped_throws_one_catchable_type()
    {
        await using var server = await FakeBancho.StartAsync();
        await using var client = NewClient();
        await client.ConnectAsync(Credentials(server.Port));

        server.DropClient();
        await WaitUntilAsync(() => !client.IsConnected);

        await Assert.ThrowsAsync<IrcConnectionLostException>(
            () => client.SendMessageAsync("#osu", "hello"));
    }

    [Fact]
    public async Task Sending_before_ever_connecting_throws_the_same_type()
    {
        await using var client = NewClient();

        await Assert.ThrowsAsync<IrcConnectionLostException>(
            () => client.SendMessageAsync("#osu", "hello"));
    }

    [Fact]
    public async Task The_lost_connection_type_is_still_an_io_exception()
    {
        // Callers that already treat I/O failure as expected must keep catching it.
        await using var client = NewClient();

        await Assert.ThrowsAnyAsync<IOException>(() => client.SendRawAsync("JOIN #osu"));
    }

    [Fact]
    public async Task Reconnecting_after_a_drop_gives_a_working_connection()
    {
        await using var first = await FakeBancho.StartAsync();
        await using var client = NewClient();
        await client.ConnectAsync(Credentials(first.Port));

        first.DropClient();
        await WaitUntilAsync(() => !client.IsConnected);

        await using var second = await FakeBancho.StartAsync();
        await client.ConnectAsync(Credentials(second.Port));

        Assert.True(client.IsConnected);
        await client.SendMessageAsync("#osu", "back again");
        Assert.Contains("PRIVMSG #osu :back again", await second.NextLineAsync(l => l.StartsWith("PRIVMSG")));
    }

    [Fact]
    public async Task A_deliberate_disconnect_ends_quietly()
    {
        await using var server = await FakeBancho.StartAsync();
        await using var client = NewClient();
        await client.ConnectAsync(Credentials(server.Port));

        await client.DisconnectAsync("bye");

        Assert.Equal(IrcConnectionState.Disconnected, client.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the client");
            await Task.Delay(20);
        }
    }

    /// <summary>Just enough of Bancho to register one client, and to hang up on it.</summary>
    private sealed class FakeBancho : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<string> _received = [];
        private TcpClient? _client;

        private FakeBancho(TcpListener listener) => _listener = listener;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<FakeBancho> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new FakeBancho(listener);
            _ = server.ServeAsync();
            return Task.FromResult(server);
        }

        public void DropClient() => _client?.Close();

        public async Task<string> NextLineAsync(Func<string, bool> match)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                lock (_received)
                {
                    var line = _received.FirstOrDefault(match);
                    if (line is not null)
                    {
                        return line;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("the server never received the expected line");
        }

        public ValueTask DisposeAsync()
        {
            _client?.Dispose();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }

        private async Task ServeAsync()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync();
                var stream = _client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (_received)
                    {
                        _received.Add(line);
                    }

                    if (line.StartsWith("USER ", StringComparison.Ordinal))
                    {
                        var welcome = Encoding.UTF8.GetBytes(":cho.ppy.sh 001 Referee :Welcome\r\n");
                        await stream.WriteAsync(welcome);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // The test hung up on purpose.
            }
        }
    }
}
