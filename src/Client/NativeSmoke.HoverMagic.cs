using System.Reflection;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHoverMagic()
    {
        stage = "hover-only Taoist poison and talisman original TCP";
        await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady);
        Client.Login("NativeTaoist", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User?.Class == MirClass.Taoist && !Client.World.Loading && State.Actors.Values.Any(actor => actor.Kind == ObjectType.Monster));
        var preferences = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        preferences.AutoSell = false; preferences.SmartBattle = false; preferences.AutoSwapAmulet = true;
        var target = State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && !actor.Dead);
        await MoveMouse(Client.World.NameAnchor(target, Environment.TickCount64));
        Client.World.Selected = 0; packets.Clear();
        Client.Cast(State.User!.Magics.FindIndex(magic => magic.Spell == Spell.Poisoning));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.Poisoning && packet.Cast && packet.TargetID == target.Id) && target.Poison != PoisonType.None);
        var poison = State.User.Magics.First(magic => magic.Spell == Spell.Poisoning);
        await Until(() => Environment.TickCount64 >= poison.CastTime + State.PlayerTiming.CastDelay(poison.Spell));
        await MoveMouse(Client.World.NameAnchor(target, Environment.TickCount64));
        Client.World.Selected = 0; packets.Clear();
        var beforeMp = State.User.MP;
        var beforeAmulets = State.User.Inventory.Concat(State.User.Equipment).Where(item => item?.Info.Type == ItemType.Amulet && item.Info.Shape == 0).Sum(item => item.Count);
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.SoulFireBall));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.SoulFireBall && packet.Cast && packet.TargetID == target.Id));
        await Until(() => State.User.MP < beforeMp && State.User.Inventory.Concat(State.User.Equipment).Where(item => item?.Info.Type == ItemType.Amulet && item.Info.Shape == 0).Sum(item => item.Count) == beforeAmulets - 1);
        if (Client.World.Selected != 0) throw new Exception("悬停施法依赖了点击选中。");
        await CombatScreenshot("hover-talisman");
        await VerifyBlockedTalisman(target);
    }

    private async Task VerifyBlockedTalisman(Actor target)
    {
        stage = "Taoist navigates around actual terrain before firing talisman";
        var origin = State.User!.Location;
        var choices = Enumerable.Range(-9, 19).SelectMany(dx => Enumerable.Range(-9, 19).Select(dy => new System.Drawing.Point(target.Location.X + dx, target.Location.Y + dy)))
            .Where(point => Functions.MaxDistance(point, target.Location) is >= 3 and <= 9 && Client.World.Walkable(point) &&
                !Functions.CanFly(point, target.Location, Client.World.Walkable))
            .Select(point => (Point: point, Route: Client.World.PathTo(point)))
            .Where(choice => choice.Route.Length is >= 2 and <= 36 && !choice.Route.Contains(new System.Drawing.Point(origin.X + 4, origin.Y)))
            .OrderBy(choice => choice.Route.Length).ToArray();
        if (choices.Length == 0) throw new Exception("真实地图缺少可达的隔墙射击检查位置。");
        var blocked = choices[0].Point;
        Client.SetDestination(blocked, true);
        await Until(() => State.User.Location == blocked && !(bool)typeof(Main).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!);
        if (Client.World.ClearShot(target.Location)) throw new Exception("隔墙检查目标已移动到无遮挡位置。");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", flags)!.GetValue(Client)!;
        prefs.SmartBattle = true;
        typeof(Main).GetField("classSkillsPaused", flags)!.SetValue(Client, true);
        Client.SelectTarget(target.Id);
        typeof(Main).GetField("autoBattleEnabled", flags)!.SetValue(Client, true);
        packets.Clear();
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.SoulFireBall && packet.TargetID == target.Id && packet.Cast) &&
            packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage != 0));
        if (State.User.Location == blocked || packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.SoulFireBall && packet.TargetID == 0))
            throw new Exception("隔墙时没有先换位，仍然原地释放无效火符。");
        typeof(Main).GetField("autoBattleEnabled", flags)!.SetValue(Client, false);
        Client.SetDestination(State.User.Location);
        await CombatScreenshot("talisman-obstacle");
        GD.Print("PASS native talisman obstacle: real blocked terrain, original TCP movement around wall, then targeted cast and actual damage without empty casts.");
    }
}
