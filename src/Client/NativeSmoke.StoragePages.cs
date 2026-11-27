using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyPortableStorage()
    {
        stage = "direct storage without any NPC interaction";
        GameArt("关闭背包").GetParent<Control>().Show(); packets.Clear();
        var open = (Control)Client.FindChild("InventoryStorage", true, false);
        await ClickSelectionControl(open); await Packet<S.NPCStorage>();
        if (Client.CanUseStorage || packets.Any(packet => packet is S.NPCResponse)) throw new Exception("直接仓库没有保留密码保护或绕路调用NPC。");
        Client.EnterStoragePassword("Store123", confirmation: "Store123");
        if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("直接仓库密码设置失败。");
        await Until(() => Client.CanUseStorage && State.StorageCapacity == 80);
        var panel = GameArt("关闭仓库").GetParent<Control>();
        if (panel.GetChildren().OfType<Label>().Any(label => label.Visible && label.Position.Y >= 310)) throw new Exception("仓库底部仍显示提示文案。");
        var source = State.User!.Inventory.First(item => item?.Info.Name == "整理试炼卷轴"); var id = source.UniqueID; var count = source.Count;
        var from = Array.IndexOf(State.User.Inventory, source);
        packets.Clear(); Client.StoreInventory(from, 0);
        if (!(await Packet<S.StoreItem>()).Success) throw new Exception("无NPC存入被原服拒绝。");
        await Until(() => State.Storage[0]?.UniqueID == id && State.User.Inventory[from] == null);
        packets.Clear(); Client.MoveStorage(0, 1);
        if (!(await Packet<S.MoveItem>()).Success) throw new Exception("无NPC仓库内移动失败。");
        packets.Clear(); Client.TakeStorage(1, from);
        if (!(await Packet<S.TakeBackItem>()).Success || State.User.Inventory[from]?.UniqueID != id || State.User.Inventory[from]?.Count != count)
            throw new Exception("无NPC取回未保持原编号和数量。");
        Client.Send(new C.StoreItem { From = from, To = 80 });
        if ((await Packet<S.StoreItem>()).Success || State.User.Inventory[from]?.UniqueID != id) throw new Exception("直接仓库越权开放了仓库二。");
        GameArt("关闭仓库").EmitSignal(BaseButton.SignalName.Pressed);
        await ClickSelectionControl(open); await Packet<S.NPCStorage>();
        if (Client.CanUseStorage) throw new Exception("直接仓库重开没有重新验证密码。");
        Client.EnterStoragePassword("Wrong123"); if ((await Packet<S.StorageUnlockResult>()).Result != 2 || Client.CanUseStorage) throw new Exception("错误密码打开了仓库。");
        Client.EnterStoragePassword("Store123"); if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("直接仓库正确密码解锁失败。");
        await Until(() => Client.CanUseStorage); await Screenshot("direct-storage-no-footer");
        GameArt("关闭仓库").EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print("PASS native direct storage: no NPC dialogue, default 80 cells, original store/move/take receipts preserve ID/count, locked expansion rejected, reopen/wrong password protected and footer removed.");
    }

    private async Task VerifyStoragePagesOnly()
    {
        await EnterUiFixture();
        stage = "four storage tabs, live unlock guidance and locked-page protection";
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        ((Button)Client.FindChild("InventoryStorage", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
        await Packet<S.NPCStorage>();
        var panel = GameArt("关闭仓库").GetParent<Control>();
        var tabs = panel.GetChildren().OfType<OriginalButton>().Where(button => button.ThemeTypeVariation == "GameTab").OrderBy(button => button.Position.X).ToArray();
        Client.EnterStoragePassword("Store123", confirmation: "Store123");
        if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("无NPC仓库一原密码流程失效。");
        await Until(() => Client.CanUseStorage);
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
        Client.SelectStoragePage(3); var original = State.User!.Inventory.First(item => item != null); var slot = Array.IndexOf(State.User.Inventory, original);
        packets.Clear(); Client.Send(new C.StoreItem { From = slot, To = 240 });
        if ((await Packet<S.StoreItem>()).Success || State.User.Inventory[slot]?.UniqueID != original.UniqueID)
            throw new Exception("未开通仓库四仍允许存入或移动了源物品。");
        GD.Print("PASS native storage: direct page-one access/password without NPC, four original-art tabs, unique selection, live unlock guidance and original TCP page-four rejection preserve item ID.");
        await Client.Quit();
    }
}
