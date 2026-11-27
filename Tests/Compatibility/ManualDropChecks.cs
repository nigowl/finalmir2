using System.Reflection;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class ManualDropChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo()) { Width = 5, Height = 5, Cells = new Cell[5, 5] };
        for (var y = 0; y < 5; y++) for (var x = 0; x < 5; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        Player Owner(int id) => new() { Info = new CharacterInfo { Index = id, HP = 100 }, Account = new AccountInfo(),
            Stats = new Stats { [Stat.BagWeight] = 100 }, CurrentMap = map, CurrentLocation = new(2, 2) };
        var owner = Owner(77); var other = Owner(78);
        var pet = new IntelligentCreatureObject(new MonsterInfo()) { Master = owner, CurrentMap = map, CurrentLocation = new(2, 2),
            CreatureRules = new IntelligentCreatureRules { AutoPickupRange = 3 }, CreatureInfo = new UserIntelligentCreature(IntelligentCreatureType.None, 0) };
        foreach (var type in new[] { ItemType.Weapon, ItemType.Potion, ItemType.Scroll, ItemType.CraftingMaterial })
        {
            var item = new UserItem(new ItemInfo { Index = 1, Name = "主动丢弃检查", Type = type, Weight = 1 }) { UniqueID = 1 };
            var ground = new ItemObject(owner, item) { ManualDropperId = owner.Info.Index };
            map.GetCell(ground.CurrentLocation).Add(ground);
            check(ground.ManuallyDroppedBy(owner) && !ground.ManuallyDroppedBy(other), "主动丢弃标记未按角色隔离");
            var legacy = Owner(77); ground.SendManualDrop(legacy); check(legacy.Packets.Count == 0, "未协商的旧客户端收到主动丢弃包");
            owner.EnableClientPresentation(); owner.Packets.Clear(); ground.Add(owner);
            check(owner.Packets.OfType<S.ManualItemDropped>().Single().ObjectID == ground.ObjectID, "毒符/药卷/材料重新进视野未重发排除对象");
            pet.PickUpItem(ground.CurrentLocation);
            check(owner.Info.Inventory.All(value => value == null) && item.Count == 1 && map.GetCell(ground.CurrentLocation).Objects.Contains(ground), "拾取宠物绕过主动丢弃保护");
            var fresh = new ItemObject(owner, item.Clone()) { CurrentLocation = new(3, 2) }; map.GetCell(fresh.CurrentLocation).Add(fresh);
            typeof(IntelligentCreatureObject).GetMethod("FindItemTarget", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
            check(ReferenceEquals(pet.Target, fresh), "拾取宠物误排除了同名普通新掉落");
            map.GetCell(ground.CurrentLocation).Remove(ground); map.GetCell(fresh.CurrentLocation).Remove(fresh);
        }
        var gold = new ItemObject(owner, 100u) { ManualDropperId = owner.Info.Index }; map.GetCell(gold.CurrentLocation).Add(gold);
        pet.PickUpItem(gold.CurrentLocation); check(owner.Account.Gold == 0, "拾取宠物捡回主动丢弃金币");
        var reconnected = Owner(77); reconnected.EnableClientPresentation();
        check(reconnected.Packets.OfType<S.ManualItemDropped>().Any(packet => packet.ObjectID == gold.ObjectID), "重登后未从仍存在的地面对象恢复丢弃保护");
    }
}
