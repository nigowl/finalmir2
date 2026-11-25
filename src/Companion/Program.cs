// 陪玩执行组件，GPL-3.0-only；通过原 TCP 游玩，进程不读取数据库。
using System.Text.Json;
using Mir2.Companions;
using Mir2.Companion;

var timing = Environment.GetEnvironmentVariable("MIR2_COMPANION_PLAYER_TIMING") is { } configured
    ? JsonSerializer.Deserialize<PlayerActionTiming>(configured) ?? throw new InvalidDataException("缺少陪玩动作速度。")
    : PlayerActionTiming.Default;
if (!PlayerActionTiming.Valid(timing.MoveMilliseconds, timing.ActionSpeedPercent))
    throw new InvalidDataException("陪玩动作速度超出范围。");

var sessions = new Dictionary<Guid, CompanionSession>();
var output = new SemaphoreSlim(1);
async Task Report(WorkerEvent value)
{
    var line = JsonSerializer.Serialize(value);
    if (line.Length > CompanionProtocol.MaxMessage) throw new InvalidDataException("陪玩事件过长。");
    await output.WaitAsync();
    try { await Console.Out.WriteLineAsync(line); await Console.Out.FlushAsync(); }
    finally { output.Release(); }
}
await Report(new("hello", Guid.Empty, Protocol: CompanionProtocol.Fingerprint));
try
{
    while (await Console.In.ReadLineAsync() is { } line)
    {
        if (line.Length > CompanionProtocol.MaxMessage) throw new InvalidDataException("陪玩控制消息过长。");
        var command = JsonSerializer.Deserialize<WorkerCommand>(line) ?? throw new InvalidDataException("陪玩控制消息为空。");
        if (command.Kind == "quit") break;
        if (command.Kind == "start" && command.Start is { } start)
        {
            foreach(var id in sessions.Where(p=>p.Value.Finished).Select(p=>p.Key).ToArray())
            {await sessions[id].Stop();sessions.Remove(id);}
            if (sessions.Remove(start.Profile.Id, out var prior)) await prior.Stop();
            if (start.Protocol != CompanionProtocol.Fingerprint || start.Profile.Id == Guid.Empty || sessions.Count >= 100)
                throw new InvalidDataException("陪玩协议、编号或会话上限不匹配。");
            var session = new CompanionSession(start, Report, timing); sessions.Add(start.Profile.Id, session); session.Begin();
        }
        else if (sessions.TryGetValue(command.Id, out var active))
        {
            if (command.Kind == "stop") { await active.Stop(); sessions.Remove(command.Id); }
            else active.Control(command);
        }
    }
}
catch (Exception e) when (e is IOException or JsonException or InvalidDataException)
{ await Console.Error.WriteLineAsync("陪玩控制通道停止：" + e.Message); }
finally { foreach (var session in sessions.Values) await session.Stop(); }
