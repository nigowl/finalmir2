using System.Collections.Concurrent;
using System.Net.Sockets;
using C = ClientPackets;

namespace Crystal.Native;

// Uses the original client framing and Shared packet serializers, without a browser gateway.
public sealed class GameConnection : IDisposable
{
    private readonly object lifecycle = new();
    private TcpClient? client;
    private CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim writer = new(1);
    public readonly ConcurrentQueue<Packet> Received = new();
    public readonly ConcurrentQueue<string> Errors = new();
    public bool Connected { get { lock (lifecycle) return client?.Connected == true && !cancellation.IsCancellationRequested; } }

    public async Task Connect(string host, int port)
    {
        TcpClient socket; CancellationTokenSource source; CancellationToken token;
        lock (lifecycle)
        {
            cancellation.Cancel(); client?.Dispose(); cancellation.Dispose();
            cancellation = source = new CancellationTokenSource(); token = source.Token;
            client = socket = new TcpClient { NoDelay = true };
            Received.Clear(); Errors.Clear(); Packet.IsServer = false;
        }
        try
        {
            await socket.ConnectAsync(host, port, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            lock (lifecycle)
            {
                if (cancellation != source || source.IsCancellationRequested) throw new OperationCanceledException();
                _ = Receive(socket, source, token); _ = KeepAlive(socket, source, token);
            }
        }
        catch
        {
            lock (lifecycle) { if (cancellation == source) { source.Cancel(); client = null; } socket.Dispose(); }
            throw;
        }
    }

    public Task Send(Packet packet)
    {
        lock (lifecycle) return client == null || cancellation.IsCancellationRequested ? Task.CompletedTask : Send(packet, client, cancellation, cancellation.Token);
    }
    private async Task Send(Packet packet, TcpClient socket, CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            var bytes = packet.GetPacketBytes().ToArray();
            if (bytes.Length is < 4 or > ushort.MaxValue) throw new InvalidDataException("游戏数据包超出协议长度，请重新连接。");
            await writer.WaitAsync(token);
            try { await socket.GetStream().WriteAsync(bytes, token); }
            finally { writer.Release(); }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or SocketException or OperationCanceledException or ObjectDisposedException)
        { Fail(error, socket, source); }
    }

    private async Task Receive(TcpClient socket, CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            var stream = socket.GetStream(); var buffer = new byte[65536]; var count = 0;
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), token);
                if (read == 0) throw new IOException("服务端已断开连接，请重新登录。");
                count += read; var consumed = 0;
                while (count - consumed >= 4)
                {
                    var length = BitConverter.ToUInt16(buffer, consumed);
                    if (length < 4) throw new InvalidDataException("游戏数据包长度无效。");
                    if (count - consumed < length) break;
                    var packet = Packet.ReceivePacket(buffer.AsSpan(consumed, length).ToArray(), out _);
                    lock (lifecycle)
                    {
                        if (cancellation != source || token.IsCancellationRequested) return;
                        if (packet != null) Received.Enqueue(packet);
                    }
                    consumed += length;
                }
                if (Received.Count > 32768) throw new IOException("游戏数据积压过多，请重新连接。");
                buffer.AsSpan(consumed, count - consumed).CopyTo(buffer); count -= consumed;
            }
        }
        catch (TimeoutException) { Fail(new IOException("服务端 30 秒未响应，请检查网络后重新连接。"), socket, source); }
        catch (Exception error) when (error is IOException or InvalidDataException or SocketException or OperationCanceledException or ObjectDisposedException)
        { Fail(error, socket, source); }
    }

    private void Fail(Exception error, TcpClient socket, CancellationTokenSource source)
    {
        lock (lifecycle)
        {
            // An old receive/write task must never disconnect a replacement connection.
            if (cancellation != source || source.IsCancellationRequested) return;
            Errors.Enqueue(error.Message); source.Cancel(); socket.Dispose(); client = null;
        }
    }
    private async Task KeepAlive(TcpClient socket, CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
            while (await timer.WaitForNextTickAsync(token)) await Send(new C.KeepAlive { Time = Environment.TickCount64 }, socket, source, token);
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        lock (lifecycle) { cancellation.Cancel(); client?.Dispose(); client = null; }
    }
}
