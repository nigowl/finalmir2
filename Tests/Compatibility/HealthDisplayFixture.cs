using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class HealthDisplayFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, string root)
    {
        if (!root.Contains("/mir2-health.", StringComparison.Ordinal)) throw new InvalidOperationException("血条验证仅允许隔离版本。");
        map.Respawns.Clear();
        var role = world.CharacterList.Single(c => c.AccountInfo.AccountID == "NativeSkills");
        role.Magics.Clear();
        var stats = role.Equipment[0].Info.Stats;
        stats[Stat.MinDC] = stats[Stat.MaxDC] = 70; stats[Stat.CriticalRate] = 0; stats[Stat.Luck] = 100;
        var target = new MonsterInfo { Index = ++world.MonsterIndex, Name = "普通血量靶", Image = Monster.Hen, AI = 13, Level = 1, ViewRange = 0,
            Stats = new Stats { [Stat.HP] = 1023 }, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue };
        var victim = new MonsterInfo { Index = ++world.MonsterIndex, Name = "护卫秒杀靶", Image = Monster.Oma, AI = 0, Level = 1, ViewRange = 0,
            Stats = new Stats { [Stat.HP] = 80 }, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue };
        var guard = new MonsterInfo { Index = ++world.MonsterIndex, Name = "秒杀验证护卫", Image = Monster.Guard, AI = 6, Level = 1, ViewRange = 1,
            Stats = new Stats { [Stat.HP] = 500, [Stat.MinDC] = 10, [Stat.MaxDC] = 10, [Stat.Accuracy] = 100 }, AttackSpeed = 500 };
        world.MonsterInfoList.AddRange([target, victim, guard]);
        map.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = target.Index, Location = new(start.X + 1, start.Y), Count = 1, Delay = 600 });
        map.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = guard.Index, Location = new(start.X - 2, start.Y - 2), Count = 1, Delay = 600 });
        foreach (var monster in new[] { target, victim, guard }) File.WriteAllText(Path.Combine("Envir", "Drops", monster.Name + ".txt"), "; 隔离血量验证无掉落。\n");
        File.WriteAllText(Path.Combine("Envir", "NPCs", "NativeShop.txt"), $"[@MAIN]\n#SAY\n<护卫秒杀验证/@guard>\n[@guard]\n#ACT\nPARAM1 {map.FileName}\nPARAM2 {start.X - 2}\nPARAM3 {start.Y - 1}\nMONGEN {victim.Name} 1\nBREAK\n");
        world.SaveAccounts();
    }
}
