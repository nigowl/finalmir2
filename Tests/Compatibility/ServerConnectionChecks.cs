using System.Net;
using System.Net.Sockets;
using Server.MirNetwork;
using S = ServerPackets;

internal static class ServerConnectionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var registrations=new MirConnectionLog{AccountsMade=[0,1,3600000]};registrations.PruneAccounts(3600000);
        check(registrations.AccountsMade.SequenceEqual(new long[]{1,3600000}),"注册限频未在一小时边界清理，或错误移除未过期请求");
        registrations.PruneAccounts(7200000);check(registrations.AccountsMade.Count==0,"注册达到上限后永久滞留");
        registrations.CharactersMade=[0,1,3600000];registrations.PruneCharacters(3600000);
        check(registrations.CharactersMade.SequenceEqual(new long[]{1,3600000}),"建角限频没有在检查前清理一小时边界");
        registrations.PruneCharacters(7200000);check(registrations.CharactersMade.Count==0,"建角达到上限后永久滞留");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var peer = new TcpClient(); var accepted = listener.AcceptTcpClientAsync();
        await peer.ConnectAsync(IPAddress.Loopback, port);
        var socket = await accepted; socket.SendBufferSize = 1024;
        var server = new MirConnection(10001, socket);
        try
        {
            var padding = new string('x', 1000);
            for (var i = 0; i < 1000; i++) server.Enqueue(new S.Chat { Message = i + ":" + padding });
            server.Process();
            for (var i = 1000; i < 2000; i++) server.Enqueue(new S.Chat { Message = i + ":" + padding });
            server.Process();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stream = peer.GetStream();
            async Task<Packet?> Read()
            {
                var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                var bytes = new byte[BitConverter.ToUInt16(header)]; header.CopyTo(bytes, 0);
                await stream.ReadExactlyAsync(bytes.AsMemory(4), timeout.Token);
                return Packet.ReceivePacket(bytes, out _);
            }
            check(await Read() is S.Connected, "Original server handshake was overtaken by queued game data");
            var values = new List<string>();
            for (var i = 0; i < 2000; i++)
            {
                if (await Read() is not S.Chat packet) throw new Exception("Game frames interleaved across server sends.");
                values.Add(packet.Message);
            }
            check(values.Select((s,i) => s == i + ":" + padding).All(v => v), "Partial/overlapping server sends dropped or reordered a game packet");
        }
        finally { server.Disconnect(20); }

        using var slow = new TcpClient { ReceiveBufferSize = 1024 };
        var next = listener.AcceptTcpClientAsync(); await slow.ConnectAsync(IPAddress.Loopback, port);
        var blocked = await next; blocked.SendBufferSize = 1024;
        var stalled = new MirConnection(10002, blocked);
        try
        {
            var payload = new string('x', 60000);
            for (var batch = 0; batch < 4 && !stalled.Disconnecting; batch++)
            {
                for (var i = 0; i < 100; i++) stalled.Enqueue(new S.Chat { Message = payload });
                stalled.Process();
            }
            check(stalled.Disconnecting, "Stalled client accumulated unlimited pending outbound bytes");
        }
        finally { stalled.Disconnect(20); }
    }
}
