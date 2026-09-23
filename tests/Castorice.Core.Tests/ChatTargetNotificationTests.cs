using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Castorice.Core.Chat;
using Castorice.Core.Irc;

namespace Castorice.Core.Tests;

/// <summary>
/// The unread badge and the user count bind straight to <see cref="ChatTarget"/>. Before it raised
/// change notifications both showed whatever they were when first drawn and never moved.
/// </summary>
public class ChatTargetNotificationTests
{
    private static ChatMessage Msg(bool highlight = false) =>
        new() { Sender = "someone", Text = "hi", IsHighlight = highlight };

    private static List<string> Record(ChatTarget target)
    {
        var changed = new List<string>();
        target.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        return changed;
    }

    [Fact]
    public void A_message_in_a_background_conversation_updates_the_badge()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        var changed = Record(target);

        target.Append(Msg(), isActive: false);

        Assert.Equal(1, target.UnreadCount);
        Assert.Contains(nameof(ChatTarget.UnreadCount), changed);
    }

    [Fact]
    public void Opening_the_conversation_clears_the_badge()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        target.Append(Msg(), isActive: false);
        var changed = Record(target);

        target.MarkRead();

        Assert.Equal(0, target.UnreadCount);
        Assert.Contains(nameof(ChatTarget.UnreadCount), changed);
    }

    [Fact]
    public void A_message_in_the_open_conversation_leaves_the_badge_alone()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        var changed = Record(target);

        target.Append(Msg(), isActive: true);

        Assert.Equal(0, target.UnreadCount);
        Assert.DoesNotContain(nameof(ChatTarget.UnreadCount), changed);
    }

    [Fact]
    public void A_mention_flags_the_conversation()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        var changed = Record(target);

        target.Append(Msg(highlight: true), isActive: false);

        Assert.True(target.HasUnreadHighlight);
        Assert.Contains(nameof(ChatTarget.HasUnreadHighlight), changed);
    }

    [Fact]
    public void The_user_count_follows_joins_and_parts()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        var changed = Record(target);

        target.AddUser("A");
        target.AddUser("B");
        target.RemoveUser("A");

        Assert.Equal(1, target.UserCount);
        Assert.Equal(3, changed.Count(n => n == nameof(ChatTarget.UserCount)));
    }

    [Fact]
    public void Nothing_is_announced_when_nothing_changed()
    {
        var target = new ChatTarget("#osu", ChatTargetKind.Channel);
        target.AddUser("A");
        var changed = Record(target);

        target.AddUser("a");          // same nick, different case
        target.RemoveUser("nobody");
        target.MarkRead();            // already read

        Assert.Empty(changed);
    }

    [Fact]
    public async Task Rejoining_a_channel_replaces_its_member_list_instead_of_adding_to_it()
    {
        // Across a reconnect the server sends the full NAMES list again. Adding it to the old list
        // would keep anyone who left in the meantime in the count for good.
        await using var server = await LineServer.StartAsync();
        await using var client = new IrcClient(new OutboundRateLimiter(100, TimeSpan.FromSeconds(1)));
        var chat = new ChatService(client);

        await client.ConnectAsync(new IrcCredentials
        {
            Username = "Referee", Password = "pw", Host = "127.0.0.1", Port = server.Port,
        });

        await server.SendAsync(
            ":Referee!cho@ppy.sh JOIN :#osu",
            ":cho.ppy.sh 353 Referee = #osu :Referee Alice Bob Carol",
            ":cho.ppy.sh 366 Referee #osu :End of /NAMES list.");
        await WaitUntilAsync(() => chat.Find("#osu")?.UserCount == 4);

        await server.SendAsync(":Bob!cho@ppy.sh PART :#osu");
        await WaitUntilAsync(() => chat.Find("#osu")?.UserCount == 3);

        // Rejoin: Carol left while we were away.
        await server.SendAsync(
            ":Referee!cho@ppy.sh JOIN :#osu",
            ":cho.ppy.sh 353 Referee = #osu :Referee Alice",
            ":cho.ppy.sh 366 Referee #osu :End of /NAMES list.");
        await WaitUntilAsync(() => chat.Find("#osu")?.UserCount == 2);

        Assert.Equal(["Alice", "Referee"], chat.Find("#osu")!.Users);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the chat to catch up");
            await Task.Delay(20);
        }
    }

    /// <summary>Registers one client, then lets the test push arbitrary server lines to it.</summary>
    private sealed class LineServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<NetworkStream> _registered = new();
        private TcpClient? _client;

        private LineServer(TcpListener listener) => _listener = listener;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<LineServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new LineServer(listener);
            _ = server.ServeAsync();
            return Task.FromResult(server);
        }

        public async Task SendAsync(params string[] lines)
        {
            var stream = await _registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stream.WriteAsync(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\r\n"))));
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
                    if (line.StartsWith("USER ", StringComparison.Ordinal))
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(":cho.ppy.sh 001 Referee :Welcome\r\n"));
                        _registered.TrySetResult(stream);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
            }
        }
    }
}
