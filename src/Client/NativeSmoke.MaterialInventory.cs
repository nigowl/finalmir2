using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyMaterialInventory()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS") ?? "").Contains("/mir2-balance.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("材料界面验证仅允许隔离设置和版本。");
        stage = "native material inventory title, cells and original storage receipts";
        await Until(() => Client.HandshakeReady); Client.Login("BalanceWarrior", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.Hero != null && !Client.World.Loading);
        var bag = GameArt("关闭背包").GetParent<Control>(); bag.Show(); bag.Position = new(12, 48);
        var open = (OriginalButton)Client.FindChild("InventoryQuestItems", true, false);
        if (open.Text != "材料物品" || open.TooltipText != "材料物品") throw new Exception("材料入口仍显示旧任务名称。");
        await ClickSelectionControl(open);
        var panel = GameArt("关闭材料物品").GetParent<Control>(); panel.Position = new(345, 240);
        await Until(() => panel.Visible);
        if (!panel.GetChildren().OfType<Label>().Any(label => label.Text == "材料物品")) throw new Exception("材料窗口标题未更新。");
        var cells = panel.GetChildren().OfType<ItemTipButton>().OrderBy(button => button.Position.Y).ThenBy(button => button.Position.X).ToArray();
        if (cells.Length != State.User!.QuestInventory.Length) throw new Exception("材料栏可见格位与服务器不一致。");
        if (panel.Size.Y != 236 || cells.Any(cell => cell.GetRect().End.Y > panel.Size.Y))
            throw new Exception("材料栏裁切了原窗口高度或最后一排格子。");
        using (var skin = panel.GetChildren().OfType<TextureRect>().Single().Texture.GetImage())
        using (var frame = OriginalArt.DialogSkin(Client.Assets.Sprite("Title", 196)!))
        using (var original = frame.GetImage())
        {
            if (skin.GetSize() != original.GetSize()) throw new Exception("材料栏底图尺寸与原完整边框不一致。");
            for (var y = 0; y < skin.GetHeight(); y++)
                for (var x = 0; x < skin.GetWidth(); x++)
                {
                    var cleared = x is >= 20 and < 293 && y is >= 211 and < 231;
                    var expected = cleared ? original.GetPixel(32 + (x - 20) % 248, 10 + y - 211) : original.GetPixel(x, y);
                    if (skin.GetPixel(x, y) != expected) throw new Exception("材料栏金币/负重底图未移除，或改动了原边框/格子。");
                }
        }
        var slot = Array.FindIndex(State.User.QuestInventory, item => item?.Info.Name == "材料验证矿石");
        var ore = State.User.QuestInventory[slot]; var id = ore.UniqueID; var count = ore.Count; var purity = ore.CurrentDura;
        if (cells[slot].Text != count.ToString() || State.User.Inventory.Any(item => item?.UniqueID == id)) throw new Exception("材料数量显示错误或普通包出现副本。");
        await Screenshot("material-inventory");
        var warehouse = (Control)Client.FindChild("InventoryStorage", true, false);
        stage = "native materials: original storage password setup";
        packets.Clear(); await ClickSelectionControl(warehouse); await Packet<S.NPCStorage>();
        if (!Client.CanUseStorage)
        {
            if (State.User.HasStoragePassword)
            {
                Client.EnterStoragePassword("Store123");
                if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("材料存仓的原仓库密码解锁失败。");
            }
            else
            {
                Client.EnterStoragePassword("Store123", confirmation: "Store123");
                if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("材料存仓的原仓库密码设置失败。");
            }
        }
        await Until(() => Client.CanUseStorage);
        var storage = GameArt("关闭仓库").GetParent<Control>(); storage.Position = new(680, 48);
        stage = "native materials: real double-click into storage";
        var point = cells[slot].GetGlobalRect().GetCenter(); await MoveMouse(point);
        using (var press = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true }) GetViewport().PushInput(press, true);
        using (var release = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left }) GetViewport().PushInput(release, true);
        if (!(await Packet<S.StoreItem>()).Success) throw new Exception("材料格双击未按原存仓回执成功。");
        await Until(() => State.Storage.Any(item => item?.UniqueID == id) && State.User.QuestInventory[slot] == null);
        packets.Clear(); Client.TakeStorage(Array.FindIndex(State.Storage, item => item?.UniqueID == id));
        if (!(await Packet<S.TakeBackItem>()).Success) throw new Exception("材料取回失败。");
        await Until(() => State.User.QuestInventory.Any(item => item?.UniqueID == id));
        var restored = State.User.QuestInventory.Single(item => item?.UniqueID == id);
        if (restored.Count != count || restored.CurrentDura != purity || State.User.Inventory.Any(item => item?.UniqueID == id)) throw new Exception("材料存取改变数量/纯度或复制物品。");
        await Screenshot("material-storage");
        GameArt("关闭仓库").EmitSignal(BaseButton.SignalName.Pressed);
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        GD.Print("PASS native material inventory: Chinese entry/title, 40 visible cells, exact quantities, real double-click store and original take/auto-route preserve IDs/counts/purity.");
        GetTree().Quit();
    }
}
