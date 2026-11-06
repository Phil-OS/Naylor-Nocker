using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using NaylorNocker.Core.Configuration;
using NaylorNocker.Core.Protocol;

namespace NaylorNocker.Core.Networking;

public sealed class UdpKnockReceiver
{
    private readonly IPEndPoint _localEndpoint;
    private readonly KnockMatcher _matcher;
    private readonly ChannelWriter<IPAddress> _matchedSources;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private long _droppedKnocks;

    public UdpKnockReceiver(ValidatedKnockOptions options, KnockMatcher matcher, ChannelWriter<IPAddress> matchedSources)
    {
        ArgumentNullException.ThrowIfNull(options);
        _localEndpoint = new IPEndPoint(options.BindAddress, options.ListenPort);
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _matchedSources = matchedSources ?? throw new ArgumentNullException(nameof(matchedSources));
    }

    public Task Ready => _ready.Task;
    public long DroppedKnocks => Interlocked.Read(ref _droppedKnocks);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The UDP receiver can only run once.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var socket = new Socket(_localEndpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.ExclusiveAddressUse = true;
            if (_localEndpoint.AddressFamily == AddressFamily.InterNetworkV6) socket.DualMode = false;
            socket.Bind(_localEndpoint);
            _ready.TrySetResult();
            // Return control to startup even if traffic is already queued on the socket.
            await Task.Yield();

            EndPoint remote = new IPEndPoint(_localEndpoint.AddressFamily == AddressFamily.InterNetwork
                ? IPAddress.Any : IPAddress.IPv6Any, 0);
            // A complete legal knock is at most 256 bytes. One extra byte prevents
            // an oversized datagram from becoming a matching truncated prefix.
            var buffer = new byte[257];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, remote,
                        cancellationToken);
                }
                catch (SocketException error) when (error.SocketErrorCode == SocketError.MessageSize)
                {
                    // Windows reports a truncated UDP receive as WSAEMSGSIZE.
                    continue;
                }
                if (received.ReceivedBytes > 256 ||
                    !_matcher.Matches(buffer.AsSpan(0, received.ReceivedBytes), truncated: false)) continue;

                IPAddress source;
                try { source = SourceAddress.Normalize(((IPEndPoint)received.RemoteEndPoint).Address); }
                catch (ArgumentException) { continue; }
                if (!_matchedSources.TryWrite(source)) Interlocked.Increment(ref _droppedKnocks);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ready.TrySetCanceled(cancellationToken);
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
            throw;
        }
    }
}
