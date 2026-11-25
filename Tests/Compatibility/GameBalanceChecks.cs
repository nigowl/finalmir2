using System.Drawing;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class GameBalanceChecks
{
    private sealed class Target(MonsterInfo info) : MonsterObject(info)
    {
        public int LastDamage;
        public override void ChangeHP(int value) { LastDamage = -value; HP = Math.Max(1, HP + value); }
        public void PoisonTick() => ProcessPoison();
        public bool GoldDrop(uint gold) => DropGold(gold);
    }
    private sealed class Player : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }

    public static void Run(Action<bool, string> check, string root)
    {
        var paths = new ProjectPaths(data: Path.Combine(root, "balance"));
        Directory.CreateDirectory(Path.Combine(paths.World, "Configs"));
        File.WriteAllText(Path.Combine(paths.World, "Configs", "Setup.ini"), "[Balance]\n; 独立测试\n");
        var service = new SystemSettings(new(paths)); var original = service.Read();
        var originalBuffs = Envir.Main.BuffInfoList; Envir.Main.BuffInfoList = BuffInfo.Load();
        try
        {
            check(GameBalance.Scale(27, 1) == 27 && GameBalance.Scale(0, 100) == 0 && GameBalance.Scale(-1, 100) == -1, "默认倍率改变原值/零值/标记值");
            check(GameBalance.Scale(int.MaxValue, 100) == int.MaxValue && GameBalance.Duration(long.MaxValue, 10) == long.MaxValue, "倍率/时长极值溢出");
            var settings = original.Values with { PlayerPhysicalDamageRate = 2, PlayerMagicDamageRate = 3, PlayerPvpDamageRate = .5f,
                MonsterDamageRate = 4, PetDamageRate = 5, MonsterHpRate = 2, MonsterDefenceRate = 3,
                MonsterAttackSpeedRate = 2, MonsterMoveSpeedRate = .5f, PlayerAttackSpeedBonus = 5,
                PoisonDamageRate = 3, PoisonDurationRate = 2, ControlDurationRate = .5f, SkillBuffDurationRate = 2, SkillCooldownRate = .5f };
            check(service.Save(new(settings, original.Version)).RequiresRestart, "技能冷却变更未标记重启同步");
            var map = new Map(new MapInfo { Index = 998, Title = "隔离平衡检查" }) { Width = 20, Height = 20, Cells = new Cell[20, 20] };
            for (var x = 0; x < 20; x++) for (var y = 0; y < 20; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
            var player = new Player { Info = new CharacterInfo { Index = 998, Name = "平衡检查", Level = 10, HP = 100, MP = 100, Equipment = new UserItem[14] },
                Account = new AccountInfo(), CurrentMap = map, CurrentLocation = new Point(10, 10), ArmourRate = 1, DamageRate = 1 };
            player.Info.Mount = new MountInfo(player); player.Node = new LinkedListNode<MapObject>(player); map.Cells[10, 10].Add(player);
            var source = new MonsterInfo { Level = 1, MoveSpeed = 1000, AttackSpeed = 1200,
                Stats = new Stats { [Stat.HP] = 10000, [Stat.MinAC] = 10, [Stat.MaxAC] = 10, [Stat.MinMAC] = 20, [Stat.MaxMAC] = 20 } };
            var target = new Target(source) { CurrentMap = map, CurrentLocation = new Point(11, 10), HP = 10000, ArmourRate = 1, DamageRate = 1 };
            target.Node = new LinkedListNode<MapObject>(target); map.Cells[11, 10].Add(target);
            target.RefreshAll();
            check(target.Stats[Stat.HP] == 20000 && target.Stats[Stat.MaxAC] == 30 && target.Stats[Stat.MaxMAC] == 60 && source.Stats[Stat.HP] == 10000, "怪物倍率修改原定义或没有用于实例属性");
            check(target.MoveSpeed == 2000 && target.AttackSpeed == 600, "怪物速度倍率未用于真实间隔");
            target.RefreshAll(); check(target.Stats[Stat.HP] == 20000 && target.HP == 10000, "刷新重复放大属性或擅自重置现存生命");
            target.PetLevel = 5; target.RefreshAll();
            check(target.Stats[Stat.HP] == 20200 && target.Stats[Stat.MaxAC] == 60, "怪物倍率遗漏战宠成长属性");
            target.PetLevel = 0;
            target.Stats.Clear(); target.Stats[Stat.HP] = 20000;
            player.Stats = new Stats { [Stat.Accuracy] = 100 };
            check(target.Attacked(player, 100, DefenceType.AC, false) == 200 && target.LastDamage == 200, "玩家物理伤害倍率未实际扣血");
            check(target.Attacked(player, 100, DefenceType.MAC, false) == 300 && target.LastDamage == 300, "玩家法术伤害倍率未实际扣血");
            player.Stats[Stat.AttackBonus] = 7;
            check(target.Attacked(player, 100, DefenceType.AC, false) == 214, "固定伤害加成被遗漏在玩家倍率之外");
            player.Stats[Stat.AttackBonus] = 0;
            var monster = new MonsterObject(new MonsterInfo()) { CurrentMap = map, CurrentLocation = new Point(12, 10), Stats = new Stats { [Stat.Accuracy] = 100 } };
            check(target.Attacked(monster, 100, DefenceType.AC) == 400, "野怪倍率未实际结算");
            monster.Master = player;
            check(target.Attacked(monster, 100, DefenceType.AC) == 500, "战宠倍率与野怪倍率混用");
            check(GameBalance.HumanDamage(player, 100, DefenceType.AC) == 100 && GameBalance.HumanDamage(player, 100, DefenceType.MAC) == 150, "PvP额外倍率未与原伤害类型组合");
            player.RefreshStats();
            var client = GameBalance.ClientStats(Settings.ClassBaseStats[0]);
            check(player.Stats[Stat.AttackSpeed] == 5 && client.Stats.Single(stat => stat.Type == Stat.AttackSpeed).Calculate(MirClass.Warrior, player.Level) == 5 &&
                Settings.ClassBaseStats[0].Stats.All(stat => stat.Type != Stat.AttackSpeed || stat.Base != 5), "攻速修改未同步原基础属性包或污染职业定义");
            check(player.AttackSpeed == (96000 + Settings.PlayerActionSpeedPercent - 1) / Settings.PlayerActionSpeedPercent, "玩家真实攻速不符基础修正及动作速度倍率");
            var magic = new UserMagic(Spell.FireBall) { Level = 2, Info = new MagicInfo { DelayBase = 3000, DelayReduction = 500 } };
            check(magic.GetDelay() == 1000 && magic.CreateClientMagic().Delay == 1000, "冷却未同时应用原施法判断和客户端元数据");
            var poison = new Poison { Owner = player, PType = PoisonType.Green, Value = 10, Duration = 5, TickSpeed = 1000, TickTime = -1 };
            target.ApplyPoison(poison); var applied = target.PoisonList.Single();
            check(applied.Value == 30 && applied.Duration == 10 && poison.Value == 10 && poison.Duration == 5, "毒倍率未生效或污染共享原毒对象");
            target.PoisonTick(); check(target.LastDamage == 30, "毒倍率未实际按周期扣血");
            target.PoisonList.Clear(); target.ApplyPoison(poison);
            check(target.PoisonList.Single().Value == 30 && target.PoisonList.Single().Duration == 10, "重用毒对象重复放大倍率");
            foreach (var type in new[] { PoisonType.Red, PoisonType.Bleeding, PoisonType.Paralysis, PoisonType.Frozen, PoisonType.Slow, PoisonType.Blindness, PoisonType.DelayedExplosion })
            {
                var effect = GameBalance.Poison(new Poison { PType = type, Value = 10, Duration = 10 });
                check(effect.Duration == (type is PoisonType.Red or PoisonType.Bleeding ? 20 : type == PoisonType.DelayedExplosion ? 10 : 5), $"{type}时长归类错误");
            }
            var buff = player.AddBuff(BuffType.MagicShield, player, 10000, new Stats(), false);
            check(buff.ExpireTime == 20000, "技能Buff没有放大初始时长");
            player.AddBuff(BuffType.MagicShield, player, 19000, null, false);
            check(buff.ExpireTime == 19000, "盾受击更新剩余时长时重复放大");
            check(GameBalance.BuffDuration(BuffType.Exp, 100, new()) == 100 && GameBalance.BuffDuration(BuffType.MagicShield, -1, new()) == -1, "倍率污染物品Buff或永久状态");
            player.Stats[Stat.CriticalRate] = 100; player.Stats[Stat.CriticalDamage] = 100;
            check(target.Attacked(player, int.MaxValue, DefenceType.AC, false) == int.MaxValue && target.LastDamage == int.MaxValue, "高倍率暴击伤害溢出为负数");
            Settings.DropGold = true; Settings.MaxDropGold = 10; Settings.DropRange = 1; Settings.DropStackSize = 1;
            check(target.GoldDrop(20), "金币按上限分堆失败");
            ItemObject[] GoldPiles() => map.Cells.Cast<Cell>().SelectMany(cell => cell.Objects ?? []).OfType<ItemObject>().ToArray();
            check(GoldPiles().Length == 2 && GoldPiles().Sum(item => item.Gold) == 20 && GoldPiles().All(item => item.Gold == 10), "金币整倍数生成空堆或改变总额");
            check(!target.GoldDrop(uint.MaxValue) && GoldPiles().Length <= 9, "低金币分堆上限在落点满后仍无界循环");
            foreach (var item in GoldPiles()) { map.RemoveObject(item); item.Despawn(); }
            foreach (var role in Enum.GetValues<MirClass>()) check(Globals.IsClassicClass(role) == ((int)role <= 2), "三职业范围错误");
        }
        finally { Envir.Main.BuffInfoList = originalBuffs; service.Save(new(original.Values, service.Read().Version)); }
    }
}
