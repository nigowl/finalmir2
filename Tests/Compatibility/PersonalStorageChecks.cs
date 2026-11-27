using System.Runtime.CompilerServices;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class PersonalStorageChecks
{
    private sealed class Player : PlayerObject
    {
        internal readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    internal static void Run(Action<bool, string> check)
    {
        var required = Settings.RequireStoragePassword;
        var wasServer = Packet.IsServer;
        try
        {
            Settings.RequireStoragePassword = true;
            Packet.IsServer = true;
            var account = new AccountInfo { StoragePassword = "Store123" };
            var player = new Player { Account = account, Info = new CharacterInfo { AccountInfo = account },
                CurrentMap = new Map(new MapInfo()), Stats = new Stats { [Stat.BagWeight] = int.MaxValue } };
            player.Report = new Reporting(player);
            player.Connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection));
            check(Packet.ReceivePacket(new ClientPackets.StorageRequest().GetPacketBytes().ToArray(), out _) is ClientPackets.StorageRequest, "直接仓库请求未沿原TCP分帧读取");
            player.RequestStorage();
            check(player.NPCPage == null && player.Packets.OfType<S.NPCStorage>().Count() == 1 && !player.Packets.OfType<S.UserStorage>().Any(), "直接开仓仍需NPC或泄露未解锁仓库");
            var item = new UserItem(new ItemInfo { Index = 1, Name = "存取检查", Type = ItemType.Potion, StackSize = 20 }) { UniqueID = 88001, Count = 12 };
            player.Info.Inventory[6] = item;
            player.StoreItem(6, 0); check(!player.Packets.OfType<S.StoreItem>().Last().Success && player.Info.Inventory[6] == item, "仓库密码未解锁仍能存入");
            check(!player.TryUnlockStorage("Wrong123") && player.TryUnlockStorage("Store123"), "直接仓库密码校验失效");
            player.StoreItem(6, 0); check(player.Packets.OfType<S.StoreItem>().Last().Success && account.Storage[0] == item && player.Info.Inventory[6] == null, "无NPC原存入失败或改变编号");
            player.MoveItem(MirGridType.Storage, 0, 1); check(account.Storage[1] == item && account.Storage[0] == null, "无NPC仓库内移动失败");
            player.TakeBackItem(1, 6); check(player.Packets.OfType<S.TakeBackItem>().Last().Success && player.Info.Inventory[6] == item && account.Storage[1] == null, "无NPC原取回失败");
            player.StoreItem(6, 80); check(!player.Packets.OfType<S.StoreItem>().Last().Success && player.Info.Inventory[6] == item, "直接仓库绕过扩展页限制");
            item.Info.Bind = BindMode.DontStore; player.StoreItem(6, 0);
            check(!player.Packets.OfType<S.StoreItem>().Last().Success && player.Info.Inventory[6] == item, "直接仓库绕过禁存绑定"); item.Info.Bind = BindMode.None;
            player.TradePartner = new Player(); player.StoreItem(6, 0); check(!player.CanOpenStorage && !player.Packets.OfType<S.StoreItem>().Last().Success, "交易中允许直接仓库存取"); player.TradePartner = null;
            player.Dead = true; player.StoreItem(6, 0); check(!player.CanOpenStorage && !player.Packets.OfType<S.StoreItem>().Last().Success, "死亡时允许直接仓库存取"); player.Dead = false;
            player.RequestStorage(); player.StoreItem(6, 0);
            check(!player.Packets.OfType<S.StoreItem>().Last().Success && player.Info.Inventory[6] == item, "重新打开仓库沿用了旧密码解锁状态");
        }
        finally { Settings.RequireStoragePassword = required; Packet.IsServer = wasServer; }
    }
}
