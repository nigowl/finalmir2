using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyItemPrices()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-prices-", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("价格界面检查只允许隔离版本。");
        stage = "item prices native login and metadata";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading && State.User.Inventory.Any(item => item?.Info?.TradingPrices != null));
        var item = State.User!.Inventory.Single(item => item != null); var gold = State.User.Gold; var count = item.Count;
        var merchant = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        Client.SelectTarget(merchant.Id); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@BUY]" }); await Packet<S.NPCGoods>();
        var rows = (VBoxContainer)Client.FindChild("NpcGoods", true, false);
        foreach (var group in rows.GetChildren().OfType<Button>()) group.ButtonPressed = true;
        var buy = rows.FindChildren("GoodsItem", "Button", true, false).OfType<Button>().Single(button => button.Text.StartsWith(item.Info.Name + " ·", StringComparison.Ordinal));
        if (!buy.Text.Contains("100 金币")) throw new Exception("NPC商品列表未使用统一买价。");
        buy.EmitSignal(BaseButton.SignalName.Pressed);
        var dialog = (ConfirmationDialog)Client.FindChild("NpcTransactionDialog", true, false);
        if (!dialog.Visible || !dialog.FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text.Contains("合计 100")))
            throw new Exception("购买确认未显示统一买价。");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@SELL]" }); await Packet<S.NPCSell>();
        Client.UseItem(item.UniqueID);
        if (!dialog.Visible || !dialog.FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text.Contains("预计获得 37 金币")))
            throw new Exception("出售确认未显示独立卖价。");
        await Screenshot("item-prices-sale"); dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (State.User.Gold != gold || item.Count != count) throw new Exception("取消价格确认改变资产。");
        GD.Print("PASS native item prices: metadata, NPC list/buy quote, independent sale quote and canceled assets.");
        await Client.Quit();
    }
}
