using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyItemOwners()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-item-names.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("绑定名称检查只允许独立版本。");
        stage = "bound item original character login";
        await Until(() => Client.HandshakeReady); Client.Login("BoundObserver", "Native123");
        var roles = (await Packet<S.LoginSuccess>()).Characters;
        Client.Send(new C.StartGame { CharacterIndex = roles.Single(role => role.Name == "绑定观察者").Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var user = State.User!; var remoteId = (uint)user.Inventory[7].SoulBoundId;
        string Snapshot() => JsonSerializer.Serialize(new { user.RealId, user.Name, user.Gold,
            Items = user.Inventory.Concat(user.Equipment).Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura, item.SoulBoundId }) });
        var before = Snapshot();
        var restored = OS.GetCmdlineUserArgs().Contains("--verify-item-owners-save");
        if (restored && Snapshot() != File.ReadAllText(Argument("--item-owner-record"))) throw new Exception("完整重启改变本人身份/金币/物品或绑定编号。");
        if (user.ObjectID == user.RealId || State.Actors.ContainsKey(remoteId)) throw new Exception("夹具未区分场景编号与离线角色数据库编号。");
        GameArt("关闭背包").GetParent<Control>().Show();
        string Text(ItemTipButton cell) => cell.OpenTooltip?.FindChild("ItemDetails", true, false) is RichTextLabel text ? text.GetParsedText() : "";
        async Task<ItemTipButton> Hover(int slot)
        {
            await MoveMouse(new Vector2(600, 400));
            var cell = (ItemTipButton)Client.FindChild($"InventorySlot{slot}", true, false);
            await MoveMouse(cell.GetGlobalRect().GetCenter());
            await Until(() => GodotObject.IsInstanceValid(cell.OpenTooltip) && cell.OpenTooltip!.IsVisibleInTree()); return cell;
        }
        stage = "self bound name uses RealId without a lookup";
        var self = await Hover(6);
        if (!Text(self).Contains("灵魂绑定角色：绑定观察者")) throw new Exception("本人灵魂绑定物品未显示真实角色名称。");
        stage = "offline bound owner original request and visible tooltip update";
        var foreign = await Hover(7);
        await Until(() => packets.OfType<S.UserName>().Any(packet => packet.Id == remoteId));
        await Until(() => GodotObject.IsInstanceValid(foreign.OpenTooltip) && Text(foreign).Contains("灵魂绑定角色：离线绑定主人"));
        if (foreign.OpenTooltip!.FindChild("EquippedComparison", true, false) is not RichTextLabel comparison ||
            !comparison.GetParsedText().Contains("灵魂绑定角色：绑定观察者") || !comparison.GetParsedText().Contains("本人对照戒"))
            throw new Exception("姓名刷新丢失已装备对比列或混用两件装备的绑定主人。");
        if (packets.OfType<S.UserName>().Count(packet => packet.Id == remoteId) != 1 || user.Name != "绑定观察者" || State.Me!.Name != user.Name ||
            State.Actors.ContainsKey(remoteId)) throw new Exception("绑定姓名查询重复请求或改动本人/场景身份。");
        await Screenshot("bound-owner-name");
        stage = "same owner reuse and unknown owner placeholder";
        var same = await Hover(8); if (!Text(same).Contains("离线绑定主人")) throw new Exception("同一主人名称未复用缓存。");
        var unknown = await Hover(9); if (!Text(unknown).Contains("未知角色")) throw new Exception("未知绑定主人被猜成其他角色。");
        await Hover(7);
        var stamp = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = stamp });
        await Until(() => packets.OfType<S.KeepAlive>().Any(packet => packet.Time == stamp));
        if (packets.OfType<S.UserName>().Count(packet => packet.Id == remoteId) != 1 || packets.OfType<S.UserName>().Any(packet => packet.Id == user.RealId) ||
            Snapshot() != before)
            throw new Exception("反复悬停重复查名、查询本人或改变原物品资产。");
        if (!restored) File.WriteAllText(Argument("--item-owner-record"), Snapshot());
        await VerifyItemTooltipScroll(new UserItem(new ItemInfo { Name = "滚动验证样本", Type = ItemType.Weapon }));
        GD.Print($"PASS native item owners{(restored ? " saved" : "")}: original TCP offline character #{remoteId}, visible self/foreign/unknown tooltips, cached lookup, distinct scene identity and preserved bound item IDs/assets.");
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>(); await Client.Quit();
    }
}
