using System.Collections.Concurrent;
using System.Net.Sockets;
using C = ClientPackets;

namespace Crystal.Native;

// Uses the original client framing and Shared packet serializers, without a browser gateway.
public sealed class GameConnection : IDisposable
{
    private TcpClient? client;
    private CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim writer = new(1);
    public readonly ConcurrentQueue<Packet> Received = new();
    public readonly ConcurrentQueue<string> Errors = new();
    public bool Connected => client?.Connected == true && !cancellation.IsCancellationRequested;

    public async Task Connect(string host, int port)
    {
        Dispose();
        cancellation.Dispose();
        Received.Clear(); Errors.Clear();
        cancellation = new CancellationTokenSource();
        client = new TcpClient { NoDelay = true };
        Packet.IsServer = false;
        await client.ConnectAsync(host, port, cancellation.Token);
        _ = Receive(client.GetStream(), cancellation.Token);
        _ = KeepAlive(cancellation.Token);
    }

    public async Task Send(Packet packet)
    {
        var socket = client;
        if (socket == null || !Connected) return;
        var token = cancellation.Token;
        var bytes = packet.GetPacketBytes().ToArray();
        try
        {
            await writer.WaitAsync(token);
            try { await socket.GetStream().WriteAsync(bytes, token); }
            finally { writer.Release(); }
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            Fail(error, token);
        }
    }

    private async Task Receive(NetworkStream stream, CancellationToken token)
    {
        try
        {
            var buffer = new byte[65536];
            var count = 0;
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), token);
                if (read == 0) throw new IOException("服务端已断开连接，请重新登录。");
                count += read;
                var consumed = 0;
                while (count - consumed >= 4)
                {
                    var length = BitConverter.ToUInt16(buffer, consumed);
                    if (length < 4) throw new InvalidDataException("Invalid game packet length.");
                    if (count - consumed < length) break;
                    var packet = Packet.ReceivePacket(buffer.AsSpan(consumed, length).ToArray(), out _);
                    if (packet != null) Received.Enqueue(packet);
                    consumed += length;
                }
                buffer.AsSpan(consumed, count - consumed).CopyTo(buffer);
                count -= consumed;
            }
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            Fail(error, token);
        }
    }

    private void Fail(Exception error, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        Errors.Enqueue(error.Message);
        if (token == cancellation.Token) { cancellation.Cancel(); client?.Dispose(); }
    }

    private async Task KeepAlive(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
            while (await timer.WaitForNextTickAsync(token)) await Send(new C.KeepAlive { Time = Environment.TickCount64 });
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        client?.Dispose();
        client = null;
    }
}
