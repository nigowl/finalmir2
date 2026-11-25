using System.Drawing;
using System.Reflection;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.MirObjects.Monsters;
using S = ServerPackets;

internal static class PetOwnershipChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }
    private sealed class WolfKing(MonsterInfo info) : SnowWolfKing(info)
    { public void Complete() => CompleteDeath([]); }

    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = -31 }) { Width = 12, Height = 12, Cells = new Cell[12, 12] };
        for (var y = 0; y < 12; y++) for (var x = 0; x < 12; x++) map.Cells[x, y] = new Cell();
        Player Owner(string name)
        {
            var player = new Player { Info = new CharacterInfo { Name = name, Level = 60, Class = MirClass.Wizard },
                CurrentMap = map, CurrentLocation = new(5, 5), AMode = AttackMode.All, Stats = new Stats() };
            player.Node = new LinkedListNode<MapObject>(player); map.AddObject(player); return player;
        }
        var first = Owner("归属甲"); var second = Owner("归属乙"); var oldSave = Settings.PetSave; var oldTime = Envir.Main.Time;
        var info = new MonsterInfo { Name = "归属原怪", Image = Monster.Hen, AI = 13, Level = 1, CanTame = true,
            Stats = new Stats { [Stat.HP] = 100 } };
        var monster = MonsterObject.GetMonster(info); monster.CurrentMap = map; monster.CurrentLocation = new(6, 5);
        monster.Stats.Add(info.Stats); monster.SetHP(100); monster.Node = new LinkedListNode<MapObject>(monster); map.AddObject(monster);
        void Receipt(Player? owner, string reason)
        {
            var packets = first.Packets.Where(packet => packet is S.ObjectName name && name.ObjectID == monster.ObjectID ||
                packet is S.ObjectMonster full && full.ObjectID == monster.ObjectID).ToArray();
            var expected = owner?.ObjectID ?? 0;
            check(packets.Length == 2 && packets[0] is S.ObjectName renamed && renamed.Name == monster.Name &&
                packets[1] is S.ObjectMonster updated && updated.Name == monster.Name && updated.MasterObjectId == expected,
                reason + "没有依次广播原名称与完整权威归属");
            check(second.Packets.OfType<S.ObjectMonster>().Any(packet => packet.ObjectID == monster.ObjectID && packet.MasterObjectId == expected),
                reason + "只更新主人，旁观者仍保留旧归属");
            first.Packets.Clear(); second.Packets.Clear();
        }
        try
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, 1000L);
            Settings.PetSave = false;
            var shock = typeof(HumanObject).GetMethod("ElectricShock", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var magic = new UserMagic(Spell.ElectricShock) { Level = 3 };
            foreach (var owner in new[] { first, second })
            {
                var previous = monster.Master;
                for (var attempt = 0; attempt < 4096 && monster.Master != owner; attempt++) shock.Invoke(owner, [monster, magic]);
                check(monster.Master == owner && owner.Pets.Count(pet => pet == monster) == 1 &&
                    (previous == null || !previous.Pets.Contains(monster)) && monster.TameTime > Envir.Main.Time,
                    "原诱惑未建立/转移唯一会话归属与计时");
                Receipt(owner, "原诱惑/转移");
            }
            monster.TameTime = Envir.Main.Time; monster.Process();
            check(monster.Master == null && !second.Pets.Contains(monster), "原到期未移除主人列表"); Receipt(null, "原到期");
            var king = new WolfKing(info) { CurrentMap = map, CurrentLocation = new Point(7, 5), EXPOwner = first };
            king.SlaveList.Add(monster); king.Complete();
            check(monster.Master == first && first.Pets.Count(pet => pet == monster) == 1 && king.SlaveList.Count == 0,
                "原雪狼死亡转移未建立归属或清理随从"); Receipt(first, "原雪狼转移");
        }
        finally { Settings.PetSave = oldSave; typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime); }
    }
}
