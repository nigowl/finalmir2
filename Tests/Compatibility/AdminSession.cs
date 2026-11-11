using System.Diagnostics;
using System.Text.Json;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class AdminSession
{
    public static async Task Run(string[] args)
    {
        var offset = Array.IndexOf(args, "--admin-session") + 1;
        var port = int.Parse(args[offset]); var account = args[offset + 1]; var password = args[offset + 2]; var characterId = int.Parse(args[offset + 3]); var output = args[offset + 4];
        using var connection = new GameConnection(); var state = new GameState();
        var nextPassword = args.FirstOrDefault(a => a.StartsWith("--new-password="))?[15..];
        var expected = args.FirstOrDefault(a => a.StartsWith("--expect-login="))?[15..];
        await connection.Connect("127.0.0.1", port);
        await Wait<S.Connected>(); await connection.Send(new C.ClientVersion { VersionHash = [] });
        if ((await Wait<S.ClientVersion>()).Result != 1) throw new Exception("Version handshake failed.");
        await connection.Send(new C.Login { AccountID = account, Password = password });
        if (args.Contains("--expect-banned")) { await Wait<S.LoginBanned>(); Write(new { banned = true }); return; }
        if (expected != null) { var response = await Wait<S.Login>(); if (response.Result != int.Parse(expected)) throw new Exception("Unexpected login rejection: " + response.Result); Write(new { result = response.Result }); return; }
        if (nextPassword != null)
        {
            if ((await Wait<S.Login>()).Result != 5) throw new Exception("Forced password change was not requested.");
            await connection.Send(new C.ChangePassword { AccountID = account, CurrentPassword = password, NewPassword = nextPassword });
            if ((await Wait<S.ChangePassword>()).Result != 6) throw new Exception("Original password-change protocol failed.");
            await connection.Send(new C.Login { AccountID = account, Password = nextPassword });
        }
        await Wait<S.LoginSuccess>();
        if (characterId > 0) { await connection.Send(new C.StartGame { CharacterIndex = characterId }); await Wait<S.UserInformation>(); }
        var timer = Stopwatch.StartNew();
        do
        {
            while (connection.Received.TryDequeue(out var packet)) state.Apply(packet);
            Write(new { ready = connection.Connected, entered = state.User != null, mapId = state.Map?.MapIndex, x = state.User?.Location.X, y = state.User?.Location.Y, level = state.User?.Level,
                actors = state.Actors.Values.Select(a => new { id = a.Id, kind = a.Kind.ToString(), name = a.Name, x = a.Location.X, y = a.Location.Y, image = a.Image }).ToArray() });
            if (!connection.Connected) return;
            await Task.Delay(100);
        } while (timer.Elapsed < TimeSpan.FromMinutes(2));
        throw new TimeoutException("Administrative session was not closed by the test.");

        void Write(object value) { File.WriteAllText(output + ".tmp", JsonSerializer.Serialize(value)); File.Move(output + ".tmp", output, true); }
        async Task<T> Wait<T>() where T : Packet
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                while (connection.Received.TryDequeue(out var packet))
                {
                    state.Apply(packet);
                    if (packet is T result) return result;
                    if (packet is S.Login rejected) throw new Exception("Login rejected: " + rejected.Result);
                    if (packet is S.LoginBanned banned) throw new Exception("Login banned: " + banned.Reason);
                }
                if (!connection.Connected) throw new IOException("Session disconnected before " + typeof(T).Name);
                await Task.Delay(20);
            }
            throw new TimeoutException("No " + typeof(T).Name);
        }
    }
}
