using Godot;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyAutoBattleInput()
    {
        stage = "auto-battle survives UI actions and confirms ground cancellation";
        EnableAutomaticBattle(false);
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        var settings = SettingsPanel();
        using var bag = new InputEventKey { PhysicalKeycode = Key.I, Pressed = true };
        Client._UnhandledInput(bag);
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "背包 I");
        await ClickSelectionControl(button);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("设置/快捷键/界面按钮停止了挂机。");
        CloseSettingsPanel(settings);
        await Until(()=>Client.World.Visual(State.Me!)==new Vector2(State.User!.Location.X,State.User.Location.Y));
        var target = Enumerable.Range(-3, 7).SelectMany(x => Enumerable.Range(-3, 7).Select(y => new Point(State.User!.Location.X + x, State.User.Location.Y + y)))
            .OrderBy(point=>Functions.MaxDistance(point,State.User!.Location))
            .First(point => point != State.User!.Location && Client.World.Walkable(point) && Client.World.NextStep(point) != null &&
                Client.World.ActorAt(Client.World.Screen(point)+new Vector2(24,16))==null && !State.Actors.Values.Any(actor => actor.Location == point));
        var position = Client.World.Screen(target) + new Vector2(24, 16);
        await ClickMapTeleport(target);
        var teleportConfirm = (ConfirmationDialog)Client.FindChild("StopBattleConfirmation", true, false);
        if (!teleportConfirm.Visible || !teleportConfirm.DialogText.Contains("传送") || menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机")
            throw new Exception("挂机中右键地图未确认便传送或提前停止挂机。");
        teleportConfirm.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("取消地图传送仍停止了挂机。");
        Client.MiniMap.SetExpanded(false);
        position = Client.World.Screen(target) + new Vector2(24, 16);
        using var ground = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = true };
        Client._UnhandledInput(ground);
        var confirm = (ConfirmationDialog)Client.FindChild("StopBattleConfirmation", true, false);
        if (!confirm.Visible || menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("点击地面没有确认或提前停止了挂机。");
        await Screenshot("auto-battle-stop-confirmation");
        if (!confirm.Visible) throw new Exception("停止挂机确认在等待用户操作时意外关闭。");
        confirm.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("取消地面停止确认仍结束了挂机。");
        // 取消期间挂机仍可能移动镜头，第二次点击要重新投影同一个世界目标。
        ground.Position=Client.World.Screen(target)+new Vector2(24,16);ground.GlobalPosition=ground.Position;
        Client._UnhandledInput(ground);
        if (!confirm.Visible) throw new Exception("取消后再次点击地面没有显示停止确认。");
        confirm.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "开始挂机") throw new Exception("确认停止没有停止挂机。");
        try{await Until(() => State.User!.Location == target);}
        catch(TimeoutException){throw new Exception($"确认停止后的目标未到达：目标{target}，当前位置{State.User!.Location}，剩余路线{string.Join(',',Client.MiniMap.Route)}");}
        Client.SetDestination(State.User!.Location);
        GD.Print("PASS native auto-battle input: settings/hotkeys/buttons retain running state, ground click confirms, cancel keeps running, confirm stops and moves.");
    }
}
