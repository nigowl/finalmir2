using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class DamageIndicatorChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = 999 }) { Width = 3, Height = 3, Cells = new Cell[3, 3] };
        for (var x = 0; x < 3; x++) for (var y = 0; y < 3; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        var player = new Player { Info = new CharacterInfo { Index = 999, Name = "飘字检查", Equipment = new UserItem[14] },
            Stats = new Stats { [Stat.Accuracy] = 100 }, CurrentMap = map, CurrentLocation = new(1, 1), Account = new AccountInfo() };
        player.Node = new LinkedListNode<MapObject>(player); map.Cells[1, 1].Add(player); map.Players.Add(player);
        var pet = MonsterObject.GetMonster(new MonsterInfo { AI = 0 });
        pet.Stats = new Stats { [Stat.Accuracy] = 100 }; pet.Master = player;
        foreach (var ai in new byte[] { 3, 79, 89, 128, 56 })
        {
            var target = MonsterObject.GetMonster(new MonsterInfo { AI = ai, Level = 1 });
            target.CurrentMap = map; target.CurrentLocation = new(2, 1); target.HP = 1000;
            target.Stats = new Stats { [Stat.HP] = 1000 }; target.ArmourRate = target.DamageRate = 1;
            var expected = ai is 79 or 56 ? 37 : 1;
            foreach (var usePet in new[] { false, true })
            {
                player.Packets.Clear(); var before = target.HP;
                var result = usePet ? target.Attacked(pet, 37) : target.Attacked(player, 37, damageWeapon: false);
                var hits = player.Packets.OfType<S.DamageIndicator>().ToArray();
                check(result > 0 && hits.Length == 1 && hits[0].ObjectID == target.ObjectID && hits[0].Damage == -expected && hits[0].Type == DamageType.Hit,
                    $"AI{ai} {(usePet ? "宠物" : "人物")}受击未广播唯一真实伤害");
                check(target.HP == before - (ai == 56 ? 0 : expected), $"AI{ai} 飘字修复改变了原扣血/训练规则");
            }
            if (ai == 3)
            {
                player.Packets.Clear(); var before = target.HP;
                check(target.Attacked(player, 37, DefenceType.MAC, false) == 0 && target.Attacked(pet, 37, DefenceType.MAC) == 0 &&
                    target.HP == before && !player.Packets.OfType<S.DamageIndicator>().Any(), "树类法术免疫被改成有效伤害");
            }
        }
        var ordinary = MonsterObject.GetMonster(new MonsterInfo());
        ordinary.CurrentMap = map; ordinary.CurrentLocation = new(2, 1); ordinary.HP = 1000;
        ordinary.Stats = new Stats { [Stat.HP] = 1000 }; ordinary.ArmourRate = ordinary.DamageRate = 1;
        player.Packets.Clear();
        check(ordinary.Struck(11, DefenceType.MAC) == 11 && ordinary.HP == 989 && player.Packets.OfType<S.DamageIndicator>().Single().Damage == -11,
            "无攻击者的场景伤害未发原回执或重复飘字");
    }
}
