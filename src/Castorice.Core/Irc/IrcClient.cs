using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Castorice.Core.Irc.Events;

namespace Castorice.Core.Irc;

/// <summary>
/// A minimal IRC client aimed at Bancho (<c>irc.ppy.sh</c>): connect, register, stay alive,
/// and surface every line it reads. It owns no chat state — <see cref="Chat.ChatService"/> does.
/// </summary>
public sealed class IrcClient : IAsyncDisposable
{
    private static readonly Encoding Wire = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly OutboundRateLimiter _limiter;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private TcpClient? _tcp;
    private Stream? _stream;
    private StreamReader? _reader;
    private CancellationTokenSource? _lifetime;
    private Task? _readLoop;
    private IrcConnectionState _state = IrcConnectionState.Disconnected;

    public IrcClient(OutboundRateLimiter? limiter = null)
    {
        _limiter = limiter ?? OutboundRateLimiter.ForPlayerAccount();
    }

    /// <summary>Every parsed line, including ones the client handles itself.</summary>
    public event EventHandler<IrcMessage>? MessageReceived;

    /// <summary>Raw wire traffic for the debug console. <c>true</c> means the line was sent by us.</summary>
    public event EventHandler<RawLine>? RawTraffic;

    public event EventHandler<IrcConnectionStateChanged>? StateChanged;

    public IrcConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(this, new IrcConnectionStateChanged(value));
        }
    }

    public bool IsConnected => State is IrcConnectionState.Connected;

    public string CurrentNick { get; private set; } = string.Empty;

    /// <summary>
    /// Opens the socket, registers with <c>PASS</c>/<c>NICK</c>/<c>USER</c> and starts the read loop.
    /// Returns once the server has accepted the registration (numeric 001).
    /// </summary>
    public async Task ConnectAsync(IrcCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (State is not IrcConnectionState.Disconnected)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        State = IrcConnectionState.Connecting;
        CurrentNick = credentials.Nick;

        var registered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(credentials.Host, credentials.Port, cancellationToken).ConfigureAwait(false);

            Stream stream = _tcp.GetStream();
            if (credentials.UseTls)
            {
                var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                await tls.AuthenticateAsClientAsync(credentials.Host).ConfigureAwait(false);
                stream = tls;
            }

            _stream = stream;
            _reader = new StreamReader(stream, Wire);

            _readLoop = Task.Run(() => ReadLoopAsync(registered, _lifetime.Token), CancellationToken.None);

            State = IrcConnectionState.Registering;

            // Registration bypasses the rate limiter: Bancho expects these three immediately.
            await WriteLineAsync($"PASS {credentials.Password}", redact: true, cancellationToken).ConfigureAwait(false);
            await WriteLineAsync($"NICK {credentials.Nick}", redact: false, cancellationToken).ConfigureAwait(false);
            await WriteLineAsync($"USER {credentials.Nick} 0 * :{credentials.Username}", redact: false, cancellationToken)
                .ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var registration = timeout.Token.Register(
                static s => ((TaskCompletionSource<string?>)s!).TrySetCanceled(),
                registered);

            var failure = await registered.Task.ConfigureAwait(false);
            if (failure is not null)
            {
                throw new IrcAuthenticationException(failure);
            }

            State = IrcConnectionState.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw new IrcAuthenticationException("The server did not complete registration within 30 seconds.");
        }
        catch
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends a <c>PRIVMSG</c>, subject to the outbound rate limit.</summary>
    public async Task SendMessageAsync(string target, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var line in SplitForWire(text))
        {
            await _limiter.WaitForSlotAsync(cancellationToken).ConfigureAwait(false);
            await WriteLineAsync($"PRIVMSG {target} :{line}", redact: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends an already-formed command line such as <c>JOIN #osu</c>, subject to the rate limit.</summary>
    public async Task SendRawAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        await _limiter.WaitForSlotAsync(cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(line, redact: false, cancellationToken).ConfigureAwait(false);
    }

    public Task JoinAsync(string channel, CancellationToken cancellationToken = default) =>
        SendRawAsync($"JOIN {Normalise(channel)}", cancellationToken);

    public Task PartAsync(string channel, CancellationToken cancellationToken = default) =>
        SendRawAsync($"PART {Normalise(channel)}", cancellationToken);

    public async Task DisconnectAsync(string? quitReason = null)
    {
        if (_stream is not null && State is IrcConnectionState.Connected && quitReason is not null)
        {
            try
            {
                await WriteLineAsync($"QUIT :{quitReason}", redact: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The socket is going away regardless.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _reader?.Dispose();
        _stream?.Dispose();
        _tcp?.Dispose();
        _lifetime?.Dispose();

        _reader = null;
        _stream = null;
        _tcp = null;
        _lifetime = null;
        _readLoop = null;

        State = IrcConnectionState.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private async Task ReadLoopAsync(TaskCompletionSource<string?> registered, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _reader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                RawTraffic?.Invoke(this, new RawLine(line, Outbound: false));

                var message = IrcMessage.Parse(line);
                if (message is null)
                {
                    continue;
                }

                switch (message.Command)
                {
                    case "PING":
                        await WriteLineAsync($"PONG :{message.Trailing}", redact: false, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case IrcNumerics.Welcome:
                        registered.TrySetResult(null);
                        break;

                    // Bancho answers a bad IRC password with "Bad authentication token."
                    case IrcNumerics.PasswordMismatch:
                    case IrcNumerics.NoNicknameGiven:
                    case IrcNumerics.ErroneousNickname:
                    case IrcNumerics.NicknameInUse:
                        registered.TrySetResult(
                            message.Trailing.Length > 0 ? message.Trailing : $"Server refused login ({message.Command}).");
                        break;
                }

                MessageReceived?.Invoke(this, message);
            }

            registered.TrySetResult("The connection closed before registration completed.");
        }
        catch (OperationCanceledException)
        {
            registered.TrySetResult("The connection was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            registered.TrySetResult(ex.Message);
            StateChanged?.Invoke(this, new IrcConnectionStateChanged(IrcConnectionState.Disconnected, ex.Message));
        }
    }

    private async Task WriteLineAsync(string line, bool redact, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("The client is not connected.");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = Wire.GetBytes(line + "\r\n");
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        RawTraffic?.Invoke(this, new RawLine(redact ? "PASS ***" : line, Outbound: true));
    }

    /// <summary>
    /// IRC lines cap out around 512 bytes including the envelope. Long chat messages are wrapped
    /// rather than truncated by the server.
    /// </summary>
    internal static IEnumerable<string> SplitForWire(string text, int maxLength = 400)
    {
        var normalised = text.Replace("\r", string.Empty);

        foreach (var paragraph in normalised.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var remaining = paragraph.Trim();
            while (remaining.Length > maxLength)
            {
                var cut = remaining.LastIndexOf(' ', maxLength - 1);
                if (cut <= 0)
                {
                    cut = maxLength;
                }

                yield return remaining[..cut].TrimEnd();
                remaining = remaining[cut..].TrimStart();
            }

            if (remaining.Length > 0)
            {
                yield return remaining;
            }
        }
    }

    private static string Normalise(string channel) =>
        channel.StartsWith('#') ? channel : '#' + channel;
}

public sealed class IrcAuthenticationException(string message) : Exception(message);
