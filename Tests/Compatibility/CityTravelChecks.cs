using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class CityTravelChecks
{
    private sealed class Traveller : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public readonly List<Point> Landings = [];
        public bool Permit = true;
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override bool Teleport(Map map, Point point, bool effects = true, byte effectnumber = 0)
        { if (Permit) Landings.Add(point); return Permit; }
    }

    public static void Run(Action<bool, string> check)
    {
        Map MakeMap(int id)
        {
            var map = new Map(new MapInfo { Index = id }) { Width = 12, Height = 12, Cells = new Cell[12, 12] };
            for (var x = 0; x < 12; x++) for (var y = 0; y < 12; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
            return map;
        }
        var source = MakeMap(88001); var target = MakeMap(88002); var city = new Point(5, 5); var field = new Point(9, 9);
        target.Info.SafeZones.Add(new SafeZoneInfo { Location = city, Size = 1 });
        var scroll = new UserItem(new ItemInfo { Index = 88003, Name = "回城卷轴", Type = ItemType.Scroll, Shape = 1, StackSize = 20 }) { UniqueID = 88003, Count = 5 };
        var account = new AccountInfo { Gold = 10000 };
        var player = new Traveller { Info = new CharacterInfo { Index = 88004, AccountInfo = account, Inventory = [scroll, null] }, Account = account,
            CurrentMap = source, CurrentLocation = new(2, 3), Stats = new Stats { [Stat.BagWeight] = 100 } };
        var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
        typeof(MapObject).GetField(nameof(MapObject.ObjectID), BindingFlags.Public | BindingFlags.Instance)!.SetValue(npc, 88005U);
        npc.Info = new NPCInfo { Location = new(2, 2), FileName = "CityTravel/回归使者" }; source.NPCs.Add(npc);
        void Ready() { player.NPCObjectID = npc.ObjectID; player.NPCPage = new NPCPage("[@MAIN]"); player.NPCSpeech.Clear(); player.Packets.Clear(); player.Landings.Clear(); }
        void Send(bool safe = true, uint gold = 0) => CityTravel.TrySend(player, target, safe ? city : field, safe, gold);
        void Rejected(string reason, bool safe = true, uint gold = 0)
        {
            Ready(); var money = account.Gold; Send(safe, gold);
            check(player.Landings.Count == 0 && account.Gold == money && player.Info.Inventory[0] == scroll && scroll.Count == 5 &&
                player.Packets.Count == 0 && player.NPCSpeech.Count > 0, reason);
        }
        Ready(); Send();
        check(source.GetSafeZone(player.CurrentLocation) == null && player.Landings.SequenceEqual([city]) && account.Gold == 10000 &&
            player.NPCObjectID == 0 && player.NPCPage == null && scroll.Count == 5, "安全区外无法传送，或免费传送改变资产/会话未清理");
        source.Info.SafeZones.Add(new SafeZoneInfo { Location = player.CurrentLocation, Size = 1 });
        Ready(); Send(); check(player.Landings.SequenceEqual([city]), "安全区内传送回归失败");
        source.Info.Movements.Add(new MovementInfo { Source = player.CurrentLocation });
        Ready(); Send(); check(player.Landings.SequenceEqual([city]), "出发格仍被套用落点/出口规则");
        target.GetCell(city).Objects.Add(new Traveller());
        Ready(); Send(); check(player.Landings.SequenceEqual([city]), "已有玩家占用时仍拒绝传送或改变落点");
        target.GetCell(field).Objects.Add(new Traveller());
        Ready(); Send(false, 2000);
        check(player.Landings.SequenceEqual([field]) && account.Gold == 8000 && player.Packets.OfType<S.LoseGold>().Single().Gold == 2000 && scroll.Count == 5,
            "危险区同格传送未成功或实扣/物品错误");
        var count = player.Landings.Count; Send(false, 2000);
        check(player.Landings.Count == count && account.Gold == 8000, "重复确认再次传送或扣款");
        player.Dead = true; Rejected("死亡仍传送"); player.Dead = false;
        player.TradePartner = new Traveller(); Rejected("交易时仍传送"); player.TradePartner = null;
        player.ItemRentalPartner = new Traveller(); Rejected("租赁时仍传送"); player.ItemRentalPartner = null;
        player.CurrentLocation = new(11, 11); Rejected("超出NPC距离仍传送"); player.CurrentLocation = new(2, 3);
        npc.Info.FileName = "Other/回归使者"; Rejected("非使者脚本获得传送"); npc.Info.FileName = "CityTravel/回归使者";
        source.Info.NoTeleport = true; Rejected("出发地图禁传送被放宽"); source.Info.NoTeleport = false;
        source.Info.NoTownTeleport = true; Rejected("出发地图禁回城被放宽"); source.Info.NoTownTeleport = false;
        target.Info.NoTeleport = true; Rejected("目标地图禁传送被放宽"); target.Info.NoTeleport = false;
        target.Info.RequiredGroup = true; Rejected("目标组队限制被放宽"); target.Info.RequiredGroup = false;
        target.GetCell(city).Attribute = CellAttribute.HighWall; Rejected("墙格仍作为落点"); target.GetCell(city).Attribute = CellAttribute.Walk;
        target.Info.SafeZones.Clear(); Rejected("城镇落点越过原安全落点要求"); target.Info.SafeZones.Add(new SafeZoneInfo { Location = city, Size = 1 });
        Rejected("免费目的地接受收费", true, 1); Rejected("危险区接受零价格", false, 0);
        Rejected("危险区接受超额费用", false, 50001);
        account.Gold = 1999; Rejected("金币不足仍传送或改变资产", false, 2000); account.Gold = 8000;
        player.Permit = false; Rejected("原Teleport失败仍扣费", false, 2000); player.Permit = true;
    }
}
