using Godot;
using Client.MirObjects;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGoldNotices()
    {
        stage = "real gold pickup before acquiring the next visible monster";
        PickupSettings(true, false, "");
        var before = State.User!.Gold; var known = State.Actors.Keys.ToHashSet();
        packets.Clear(); Client.Send(new C.DropGold { Amount = 37 });
        await Until(() => State.User.Gold == before - 37 && State.Actors.Values.Any(actor => !known.Contains(actor.Id) && actor.Gold == 37));
        var gold = State.Actors.Values.Single(actor => !known.Contains(actor.Id) && actor.Gold == 37);
        if (gold.Image != 112) throw new Exception("真实丢弃金币未使用少量金币素材。");
        var away = Enum.GetValues<MirDirection>().Select(direction => Functions.PointMove(gold.Location, direction, 3))
            .First(point => Client.World.PathTo(point).Length is >= 2 and <= 6);
        Client.SetDestination(away); await Until(() => State.User.Location == away);
        const uint id = 0x71000000;
        // 仅增加本地候选验证调度优先级，金币仍由原服丢弃/拾取，数量以原回执为准。
        State.Actors[id] = new Actor { Id = id, Kind = ObjectType.Monster, Image = (int)Monster.ArcherGuard,
            Name = "拣取优先级检查", Location = Functions.PointMove(State.User.Location, MirDirection.Right, 3) };
        try
        {
            EnableAutomaticBattle(false);
            await Until(() => !State.Actors.ContainsKey(gold.Id) && State.User.Gold == before);
            ToggleBattleMenu();
            if (packets.OfType<S.GainedGold>().Count(packet => packet.Gold == 37) != 1) throw new Exception("金币重复到账或不是原TCP回执。");
        }
        finally { State.Actors.Remove(id); }
        var notices = (VBoxContainer)Client.FindChild("ItemNotices", true, false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var labels = notices.FindChildren("*", "Label", true, false).OfType<Label>().ToArray();
        if (!notices.Visible || !labels.Any(label => label.Text == "获得金币 ×37") || notices.Position.Y < Client.MiniMap.GetRect().End.Y ||
            notices.GetRect().End.X > 1016.5f || labels.Any(label => label.GetRect().Size.X > 220) || notices.MouseFilter != Control.MouseFilterEnum.Ignore ||
            labels.Any(label => label.MouseFilter != Control.MouseFilterEnum.Ignore || label.GetParent<Control>().MouseFilter != Control.MouseFilterEnum.Ignore))
            throw new Exception("小地图下方物品消息缺失、越界或拦截鼠标。");
        await CombatScreenshot("gold-notices");
        Client.MiniMap.SetExpanded(true);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (notices.Visible) throw new Exception("大地图打开后仍遮挡显示消息。");
        Client.MiniMap.SetExpanded(false);
        await Until(() => notices.GetChildCount() == 0);
        PickupSettings(false, false, "");
        GD.Print("PASS gold/notices: original DropGold/PickUp/GainedGold/ObjectRemove, exactly 37 restored from three cells away despite a new monster candidate; minimap placement, mouse pass-through and three-second expiry.");
    }

    private async Task VerifyCombatStance()
    {
        stage = "complete normal attack followed by original combat stance";
        var direction = State.Me!.Direction;
        await Until(() => Client.World.MeleeReady(Spell.None, 0, direction) && Environment.TickCount64 - State.Me.MotionTime > 1200);
        var before = State.Me.ActionTime; Client.Attack(direction, Spell.None);
        await Until(() => State.Me.ActionTime != before);
        await Until(() => Environment.TickCount64 - State.Me.ActionTime >= 650);
        var art = OriginalPlayerArt.Resolve(State.Me.Class, State.Me.Gender, State.Me.Armour, State.Me.Hair, State.Me.Weapon, MirAction.Stance);
        var frame = Client.World.ActorFrame(State.Me, Environment.TickCount64, out _);
        if (frame != Client.Assets.Frames(art.Body)!.Frames[MirAction.Stance] || State.Me.Action != MirAction.Attack1)
            throw new Exception("攻击后仍立即跳回站立，或更改了权威动作状态。");
        await CombatScreenshot("combat-stance");
        GD.Print("PASS combat stance: original attack completed, ready pose retained without changing attack speed or protocol state.");
    }
}
