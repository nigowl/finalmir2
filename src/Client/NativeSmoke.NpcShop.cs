using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNpcShopOnly()
    {
        await EnterUiFixture();
        stage = "confirmed original NPC buying/selling and exclusive item notices";
        var merchant = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        packets.Clear(); Client.SelectTarget(merchant.Id); await Packet<S.NPCResponse>();
        var quest = (LinkButton)Client.FindChild("NpcQuestLink", true, false);
        if (quest.GetParent().Name != "NpcText" || quest.Text != "查看 任务") throw new Exception("任务链接没有放进对白。");
        await CombatScreenshot("npc-task-inline");
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@BUY]" }); var goods = await Packet<S.NPCGoods>();
        await VerifyNpcGoods(goods);
        var rows = (VBoxContainer)Client.FindChild("NpcGoods", true, false);
        foreach (var group in rows.GetChildren().OfType<Button>()) group.ButtonPressed = true;
        var potion = goods.List.First(entry => entry.Info.Type == ItemType.Potion && entry.Info.Stats[Stat.HP] > 0);
        var item = rows.FindChildren("GoodsItem", "Button", true, false).OfType<ItemTipButton>().First(button => button.TooltipText.StartsWith(potion.Info.FriendlyName, StringComparison.Ordinal));
        var dialog = (ConfirmationDialog)Client.FindChild("NpcTransactionDialog", true, false);
        var quantity = dialog.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().Single();
        var gold = State.User!.Gold; var before = State.User.Inventory.Sum(entry => entry?.Count ?? 0);
        item.EmitSignal(BaseButton.SignalName.Pressed);
        if (!dialog.Visible || State.User.Gold != gold) throw new Exception("点击商品未先确认或提前扣款。");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (State.User.Gold != gold || State.User.Inventory.Sum(entry => entry?.Count ?? 0) != before) throw new Exception("取消购买改变资产。");
        item.EmitSignal(BaseButton.SignalName.Pressed); quantity.Value = 2;
        packets.Clear(); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
        await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.StartsWith("购买成功：")));
        if (State.User.Gold >= gold || State.User.Inventory.Sum(entry => entry?.Count ?? 0) != before + 2) throw new Exception("购买确认没有按原回执只买两件。");
        var purchased = packets.OfType<S.GainedItem>().Single().Item;
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        if (messages.GetParsedText().Contains("购买成功：")) throw new Exception("物品成功消息仍写入聊天框。");
        await CombatScreenshot("npc-purchase-confirmed");
        packets.Clear(); Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@SELL]" }); await Packet<S.NPCSell>();
        var inventoryItem = State.User.Inventory.First(entry => entry?.ItemIndex == purchased.ItemIndex);
        gold = State.User.Gold; before = State.User.Inventory.Where(entry => entry?.ItemIndex == purchased.ItemIndex).Sum(entry => entry!.Count);
        Client.UseItem(inventoryItem!.UniqueID);
        if (!dialog.Visible || State.User.Gold != gold) throw new Exception("出售没有确认。");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (State.User.Inventory.Where(entry => entry?.ItemIndex == purchased.ItemIndex).Sum(entry => entry!.Count) != before) throw new Exception("取消出售扣除了物品。");
        Client.UseItem(inventoryItem.UniqueID); packets.Clear(); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
        if (!(await Packet<S.SellItem>()).Success || State.User.Inventory.Where(entry => entry?.ItemIndex == purchased.ItemIndex).Sum(entry => entry!.Count) != before - 1)
            throw new Exception("出售确认未按原回执扣一件。");
        if (messages.GetParsedText().Contains("出售成功：")) throw new Exception("出售消息未独立分流。");
        await CombatScreenshot("npc-sale-confirmed");
        GameArt("关闭 NPC").EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print("PASS native NPC shop: inline task link, expandable deduplicated rows/icons, no back button; canceled buy/sell preserve assets, confirmed exact quantities/gold, duplicate protection and item-only notices.");
        await Client.Quit();
    }
}
