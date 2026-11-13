using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class SkillDamageChecks
{
    private sealed class Target(MonsterInfo info) : MonsterObject(info)
    {
        public void TickPoison() => ProcessPoison();
    }
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void Broadcast(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = 999, Title = "隔离技能检查" }) { Width = 20, Height = 20, Cells = new Cell[20, 20] };
        for (var x = 0; x < 20; x++) for (var y = 0; y < 20; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        var player = new Player { Info = new CharacterInfo { Index = 999, Name = "技能检查", Level = 50, HP = 1000, MP = 1000, Equipment = new UserItem[14] },
            Account = new AccountInfo(), Stats = new Stats { [Stat.MinDC] = 100, [Stat.MaxDC] = 100, [Stat.MinMC] = 100, [Stat.MaxMC] = 100,
                [Stat.MinSC] = 100, [Stat.MaxSC] = 100, [Stat.HP] = 1000, [Stat.MP] = 1000, [Stat.Accuracy] = 100, [Stat.Luck] = 100 } };
        player.Info.Mount = new MountInfo(player); player.CurrentMap = map; player.CurrentLocation = new(10, 10);
        player.Node = new LinkedListNode<MapObject>(player); map.Cells[10, 10].Add(player);
        var target = new Target(new MonsterInfo { Level = 1, AI = 0 }) { HP = 10000,
            Stats = new Stats { [Stat.HP] = 10000 }, ArmourRate = 1, DamageRate = 1, CurrentMap = map, CurrentLocation = new(11, 10) };
        target.Node = new LinkedListNode<MapObject>(target); map.Cells[11, 10].Add(target);
        UserMagic Magic(Spell spell) => new(spell) { Level = 3, CastTime = -100000,
            Info = new MagicInfo { Spell = spell, BaseCost = 5, LevelCost = 2, PowerBase = 10, PowerBonus = 0, MultiplierBase = 2, MultiplierBonus = 0, DelayBase = 1800 } };
        void Reset(Spell spell)
        {
            target.HP = 10000; player.MP = 1000; player.HP = 800; player.ActionTime = player.AttackTime = player.SpellTime = 0;
            player.FlamingSword = player.TwinDrakeBlade = player.Slaying = false; player.FlamingSwordTime = 0;
            player.Info.Magics = [Magic(spell)]; player.ActionList.Clear(); player.Packets.Clear(); target.PoisonList.Clear();
        }
        void Complete()
        {
            foreach (var action in player.ActionList.ToArray()) { player.ActionList.Remove(action); player.Process(action); }
        }
        Reset(Spell.None); player.Info.Magics.Clear(); player.Attack(MirDirection.Right, Spell.None); Complete();
        check(target.HP == 9900 && player.MP == 1000, "普通攻击没有实际扣除100HP或错误耗蓝");
        Reset(Spell.FlamingSword); player.SpellToggle(Spell.FlamingSword, SpellToggleState.True);
        check(player.FlamingSword && player.MP == 989 && target.HP == 10000, "烈火准备应仅耗11MP，不应提前扣怪物HP");
        player.SpellToggle(Spell.FlamingSword, SpellToggleState.True);
        check(player.MP == 989, "重复烈火准备错误扣蓝");
        player.Attack(MirDirection.Right, Spell.FlamingSword); Complete();
        check(target.HP == 9780 && player.MP == 989 && !player.FlamingSword, "烈火没有按原倍率实际扣220HP，或攻击时再次耗蓝");
        check(player.Packets.OfType<S.ObjectAttack>().Single().Spell == Spell.FlamingSword, "烈火成功攻击广播丢失技能类型");
        player.SpellToggle(Spell.FlamingSword, SpellToggleState.True);
        check(!player.FlamingSword && player.MP == 989, "烈火绕过原10秒准备冷却");
        Reset(Spell.FlamingSword); player.Attack(MirDirection.Right, Spell.FlamingSword); Complete();
        check(target.HP == 9900 && player.Packets.OfType<S.ObjectAttack>().Single().Spell == Spell.None, "未准备烈火没有降级为普通攻击");
        Reset(Spell.FlamingSword); player.MP = 10; player.SpellToggle(Spell.FlamingSword, SpellToggleState.True);
        check(!player.FlamingSword && player.MP == 10, "蓝量不足仍准备烈火");
        foreach (var spell in new[] { Spell.Slaying, Spell.Thrusting, Spell.HalfMoon, Spell.CrossHalfMoon, Spell.DoubleSlash, Spell.TwinDrakeBlade })
        {
            Reset(spell); player.Slaying = spell == Spell.Slaying; player.TwinDrakeBlade = spell == Spell.TwinDrakeBlade;
            player.Attack(MirDirection.Right, spell); Complete();
            var expected = spell is Spell.Slaying ? 220 : spell is Spell.DoubleSlash or Spell.TwinDrakeBlade ? 440 : 100;
            check(10000 - target.HP == expected, $"{spell} 实际HP扣减不符原近战单段/双段规则：{10000 - target.HP}/{expected}");
        }
        foreach (var spell in new[] { Spell.FireBall, Spell.GreatFireBall, Spell.ThunderBolt, Spell.SoulFireBall, Spell.FrostCrunch, Spell.FlameDisruptor, Spell.StraightShot, Spell.DoubleShot })
        {
            Reset(spell);
            player.Info.Equipment[(int)EquipmentSlot.Amulet] = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 0, StackSize = 100 }) { Count = 100 };
            player.Magic(spell, MirDirection.Right, target.ObjectID, target.CurrentLocation); Complete();
            var expected = spell == Spell.FlameDisruptor ? 330 : spell == Spell.DoubleShot ? 440 : 220;
            check(10000 - target.HP == expected && player.MP == 989, $"{spell} 实际伤害/耗蓝错误：HP={10000 - target.HP} MP={player.MP}");
            check(player.Packets.OfType<S.Magic>().Any(packet => packet.Cast), $"{spell} 未发出成功施法回执");
            var hp = target.HP; var mp = player.MP; player.SpellTime = player.ActionTime = 0;
            player.Magic(spell, MirDirection.Right, target.ObjectID, target.CurrentLocation); Complete();
            check(target.HP == hp && player.MP == mp, $"{spell} 冷却中仍重复扣血/耗蓝");
        }
        foreach (short shape in new short[] { 2, 1 })
        {
            Reset(Spell.Poisoning);
            var poison = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = shape, StackSize = 100 }) { Count = 10 };
            player.Info.Equipment[(int)EquipmentSlot.Amulet] = poison;
            player.Magic(Spell.Poisoning, MirDirection.Right, target.ObjectID, target.CurrentLocation); Complete();
            var effect = target.PoisonList.SingleOrDefault(p => p.PType == (shape == 2 ? PoisonType.Red : PoisonType.Green));
            check(effect != null && poison.Count == 9 && player.MP == 989, $"原{shape}毒没有中毒效果/单份耗材/真实扣蓝");
            effect!.TickTime = -1; var hp = target.HP; target.TickPoison();
            check(shape == 2 ? target.ArmourRate < 1 && target.HP == hp : target.HP == hp - effect.Value && effect.Value > 0,
                $"原{shape}毒没有实际降防或持续扣HP");
        }
        Reset(Spell.FireWall); player.InSafeZone = true;
        var wall = new SpellObject { Spell = Spell.FireWall, Caster = player, CurrentMap = map, Value = 100 };
        wall.ProcessSpell(target);
        check(target.HP == 10000, "原火墙绕过安全区保护");
        player.InSafeZone = false; wall.ProcessSpell(target); wall.ProcessSpell(target);
        check(target.HP == 9800, "走出安全区后火墙未持续扣HP");
    }
}
