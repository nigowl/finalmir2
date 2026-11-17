using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyStoragePagesOnly()
    {
        await EnterUiFixture();
        stage = "four storage tabs, live unlock guidance and locked-page protection";
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        ((Button)Client.FindChild("InventoryStorage", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
        var panel = GameArt("关闭仓库").GetParent<Control>();
        var tabs = panel.GetChildren().OfType<OriginalButton>().Where(button => button.ThemeTypeVariation == "GameTab").OrderBy(button => button.Position.X).ToArray();
        await Screenshot("storage-pending");
        try { await Until(() => tabs.All(tab => tab.GetThemeStylebox("pressed") is StyleBoxTexture)); }
        catch (TimeoutException) { throw new Exception("页签底图未就绪：" + string.Join(";", Client.Assets.Errors.Take(8))); }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (tabs.Length != 4 || tabs.Any(tab => tab.Size != new Vector2(68, ClientUi.TabHeight)) || !tabs[0].ButtonPressed)
            throw new Exception("仓库四页签缺失、越界或默认未选择仓库一：" + string.Join(";", tabs.Select(tab => $"{tab.Text}/{tab.Size}/{tab.ButtonPressed}")));
        for (var page = 1; page < 4; page++)
        {
            tabs[page].EmitSignal(BaseButton.SignalName.Pressed);
            if (tabs.Count(tab => tab.ButtonPressed) != 1 || !tabs[page].ButtonPressed ||
                !panel.GetChildren().OfType<Label>().Any(label => label.Visible && label.Text.Contains("未开通") && label.Text.Contains("完成") && label.Text.Contains("（")))
                throw new Exception("锁定页没有显示当前版本真实NPC/地图/任务提示。");
            await CombatScreenshot("storage-page-" + (page + 1));
        }
        tabs[0].EmitSignal(BaseButton.SignalName.Pressed); await CombatScreenshot("storage-page-1");
        GameArt("关闭仓库").EmitSignal(BaseButton.SignalName.Pressed);
        var npc = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        packets.Clear(); Client.SelectTarget(npc.Id); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@STORAGE]" }); await Packet<S.NPCStorage>();
        Client.EnterStoragePassword("Store123", confirmation: "Store123");
        if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("仓库一原密码流程失效。");
        await Until(() => Client.CanUseStorage);
        Client.SelectStoragePage(3); var original = State.User!.Inventory.First(item => item != null); var slot = Array.IndexOf(State.User.Inventory, original);
        packets.Clear(); Client.Send(new C.StoreItem { From = slot, To = 240 });
        if ((await Packet<S.StoreItem>()).Success || State.User.Inventory[slot]?.UniqueID != original.UniqueID)
            throw new Exception("未开通仓库四仍允许存入或移动了源物品。");
        GD.Print("PASS native storage: four original-art tabs, unique selection, live NPC/map/quest guidance; default page-one password flow and original TCP page-four rejection preserve item ID.");
        await Client.Quit();
    }
}
