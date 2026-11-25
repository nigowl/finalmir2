using Godot;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGridMovement()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("格位与英雄管理检查仅允许隔离版本。");
        await Until(() => Client.HandshakeReady); Client.Login("TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.Hero != null && !Client.World.Loading);
        var heroId = State.Hero!.ObjectID;
        string Snapshot() => JsonSerializer.Serialize(new { State.User!.RealId, State.User.Gold, Active = State.ActiveHero!.Index,
            Stored = State.StoredHeroes.Select(hero => hero?.Index ?? 0),
            Inventory = State.User.Inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.ItemIndex}:{item.Count}"),
            HeroEquipment = State.Hero!.Equipment.Select(item => item?.UniqueID ?? 0) });
        if (OS.GetCmdlineUserArgs().Contains("--verify-grid-save"))
        {
            if (State.MaximumHeroCount != 5 || State.StoredHeroes.Count(hero => hero != null) != 3 ||
                State.StoredHeroes.Any(hero => hero?.Name == "第五格位英雄") || State.ActiveHero?.Name != "格位英雄1")
                throw new Exception("英雄数量/删除/当前身份重启后不一致。");
            if (File.ReadAllText(Argument("--grid-record")) != Snapshot()) throw new Exception("英雄身份、装备或共用背包重启后改变。");
            GD.Print("PASS native grid hero restart: five-slot capacity, deleted hero absent, four original identities retained.");
            await Client.Quit(); return;
        }
        var guide = State.Actors.Values.Single(actor => actor.Name == "英雄使者");
        async Task Page(string key)
        {
            packets.RemoveAll(packet => packet is S.NPCResponse or S.HeroCreateRequest or S.ManageHeroes);
            Client.Send(new C.CallNPC { ObjectID = guide.Id, Key = key });
            if (key == "[@CREATEHERO]") await Packet<S.HeroCreateRequest>();
            else if (key == "[@MANAGEHERO]") await Packet<S.ManageHeroes>();
            else await Packet<S.NPCResponse>();
        }
        stage = "NPC task link above close and five-hero limit";
        await Page("[@MAIN]");
        var task = (LinkButton)Client.FindChild("NpcQuestLink", true, false);
        var close = task.GetParent().GetChildren().OfType<Control>().Single(control =>
            control.GetChildren().OfType<LinkButton>().Any(link => link.Text == "关闭"));
        if (task.GetIndex() >= close.GetIndex()) throw new Exception("查看任务没有排列在关闭上方。");
        await Until(() => task.Visible); await Screenshot("npc-task-before-close");
        await Page("[@CREATEHERO]");
        packets.RemoveAll(packet => packet is S.NewHero);
        Client.Send(new C.NewHero { Name = "第五格位英雄", Class = MirClass.Warrior, Gender = MirGender.Male });
        if ((await Packet<S.NewHero>()).Result != 10) throw new Exception("第五个英雄创建失败。");
        var seal = State.User!.Inventory.Single(item => item?.Info.Type == ItemType.SealedHero);
        var fifthId = seal.AddedStats[Stat.Hero];
        await Page("[@MAIN]"); await Page("[@CREATEHERO]"); packets.RemoveAll(packet => packet is S.NewHero);
        Client.Send(new C.NewHero { Name = "第六格位英雄", Class = MirClass.Warrior, Gender = MirGender.Male });
        if ((await Packet<S.NewHero>()).Result != 4 || !State.User.Inventory.Contains(seal)) throw new Exception("未激活的第五个封印被忽略，允许创建第六个英雄。");
        ((ConfirmationDialog)Client.FindChild("HeroCreation", true, false)).Hide();
        Client.Send(new C.UseItem { UniqueID = seal.UniqueID, Grid = MirGridType.Inventory }); await Packet<S.UseItem>();
        await Page("[@MAIN]"); await Page("[@MANAGEHERO]");
        if (State.MaximumHeroCount != 5 || State.StoredHeroes.Count(hero => hero != null) != 4) throw new Exception("管理列表没有显示五个英雄。");
        var list = (ItemList)Client.FindChild("ManagedHeroes", true, false);
        var slot = Array.FindIndex(State.StoredHeroes, hero => hero?.Index == fifthId) + 1;
        var row = Enumerable.Range(0, list.ItemCount).Single(index => list.GetItemMetadata(index).AsInt32() == slot);
        list.Select(row); list.EmitSignal(ItemList.SignalName.ItemSelected, row);
        var panel = (Control)Client.FindChild("HeroManagement", true, false);
        var deletion = panel.GetChildren().OfType<Button>().Single(button => button.Text == "删除");
        await ClickSelectionControl(deletion);
        var confirmation = (ConfirmationDialog)Client.FindChild("HeroDeletion", true, false);
        await Until(() => confirmation.Visible);
        if (!confirmation.DialogText.Contains("第五格位英雄")) throw new Exception("删除确认没有绑定选中的英雄身份。");
        await ClickSelectionControl(confirmation.GetCancelButton());
        if (!State.StoredHeroes.Any(hero => hero?.Index == fifthId)) throw new Exception("取消删除仍改变英雄。");
        await ClickSelectionControl(deletion); await Until(() => confirmation.Visible);
        packets.RemoveAll(packet => packet is S.DeleteHeroResult or S.ManageHeroes);
        await ClickSelectionControl(confirmation.GetOkButton());
        if (!(await Packet<S.DeleteHeroResult>()).Success) throw new Exception("原TCP删除英雄失败。");
        await Packet<S.ManageHeroes>();
        if (State.StoredHeroes.Count(hero => hero != null) != 3 || State.ActiveHero?.Name != "格位英雄1" || State.User.Inventory.Any(item => item?.UniqueID == seal.UniqueID))
            throw new Exception("英雄删除改变其他英雄或保留封印。");
        await Screenshot("hero-five-slots-delete"); panel.Hide(); ((Control)Client.FindChild("NpcDialog", true, false)).Hide();
        stage = "original TCP grid walking, corners and hero follow radius";
        var movementPackets = 0; var runs = 0; var lastUserLocation = State.User.Location;
        void Count(Packet packet)
        {
            if (packet is S.ObjectWalk walk && walk.ObjectID == heroId || packet is S.ObjectRun run && run.ObjectID == heroId) movementPackets++;
            if (packet is S.UserLocation location)
            { if (Functions.MaxDistance(lastUserLocation, location.Location) > 1) runs++; lastUserLocation = location.Location; }
        }
        Client.PacketReceived += Count;
        var samples = 0;
        void Validate()
        {
            foreach (var actor in new[] { State.Me!, State.Actors[heroId] })
            {
                var origin = Client.World.Visual(actor, actor.MotionTime); var corner = new Vector2(actor.Previous.X, actor.Previous.Y);
                var end = new Vector2(actor.Location.X, actor.Location.Y); var actual = Client.World.Visual(actor);
                if (!OnGridSegment(actual, origin, corner) && !OnGridSegment(actual, corner, end)) throw new Exception("原生画面斜切确认格位路径。");
                if (Client.World.CellAt(Client.World.Screen(actor.Location) + new Vector2(24, 16)) != actor.Location)
                    throw new Exception("画面格中心与点击格不匹配。");
                samples++;
            }
        }
        try
        {
            for (var i = 0; i < 24; i++)
            {
                var preferred = (MirDirection)(i % 8);
                var direction = Enumerable.Range(0, 8).Select(offset => (MirDirection)(((int)preferred + offset) % 8)).First(dir =>
                {
                    var tile = Functions.PointMove(State.User.Location, dir, 1);
                    return Client.World.Walkable(tile) && !State.Actors.Values.Any(actor => actor.Id != State.User.ObjectID &&
                        actor.Kind is ObjectType.Player or ObjectType.Monster or ObjectType.Merchant && actor.Location == tile);
                });
                var destination = Functions.PointMove(State.User.Location, direction, 1);
                Client.Send(new C.Walk { Direction = direction });
                await Until(() => { Validate(); return State.User.Location == destination; });
                var due = Environment.TickCount64 + State.PlayerTiming.MoveMilliseconds;
                await Until(() => { Validate(); return Environment.TickCount64 >= due; });
            }
            var runStart = State.User.Location;
            var runRoute = Enum.GetValues<MirDirection>().Where(direction => (int)direction < 8)
                .Select(direction => Client.World.PathTo(Functions.PointMove(runStart, direction, 8)))
                .First(route => route.Length == 9 && route.Zip(route.Skip(1), Main.Direction).Distinct().Count() == 1);
            foreach (var destination in new[] { runRoute[^1], runStart })
            {
                Client.SetDestination(destination, true);
                await Until(() => { Validate(); return State.User.Location == destination; });
            }
            if (runs == 0) throw new Exception("跑步专项没有收到原服多格移动回执。");
            await Until(() => { Validate(); var hero = State.Actors[heroId]; return Functions.MaxDistance(State.User.Location, hero.Location) <= 3 &&
                Environment.TickCount64 - hero.MotionTime > hero.MovementDuration(600); });
            var heroLocation = State.Actors[heroId].Location; var before = movementPackets;
            foreach (var direction in Enum.GetValues<MirDirection>().Where(dir => (int)dir < 8))
            {
                Client.Send(new C.Turn { Direction = direction });
                var due = Environment.TickCount64 + State.PlayerTiming.TurnMilliseconds + 80;
                await Until(() => { Validate(); return Environment.TickCount64 >= due; });
            }
            if (State.Actors[heroId].Location != heroLocation || movementPackets != before) throw new Exception("主人原地转向仍让范围内英雄追逐固定格。");
            await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Spell && actor.ActionSpell == Spell.TrapHexagon && !actor.SpellParam) &&
                Client.Assets.Sprite("Magic", 1390) != null);
            await Screenshot("grid-follow-original-border");
            GD.Print($"PASS native grid movement: {samples} player/hero path and cell-centre samples, {runs} original run acknowledgements, {movementPackets} hero steps, eight owner turns without hero repositioning; original safe-zone border, task link order, fifth creation/sixth rejection, delete confirmation/cancel/original-TCP result.");
        }
        finally { Client.PacketReceived -= Count; }
        File.WriteAllText(Argument("--grid-record"), Snapshot());
        await Client.Quit();
    }

    private static bool OnGridSegment(Vector2 point, Vector2 from, Vector2 to) =>
        Math.Abs((point.X - from.X) * (to.Y - from.Y) - (point.Y - from.Y) * (to.X - from.X)) < .0001f &&
        point.X >= Math.Min(from.X, to.X) - .0001f && point.X <= Math.Max(from.X, to.X) + .0001f &&
        point.Y >= Math.Min(from.Y, to.Y) - .0001f && point.Y <= Math.Max(from.Y, to.Y) + .0001f;
}
