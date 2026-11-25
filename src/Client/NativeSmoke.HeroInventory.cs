using Godot;
using System.Text.Json;
using System.Reflection;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHeroInventory()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("英雄共享背包实测仅允许隔离版本。");
        await Until(() => Client.HandshakeReady); Client.Login("TradeLeft", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.Hero != null && !Client.World.Loading);
        var user = State.User!; var hero = State.Hero!;
        string Snapshot() => JsonSerializer.Serialize(new { user.RealId, user.Gold, hero.Name,
            Inventory = user.Inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.ItemIndex}:{item.Count}"),
            PlayerEquipment = user.Equipment.Select(item => item?.UniqueID ?? 0), HeroEquipment = hero.Equipment.Select(item => item?.UniqueID ?? 0),
            PlayerSkills = user.Magics.Select(m => new { m.Spell, m.Level, m.Experience, m.Key }), HeroSkills = hero.Magics.Select(m => new { m.Spell, m.Level, m.Experience, m.Key }) });
        if (OS.GetCmdlineUserArgs().Contains("--verify-hero-save"))
        {
            if (Snapshot() != File.ReadAllText(Argument("--hero-inventory-record"))) throw new Exception("共享背包/英雄装备/普通技能及快捷键重启后不一致。");
            GD.Print("PASS native hero shared bag and independent equipment/ordinary skills/keys/IDs/counts after full restart."); await Client.Quit(); return;
        }
        if (!ReferenceEquals(user.Inventory, hero.Inventory) || !user.Inventory.Any(item => item?.Count == 7 && item.Info.Type == ItemType.Potion && item.Info.Shape == 0))
            throw new Exception("旧英雄物品没有进入唯一的共用背包。");
        var portrait = (Button)Client.FindChild("HeroShortcut", true, false);
        var window = GameArt("关闭角色").GetParent<Control>();
        var bagTitle = GameArt("关闭背包").GetParent().GetNode<Label>("WindowTitle");
        var weapon = user.Inventory.First(item => item?.Info.Type == ItemType.Weapon);
        var book = user.Inventory.First(item => item?.Info.Type == ItemType.Book);
        var potion = user.Inventory.First(item => item?.Info.Type == ItemType.Potion && item.Info.Shape == 1);
        await ClickSelectionControl(portrait); await Until(() => window.Visible && bagTitle.Text == "背包·英雄");
        var bag = GameArt("关闭背包").GetParent<Control>();
        if (bag.Visible) throw new Exception("打开英雄窗口自动打开了背包。");
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => bag.Visible);
        if (window.GetGlobalRect().Intersects(GameArt("关闭背包").GetParent<Control>().GetGlobalRect())) throw new Exception("英雄装备窗遮住共用背包。");
        stage = "shared potion applies to hero";
        var hp = hero.HP; var playerHP = user.HP; var count = potion.Count;
        packets.RemoveAll(p => p is S.UseItem or S.HeroHealthChanged);
        await ClickSelectionControl((Control)Client.FindChild("ItemShortcut1", true, false));
        var used = await Packet<S.UseItem>();
        await Until(() => hero.HP > hp);
        if (!used.Success || used.Grid != MirGridType.HeroInventory || potion.Count != count - 1 || user.HP != playerHP)
            throw new Exception("打开英雄窗后药品没有作用于英雄，或多扣/误加本人生命。");
        stage = "shared ordinary book and paperdoll equipment";
        async Task DoubleClickBook()
        {
            var cell = await VisibleBagCell(Array.FindIndex(user.Inventory, item => item?.UniqueID == book.UniqueID));
            var point = cell.GetGlobalRect().GetCenter(); await MoveMouse(point);
            using var press = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true };
            GetViewport().PushInput(press, true); MouseButtonAt(point, false);
        }
        packets.RemoveAll(p => p is S.UseItem); await DoubleClickBook();
        var learning = (ConfirmationDialog)Client.FindChild("LearnSkillConfirmation", true, false);
        await Until(() => learning.Visible);
        if (!learning.DialogText.Contains("英雄") || !learning.DialogText.Contains(book.Info.FriendlyName) || book.Count != 2 || packets.OfType<S.UseItem>().Any())
            throw new Exception("英雄学书确认对象/文案错误或确认前发送了学习请求。");
        await ClickSelectionControl(learning.GetCancelButton());
        if (learning.Visible) throw new Exception("学习确认的取消按钮未关闭窗口。");
        await DoubleClickBook(); await Until(() => learning.Visible);
        if (book.Count != 2 || packets.OfType<S.UseItem>().Any()) throw new Exception("取消学习仍消耗技能书。");
        GameArt("关闭角色").EmitSignal(BaseButton.SignalName.Pressed);
        if (learning.Visible || book.Count != 2 || packets.OfType<S.UseItem>().Any()) throw new Exception("切换背包对象没有撤销旧学习确认。");
        await ClickSelectionControl(portrait); await Until(() => window.Visible);
        await DoubleClickBook(); await Until(() => learning.Visible);
        await ClickSelectionControl(learning.GetOkButton());
        if (learning.Visible) throw new Exception("学习确认的学习按钮未关闭窗口。");
        used = await Packet<S.UseItem>();
        if (!used.Success || !hero.Magics.Any(m => m.Spell == Spell.FireBall) || user.Magics.Any(m => m.Spell == Spell.FireBall) || book.Count != 1)
            throw new Exception("普通技能书没有只教给英雄。");
        packets.RemoveAll(p => p is S.EquipItem); Client.UseItem(weapon.UniqueID);
        var equipped = await Packet<S.EquipItem>();
        if (!equipped.Success || equipped.Grid != MirGridType.HeroInventory || hero.Equipment[0]?.UniqueID != weapon.UniqueID || user.Equipment[0] != null)
            throw new Exception("打开英雄窗后装备没有穿到英雄。");
        await Screenshot("hero-shared-equipment");
        var tabs = window.GetChildren().OfType<OriginalButton>().Where(button => button.Text.Length > 0).ToArray();
        await ClickSelectionControl(tabs.Single(tab => tab.Text == "技能"));
        await Until(() => Client.FindChild("SkillFireBall", true, false) != null);
        var binding = Client.FindChild("SkillFireBall", true, false).GetChildren().OfType<OptionButton>().Single();
        await ClickSelectionControl(binding); await Until(() => binding.GetPopup().Visible);
        binding.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, binding.GetItemIndex(17)); binding.GetPopup().Hide();
        await Until(() => hero.Magics.Single().Key == 17 && Client.FindChild("SkillFireBall", true, false).GetChildren().OfType<OptionButton>().Single().GetSelectedId() == 17);
        await Screenshot("hero-shared-skills");
        var expansion = user.Inventory.First(item => item?.Info.Type == ItemType.Scroll && item.Info.Shape == 15);
        var size = user.Inventory.Length;
        packets.RemoveAll(p => p is S.UseItem); Client.UseItem(expansion.UniqueID);
        if ((await Packet<S.UseItem>()).Success || user.Inventory.Length != size || !user.Inventory.Contains(expansion)) throw new Exception("旧英雄扩包卷被错误消费。");
        stage = "close switches back to player";
        await ClickSelectionControl(GameArt("关闭角色")); await Until(() => !window.Visible && bagTitle.Text == "背包");
        packets.RemoveAll(p => p is S.UseItem); await DoubleClickBook();
        await Until(() => learning.Visible);
        if (!learning.DialogText.Contains("角色")) throw new Exception("关英雄窗后学习确认未恢复角色。");
        await ClickSelectionControl(learning.GetOkButton());
        used = await Packet<S.UseItem>();
        if (!used.Success || used.Grid != MirGridType.Inventory || !user.Magics.Any(m => m.Spell == Spell.FireBall) || hero.Magics.Count != 1)
            throw new Exception("关闭英雄窗后普通技能书没有恢复作用于角色。");
        await ClickSelectionControl(portrait); await Until(() => window.Visible);
        await ClickSelectionControl(tabs.Single(tab => tab.Text == "英雄"));
        var remove = Client.GetType().GetMethod("RemoveEquipment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        packets.RemoveAll(p => p is S.RemoveItem); remove.Invoke(Client, [0]);
        if (!(await Packet<S.RemoveItem>()).Success || hero.Equipment[0] != null || !user.Inventory.Any(item => item?.UniqueID == weapon.UniqueID))
            throw new Exception("英雄装备卸下未进入共用背包。");
        // 收到前关闭窗口，随后角色操作仍须等待同一个原回执，不能因窗口关闭解锁。
        packets.RemoveAll(p => p is S.EquipItem); Client.UseItem(weapon.UniqueID); GameArt("关闭角色").EmitSignal(BaseButton.SignalName.Pressed); Client.UseItem(weapon.UniqueID);
        equipped = await Packet<S.EquipItem>();
        if (!equipped.Success || hero.Equipment[0]?.UniqueID != weapon.UniqueID || user.Equipment[0] != null) throw new Exception("关闭窗口释放了未决装备锁或串换装对象。");
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (packets.OfType<S.EquipItem>().Any(p => p.Grid == MirGridType.Inventory)) throw new Exception("同一未决物品重复发给角色。");
        File.WriteAllText(Argument("--hero-inventory-record"), Snapshot());
        GD.Print("PASS native shared hero inventory: legacy migration, same paperdoll/ordinary books, real HP and consumption, player switch, equipment roundtrip and close-while-pending protection.");
        await Client.Quit();
    }
}
