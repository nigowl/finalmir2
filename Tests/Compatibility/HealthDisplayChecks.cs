using System.Text.Json;
using Crystal.Native;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class HealthDisplayChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }
    private sealed class Monster : MonsterObject
    {
        public Monster() : base(new MonsterInfo { Level = 1 }) { }
        public override void Die() => Dead = true;
        public void Regenerate() => ProcessRegen();
    }

    public static void Run(Action<bool, string> check)
    {
        var old = JsonSerializer.Deserialize<ClientPreferences>("{\"Volume\":40,\"DefaultRun\":true}")!;
        check(old.SoundEnabled && old.Volume == 40 && old.DefaultRun && !old.ShowHealthBars, "旧设置改变音量、走跑习惯或擅自打开血条");
        old.SoundEnabled = false; old.ShowHealthBars = true;
        var saved = JsonSerializer.Deserialize<ClientPreferences>(JsonSerializer.Serialize(old))!;
        check(!saved.SoundEnabled && saved.Volume == 40 && saved.ShowHealthBars && saved.DefaultRun, "静音清空原音量或新选项未保存");
        var wasServer = Packet.IsServer;
        try
        {
            Packet.IsServer = true;
            check(Packet.ReceivePacket(new ClientPackets.HealthDisplayRequest().GetPacketBytes().ToArray(), out var extra) is ClientPackets.HealthDisplayRequest && extra.Length == 0, "血条请求原分帧错误");
            Packet.IsServer = false;
            var info = (S.ObjectVitals)Packet.ReceivePacket(new S.ObjectVitals { ObjectID = 7, HP = 123, MaxHP = 1023 }.GetPacketBytes().ToArray(), out extra);
            check(info.ObjectID == 7 && info.HP == 123 && info.MaxHP == 1023 && extra.Length == 0, "精确血量布局错误");
            check(new S.ObjectHealth().GetPacketBytes().Count() == 10 && (int)ServerPacketIds.ObjectVitals > (int)ServerPacketIds.RecycleItemResult &&
                (int)ClientPacketIds.HealthDisplayRequest > (int)ClientPacketIds.RecycleItem, "改变旧血量包布局或插入既有包编号");
        }
        finally { Packet.IsServer = wasServer; }
        var state = new GameState(); state.Apply(new S.ObjectVitals { ObjectID = 7, HP = 123, MaxHP = 1023 });
        check(state.Actors.Count == 0, "未知血量创建幽灵对象");
        state.Apply(new S.ObjectMonster { ObjectID = 7 }); state.Apply(new S.ObjectVitals { ObjectID = 7, HP = 123, MaxHP = 1023 });
        check(state.Actors[7].CurrentHealth == 123 && state.Actors[7].MaximumHealth == 1023 && state.Actors[7].Health == 12, "血量按百分比猜测或未归约");
        foreach (var packet in new[] { new S.ObjectVitals { ObjectID = 7, HP = -1, MaxHP = 1023 }, new S.ObjectVitals { ObjectID = 7, HP = 1024, MaxHP = 1023 }, new S.ObjectVitals { ObjectID = 7, HP = 0, MaxHP = 0 } }) state.Apply(packet);
        check(state.Actors[7].CurrentHealth == 123, "非法血量污染状态");
        state.Apply(new S.ObjectPlayer { ObjectID = 8 }); state.Apply(new S.ObjectVitals { ObjectID = 8, HP = 9, MaxHP = 10 }); state.Apply(new S.ObjectPlayer { ObjectID = 8 });
        check(state.Actors[8].CurrentHealth == 9 && state.Actors[8].MaximumHealth == 10, "人物全量外观刷新丢失血量");
        state.Apply(new S.ObjectRemove { ObjectID = 7 }); state.Apply(new S.ObjectMonster { ObjectID = 7 });
        check(state.Actors[7].CurrentHealth == null, "重新出现复用旧血量");
        state.Apply(new S.LogOutSuccess()); check(state.Actors.Count == 0, "退出保留血量对象");

        var map = new Map(new MapInfo()) { Width = 3, Height = 3, Cells = new Cell[3, 3] };
        for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        var player = new Player { Info = new CharacterInfo { HP = 123 }, Stats = new Stats { [Stat.HP] = 1023 }, CurrentMap = map, CurrentLocation = new(1, 1) };
        player.Node = new LinkedListNode<MapObject>(player); map.Cells[1, 1].Add(player); map.Players.Add(player);
        var monster = new Monster { CurrentMap = map, CurrentLocation = new(2, 1), HP = 80,
            Stats = new Stats { [Stat.HP] = 1023 }, DamageRate = 1, ArmourRate = 1 };
        monster.Node = new LinkedListNode<MapObject>(monster); map.Cells[2, 1].Add(monster);
        monster.SendExactHealth(player); check(player.Packets.Count == 0, "未请求的旧客户端收到新增包");
        player.EnableHealthDisplay();
        check(player.Packets.OfType<S.ObjectVitals>().Count() == 2 && player.Packets.OfType<S.ObjectVitals>().Any(p => p.ObjectID == monster.ObjectID && p.HP == 80 && p.MaxHP == 1023), "启用未取得视野内本人及怪物真实血量");
        player.Packets.Clear(); player.EnableHealthDisplay(); check(player.Packets.Count == 0, "重复请求反复扫描并重发全量");
        monster.Hidden = true; player.Packets.Clear(); monster.SendExactHealth(player); check(player.Packets.Count == 0, "泄露隐藏对象血量");
        monster.Hidden = false; player.Packets.Clear(); monster.CurrentLocation = new(999, 999); monster.SendExactHealth(player); check(player.Packets.Count == 0, "泄露视野外血量"); monster.CurrentLocation = new(2, 1);
        monster.ChangeHP(-17); check(player.Packets.OfType<S.ObjectVitals>().Last().HP == 63, "原扣血未同步精确增量");
        var guard = MonsterObject.GetMonster(new MonsterInfo { AI = 6 }); guard.Stats = new Stats { [Stat.Accuracy] = 100 };
        player.Packets.Clear(); monster.Attacked(guard, int.MaxValue, DefenceType.None);
        check(monster.HP == 0 && monster.Dead && player.Packets.OfType<S.DamageIndicator>().Single(p => p.Type == DamageType.Hit).Damage == -63,
            "护卫秒杀显示结算上限而非怪物剩余63HP");
        player.Packets.Clear(); monster.Dead = false; monster.HP = 1000; monster.HealAmount = 10000; monster.HealTime = monster.RegenTime = -1; monster.Regenerate();
        check(monster.HP == 1023 && player.Packets.OfType<S.DamageIndicator>().Single(p => p.Type == DamageType.Hit).Damage == 23, "治疗溢出显示为计划值而非实际23HP");
        player.Info.Mount = new MountInfo(player); player.DamageRate = player.ArmourRate = 1; player.Stats[Stat.MP] = 1000; player.MP = 1000;
        player.SpecialMode = SpecialItemMode.Protection;
        player.Packets.Clear(); player.Struck(80, DefenceType.None);
        check(player.HP == 123 && player.MP == 920 && !player.Packets.OfType<S.DamageIndicator>().Any(p => p.Type == DamageType.Hit), "护身转蓝仍显示虚假扣血");
        player.SpecialMode = SpecialItemMode.None;
        player.Packets.Clear(); player.Struck(80, DefenceType.None);
        check(player.HP == 43 && player.Packets.OfType<S.DamageIndicator>().Single(p => p.Type == DamageType.Hit).Damage == -80, "人物真实80HP场景伤害未显示");
    }
}
