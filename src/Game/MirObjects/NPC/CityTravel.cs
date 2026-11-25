using System.Drawing;
using Server.MirEnvir;

namespace Server.MirObjects;

public static class CityTravel
{
    public static bool Allows(Map map) => !map.Info.NoTeleport && !map.Info.NoTownTeleport && !map.Info.NoReconnect &&
        !map.Info.RequiredGroup && !map.Info.GT && !map.Info.NeedHole && !map.Info.Fire && !map.Info.Lightning;

    public static bool Landing(Map map, Point point, bool safe) =>
        map != null && Allows(map) && map.ValidPoint(point) && (map.GetSafeZone(point) != null) == safe &&
        map.DoorIndex?[point.X, point.Y] == null && !map.Info.Movements.Any(move => move.Source == point) &&
        !map.Info.ActiveCoords.Contains(point) &&
        !map.Conquest.Any(site => site.Info.FullMap || Functions.InRange(site.Info.Location, point, site.Info.Size));

    internal static void TrySend(PlayerObject player, Map target, Point point, bool safe, uint gold)
    {
        var source = player.CurrentMap;
        var npc = source.NPCs.FirstOrDefault(n => n.ObjectID == player.NPCObjectID);
        if (!player.Dead && player.TradePartner == null && player.ItemRentalPartner == null && npc != null &&
            (safe ? gold == 0 : gold is >= 2000 and <= 50000) &&
            npc.Info.FileName.StartsWith("CityTravel/", StringComparison.Ordinal) &&
            Functions.InRange(npc.CurrentLocation, player.CurrentLocation, 4) &&
            Allows(source) && Landing(target, point, safe))
        {
            if (player.Account.Gold < gold)
            { player.NPCSpeech.Add($"金币不足，本次传送需要 {gold:N0} 金币。"); return; }
            var world = Envir.Main;
            ItemInfo scroll = null;
            if (!player.Info.Inventory.Any(item => item is { Count: > 0 } && item.Info.Type == ItemType.Scroll && item.Info.Shape == 1))
            {
                scroll = world.ItemInfoList.SingleOrDefault(item => item.Name == "回城卷轴" && item.Type == ItemType.Scroll && item.Shape == 1);
                if (scroll == null) { player.NPCSpeech.Add("当前版本缺少回城卷轴，暂不能传送。"); return; }
                // 只用无编号样本预检，拒绝时不能消耗唯一编号或先收款。
                var sample = new UserItem(scroll) { Count = 1 };
                if (!player.CanGainItem(sample) ||
                    player.Info.Inventory.Sum(item => item?.Weight ?? 0) + sample.Weight > player.Stats[Stat.BagWeight])
                { player.NPCSpeech.Add("请先为赠送的1张回城卷腾出背包空间和负重，再传送。"); return; }
            }
            // 原Teleport允许到达同一格，不因多人同时传送而拒绝或改变目的地。
            if (!player.Teleport(target, point))
            { player.NPCSpeech.Add("传送未成功，未收取金币，请稍后重试。"); return; }
            if (gold > 0)
            {
                player.Account.Gold -= gold;
                player.Enqueue(new ServerPackets.LoseGold { Gold = gold });
            }
            if (scroll != null) player.GainItem(world.CreateFreshItem(scroll));
            // 空对话沿用原 NPCResponse 关闭窗口；清掉旧会话拒绝重复确认。
            player.NPCSpeech.Clear();
            player.NPCPage = null;
            player.NPCObjectID = 0;
            return;
        }

        player.NPCSpeech.Add("当前无法传送。请靠近传送使者、结束交易后再试，并确认当前地图和目的地允许传送。");
    }
}
