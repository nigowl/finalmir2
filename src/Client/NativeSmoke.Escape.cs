using Godot;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyAutomaticEscape(Point start)
    {
        stage = "automatic random scroll, authoritative teleport and emergency town scroll";
        var random = State.User!.Inventory.First(item => item?.Info is { Type: ItemType.Scroll, Shape: 2 });
        var town = State.User.Inventory.First(item => item?.Info is { Type: ItemType.Scroll, Shape: 1 });
        var randomCount = random.Count; var townCount = town.Count;
        var panel = SettingsPanel(); Setting(panel, "无怪随机").ButtonPressed = true; Setting(panel, "无怪跑图").ButtonPressed = false;
        CloseSettingsPanel(panel); packets.RemoveAll(packet => packet is S.UseItem or S.MapChanged);
        ToggleBattleMenu();
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == random.UniqueID));
        var randomResult = packets.OfType<S.UseItem>().Single(packet => packet.UniqueID == random.UniqueID);
        var randomLeft = State.User.Inventory.FirstOrDefault(item => item?.UniqueID == random.UniqueID)?.Count ?? 0;
        await Until(() => !Client.World.Loading);
        if (!randomResult.Success || randomLeft != randomCount - 1 || !packets.OfType<S.MapChanged>().Any(packet => packet.MapIndex == 1 && packet.Location == State.User.Location))
            throw new Exception("Automatic random consumed the wrong count or omitted authoritative teleport coordinates.");
        var header = (Label)Client.FindChild("WorldStatus", true, false);
        if (!header.Text.Contains("挂机中")) throw new Exception("A requested same-map random teleport stopped automation.");
        ToggleBattleMenu();
        panel = SettingsPanel(); Setting(panel, "无怪随机").ButtonPressed = false; CloseSettingsPanel(panel);
        var injury = State.User.Inventory.Single(item => item?.Info?.Name == "NativeInjury"); var hp = State.User.HP;
        packets.RemoveAll(packet => packet is S.UseItem or S.MapChanged);
        Client.UseItem(injury.UniqueID);
        if (!(await Packet<S.UseItem>()).Success) throw new Exception("The isolated injury item did not lower HP for protection testing.");
        await Until(() => State.User.HP < hp && State.User.HP > 0);
        panel = SettingsPanel(); Setting(panel, "自动回城").ButtonPressed = true;
        panel.FindChildren("*", "SpinBox", true, false).Cast<SpinBox>().OrderBy(input => input.Position.Y).Last().Value = 95;
        CloseSettingsPanel(panel);
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == town.UniqueID));
        var townResult = packets.OfType<S.UseItem>().Single(packet => packet.UniqueID == town.UniqueID);
        var townLeft = State.User.Inventory.FirstOrDefault(item => item?.UniqueID == town.UniqueID)?.Count ?? 0;
        await Until(() => !Client.World.Loading && State.User.Location == start);
        if (!townResult.Success || townLeft != townCount - 1 || !packets.OfType<S.MapChanged>().Any(packet => packet.Location == start))
            throw new Exception("Emergency town protection did not consume exactly one scroll and restore the authoritative bind coordinate.");
        panel = SettingsPanel(); Setting(panel, "自动回城").ButtonPressed = false; CloseSettingsPanel(panel);
        GD.Print("PASS native escape: no-monster random retains automation, original scroll shapes/counts, server teleport synchronization, actual low-HP threshold and automatic town return.");
    }
}
