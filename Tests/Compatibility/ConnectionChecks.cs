using System.Net;
using System.Net.Sockets;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class ConnectionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var connection = new GameConnection();
        async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition()) await Task.Delay(10, timeout.Token);
        }
        async Task<TcpClient> Open()
        {
            var accepted = listener.AcceptTcpClientAsync(); await connection.Connect("127.0.0.1", port);
            return await accepted;
        }
        using (var peer = await Open())
        {
            var stream = peer.GetStream(); var bytes = new S.KeepAlive { Time = 123 }.GetPacketBytes().ToArray();
            await stream.WriteAsync(bytes.AsMemory(0, 3)); await Task.Delay(30);
            check(connection.Connected && connection.Received.IsEmpty, "Partial TCP header was decoded prematurely");
            await stream.WriteAsync(bytes.AsMemory(3, 4)); await Task.Delay(30);
            check(connection.Received.IsEmpty, "Partial TCP payload was decoded prematurely");
            await stream.WriteAsync(bytes.AsMemory(7));
            await Until(() => !connection.Received.IsEmpty);
            check(connection.Received.TryDequeue(out var packet) && packet is S.KeepAlive { Time: 123 }, "Fragmented packet did not reassemble");
            await stream.WriteAsync(bytes.Concat(new S.Connected().GetPacketBytes()).ToArray());
            await Until(() => connection.Received.Count == 2);
            check(connection.Received.TryDequeue(out var first) && first is S.KeepAlive && connection.Received.TryDequeue(out var second) && second is S.Connected, "Coalesced TCP packets lost order");
            var sends = Enumerable.Range(0, 100).Select(i => connection.Send(new C.Chat { Message = "frame-" + i })).ToArray();
            var names = new HashSet<string>();
            for (var i = 0; i < sends.Length; i++)
            {
                var header = new byte[4]; await stream.ReadExactlyAsync(header);
                var body = new byte[BitConverter.ToUInt16(header) - 4]; await stream.ReadExactlyAsync(body);
                using var reader = new BinaryReader(new MemoryStream(body)); names.Add(reader.ReadString());
            }
            await Task.WhenAll(sends);
            check(names.Count == 100 && Enumerable.Range(0,100).All(i => names.Contains("frame-"+i)), "Concurrent writes interleaved game frames");
        }
        await Until(() => !connection.Connected);
        check(connection.Errors.Count == 1, "Remote EOF did not fail the connection exactly once");
        using (var peer = await Open())
        {
            await Task.Delay(50);
            check(connection.Connected && connection.Errors.IsEmpty && connection.Received.IsEmpty, "Previous receive task poisoned a replacement connection");
            await peer.GetStream().WriteAsync(new S.KeepAlive { Time = 456 }.GetPacketBytes().ToArray());
            await Until(() => !connection.Received.IsEmpty);
            check(connection.Received.TryDequeue(out var packet) && packet is S.KeepAlive { Time: 456 }, "Replacement connection did not receive its own packets");
            // Header claims a KeepAlive without the required Int64 body.
            var malformed = new S.KeepAlive().GetPacketBytes().Take(4).ToArray(); malformed[0] = 4; malformed[1] = 0;
            await peer.GetStream().WriteAsync(malformed); await Until(() => !connection.Connected);
            check(connection.Errors.Count == 1, "Malformed packet decoding left a dead receive task marked connected");
        }
        using (var peer = await Open())
        {
            await connection.Send(new C.Chat { Message = new string('x', 70000) });
            check(!connection.Connected && connection.Errors.Count == 1 && !peer.GetStream().DataAvailable, "Outgoing frame overflow wrapped its UInt16 length on the wire");
        }
        using (var peer = await Open())
        {
            connection.Dispose(); await Task.Delay(30);
            check(connection.Errors.IsEmpty && !connection.Connected, "Planned disposal reported a network failure");
        }
        using (var previous = await Open())
        {
            await previous.GetStream().WriteAsync(new byte[] { 12, 0, 0 });
            using var replacement = await Open();
            await replacement.GetStream().WriteAsync(new S.KeepAlive { Time = 789 }.GetPacketBytes().ToArray());
            await Until(() => !connection.Received.IsEmpty); await Task.Delay(30);
            check(connection.Connected && connection.Errors.IsEmpty, "Canceling an in-flight partial receive disconnected its replacement");
            check(connection.Received.TryDequeue(out var packet) && packet is S.KeepAlive { Time: 789 } && connection.Received.IsEmpty, "Partial old-session bytes crossed into the new session");
        }
    }
}
