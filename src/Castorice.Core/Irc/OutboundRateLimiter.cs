namespace Castorice.Core.Irc;

/// <summary>
/// Sliding-window limiter for outbound lines. Bancho silences accounts that exceed its quota,
/// so every write goes through here rather than straight onto the socket.
/// </summary>
public sealed class OutboundRateLimiter(int maxMessages, TimeSpan window, TimeProvider? timeProvider = null)
{
    private readonly Queue<long> _sent = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Quota for a regular osu! account: 10 messages per 5 seconds.</summary>
    public static OutboundRateLimiter ForPlayerAccount(TimeProvider? timeProvider = null) =>
        new(10, TimeSpan.FromSeconds(5), timeProvider);

    /// <summary>Quota for an account flagged as a bot by osu! staff: 300 messages per 60 seconds.</summary>
    public static OutboundRateLimiter ForBotAccount(TimeProvider? timeProvider = null) =>
        new(300, TimeSpan.FromSeconds(60), timeProvider);

    public int MaxMessages { get; } = maxMessages > 0
        ? maxMessages
        : throw new ArgumentOutOfRangeException(nameof(maxMessages));

    public TimeSpan Window { get; } = window > TimeSpan.Zero
        ? window
        : throw new ArgumentOutOfRangeException(nameof(window));

    /// <summary>
    /// Blocks until another line may be sent, then records it against the window.
    /// </summary>
    public async Task WaitForSlotAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            TimeSpan wait;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _time.GetTimestamp();
                Trim(now);

                if (_sent.Count < MaxMessages)
                {
                    _sent.Enqueue(now);
                    return;
                }

                wait = Window - _time.GetElapsedTime(_sent.Peek(), now);
                if (wait < TimeSpan.Zero)
                {
                    wait = TimeSpan.Zero;
                }
            }
            finally
            {
                _gate.Release();
            }

            // Nudge past the boundary so the retry is guaranteed to find the slot free.
            await Task.Delay(wait + TimeSpan.FromMilliseconds(15), _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Trim(long now)
    {
        while (_sent.Count > 0 && _time.GetElapsedTime(_sent.Peek(), now) >= Window)
        {
            _sent.Dequeue();
        }
    }
}
