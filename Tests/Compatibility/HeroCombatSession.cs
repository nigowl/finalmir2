using System.Diagnostics;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using C = ClientPackets;
using S = ServerPackets;

internal static class HeroCombatSession
{
    public static async Task Run(string[] args)
    {
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄战斗实测仅允许隔离版本。");
        var index = Array.IndexOf(args, "--hero-combat-session"); var port = int.Parse(args[index + 1]); var record = args[index + 2];
        var restored = args.Contains("--verify-save");
        var snapshots = restored ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(record))! : new Dictionary<string, string>();
        Packet.IsServer = false;
        foreach (var job in Enum.GetValues<MirClass>())
        {
            using var connection = new GameConnection(); var state = new GameState(); var packets = new List<Packet>();
            async Task Until(Func<bool> ready, int milliseconds = 45000)
            {
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); packets.Add(packet); }
                    if (ready()) return;
                    if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                    if (timer.ElapsedMilliseconds > milliseconds) throw new TimeoutException(job + " 英雄自动组合超时：" + string.Join(',', packets.OfType<S.ObjectMagic>().Where(p => p.ObjectID == state.Hero?.ObjectID).Select(p => p.Spell).Distinct()));
                    await Task.Delay(20);
                }
            }
            string Snapshot() => JsonSerializer.Serialize(new { state.Hero!.Name, state.Hero.Class,
                Equipment = state.Hero.Equipment.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.ItemIndex}:{item.Count}"),
                Skills = state.Hero.Magics.Select(magic => new { magic.Spell, magic.Level, magic.Experience, magic.Key }) });
            await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
            await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
            await connection.Send(new C.Login { AccountID = HeroCombatFixture.Account(job), Password = "Native123" }); await Until(() => packets.OfType<S.LoginSuccess>().Any());
            await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single().Index });
            await Until(() => state.User != null && (restored ? state.HeroState == HeroSpawnState.Unsummoned : state.Hero != null));
            if (restored)
            {
                if (state.CurrentPetMode != PetMode.MoveOnly) throw new Exception(job + "英雄跟随模式未保存。");
                // 休息中的英雄登录时不发送完整资料，按正常召唤读取且保持跟随模式。
                await connection.Send(new C.Chat { Message = "@SUMMONHERO" }); await Until(() => state.Hero != null && state.HeroState == HeroSpawnState.Summoned);
                if (Snapshot() != snapshots[job.ToString()]) throw new Exception(job + "英雄装备/耗材/熟练度/未绑键未完整保存。");
                await Task.Delay(1600); await connection.Send(new C.Chat { Message = "@SUMMONHERO" }); await Until(() => state.HeroState == HeroSpawnState.Unsummoned);
            }
            else
            {
                var hero = state.Hero!; var initialMana = hero.MP; var initialHealth = state.User!.HP;
                bool Seen(Spell spell) => packets.OfType<S.ObjectMagic>().Any(p => p.ObjectID == hero.ObjectID && p.Spell == spell && p.Cast);
                bool Hit(Spell spell) => packets.OfType<S.ObjectAttack>().Any(p => p.ObjectID == hero.ObjectID && p.Spell == spell);
                await Until(() => job switch
                {
                    MirClass.Warrior => Seen(Spell.Rage) && (Hit(Spell.FlamingSword) || Hit(Spell.TwinDrakeBlade)),
                    MirClass.Wizard => Seen(Spell.MagicShield) && Seen(Spell.Mirroring) && (Seen(Spell.IceStorm) || Seen(Spell.ThunderBolt)),
                    MirClass.Taoist => Seen(Spell.SummonShinsu) && Seen(Spell.SoulFireBall) && state.User!.HP > initialHealth,
                    MirClass.Assassin => Seen(Spell.Haste) && (Seen(Spell.CrescentSlash) || Hit(Spell.DoubleSlash) || Seen(Spell.HeavenlySword)),
                    _ => Seen(Spell.ElementalBarrier) && (Seen(Spell.NapalmShot) || Seen(Spell.DoubleShot))
                });
                await Until(() => hero.MP < initialMana && packets.OfType<S.DamageIndicator>().Any(p => p.Type == DamageType.Hit && p.Damage < 0));
                if (hero.Magics.Any(magic => magic.Key != 0)) throw new Exception(job + "自动组合修改了手动快捷键。");
                await connection.Send(new C.ChangePMode { Mode = PetMode.MoveOnly }); await Until(() => state.CurrentPetMode == PetMode.MoveOnly);
                // 原战斗退出保护结束后再收回，不能在持续攻击时等待一次被拒绝的请求。
                await Task.Delay((int)Globals.LogDelay + 2200);
                await connection.Send(new C.Chat { Message = "@SUMMONHERO" }); await Until(() => state.HeroState == HeroSpawnState.Unsummoned);
                await Task.Delay(1600); await connection.Send(new C.Chat { Message = "@SUMMONHERO" }); await Until(() => state.HeroState == HeroSpawnState.Summoned);
                await Task.Delay(1600); await connection.Send(new C.Chat { Message = "@SUMMONHERO" }); await Until(() => state.HeroState == HeroSpawnState.Unsummoned);
                snapshots[job.ToString()] = Snapshot();
                Console.WriteLine($"PASS {job} automatic original-TCP hero combo, real MP/damage, unbound keys; no client Magic packet sent.");
            }
            packets.RemoveAll(p => p is S.LogOutSuccess); await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
            await Task.Delay(5200);
        }
        if (!restored) File.WriteAllText(record, JsonSerializer.Serialize(snapshots));
        Console.WriteLine(restored ? "PASS all five hero skill/material snapshots after full restart." : "PASS all five automatic hero class combinations.");
    }
}
