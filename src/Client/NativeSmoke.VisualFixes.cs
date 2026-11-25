using System.Reflection;
using Crystal.Native;
using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyVisualFixes()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-actions.", StringComparison.Ordinal))
            throw new InvalidOperationException("界面修正检查必须使用隔离版本。");
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeWizard", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        prefs.SmartBattle = false; prefs.AutoSell = false;
        await VerifyIndependentCooldown();
        await VerifyNpcContinuity();
        await VerifyTerrainLayers();
        await ProfileActors();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await CombatScreenshot("visual-fixes-clean");
        GD.Print("PASS native visual fixes: independent skill cooldown/global input protection, NPC full idle/turn/blank-frame continuity, GPU animated-floor occlusion and interpolated draw depth.");
        await Client.Quit();
    }

    private async Task VerifyIndependentCooldown()
    {
        stage = "individual shortcut cooldowns with real cast acknowledgement";
        var skill = State.User!.Magics.Single(magic => magic.Spell == Spell.FireBall);
        var other = State.User.Magics.Single(magic => magic.Spell == Spell.MagicShield);
        Client.BindMagic(State.User.Magics.IndexOf(skill), 1); Client.BindMagic(State.User.Magics.IndexOf(other), 2);
        await Until(() => Client.FindChildren("*", "Button", true, false).OfType<SkillIconButton>().Any(button => button.Name == "SkillShortcut1" && button.Magic == skill));
        var icons = Client.FindChildren("*", "Button", true, false).OfType<SkillIconButton>().Where(button => button.ShowSlotFrame).ToArray();
        var used = icons.Single(button => button.Magic == skill); var unused = icons.Single(button => button.Magic == other);
        await Until(() => Client.Assets.Sprite("MagIcon2", skill.Icon * 2) != null && Client.Assets.Sprite("MagIcon2", other.Icon * 2) != null);
        Client.World.Selected = State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && !actor.Dead).Id;
        var otherCast = other.CastTime;
        packets.Clear();
        var started = Environment.TickCount64;
        Client.Cast(State.User.Magics.IndexOf(skill));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell == skill.Spell));
        var response = State.Me!.ActionTime - started;
        if (response is < 0 or >= 250) throw new Exception($"静止施法确认用时 {response}ms。");
        GD.Print($"PROFILE manual cast: stationary input to authoritative cast {response}ms.");
        var castAt = skill.CastTime;
        used.EmitSignal(BaseButton.SignalName.Pressed);
        if (skill.CastTime != castAt || unused.CooldownRemaining != 0 || other.CastTime != otherCast)
            throw new Exception("公共施法间隔污染其他图标，或允许重复施法。");
        // 只为图标绘制设置本地长短冷却样本，不向服务端改技能参数。
        var delay = skill.Delay;
        try
        {
            skill.Delay = 4000;
            if (used.CooldownRemaining <= 0 || unused.CooldownRemaining != 0) throw new Exception("图标没有独立倒计时。");
            await CombatScreenshot("independent-cooldowns");
        }
        finally { skill.Delay = delay; }
    }

    private async Task VerifyNpcContinuity()
    {
        stage = "NPC complete idle clips, direction changes and empty source frames";
        const uint id = 0x70002001;
        var npc = new Actor { Id = id, Kind = ObjectType.Merchant, Image = 0, Name = "动作连续检查",
            Location = new Point(State.User!.Location.X - 2, State.User.Location.Y - 1) };
        State.Actors[id] = npc;
        try
        {
            await Until(() => Client.World.ActorBody(npc, Environment.TickCount64) != null && Enumerable.Range(0, 3).All(direction =>
                Client.Assets.Frames("NPC/00")!.Frames.Values.All(frame => Client.Assets.PrepareAnimation("NPC/00", frame, (MirDirection)direction))));
            await VerifyActorNameCentres(npc, State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && !actor.Dead));
            var frames = Client.Assets.Frames("NPC/00")!.Frames;
            for (var direction = 0; direction < 3; direction++)
            for (var now = 0; now < 8000; now += 50)
            {
                State.Apply(new S.ObjectTurn { ObjectID = id, Location = npc.Location, Direction = (MirDirection)direction });
                if (Client.World.ActorBody(npc, now) == null) throw new Exception("NPC完整待机/转向存在空画面。");
            }
            var cache = (Dictionary<(string, int), SpriteImage?>)typeof(ResourceCache).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client.Assets)!;
            var indices = frames.Values.SelectMany(frame => Enumerable.Range(0, frame.Count).Select(at => frame.Start + 2 * frame.OffSet + at)).Distinct().ToArray();
            var original = indices.ToDictionary(index => index, index => cache.GetValueOrDefault(("NPC/00", index)));
            try
            {
                foreach (var index in indices) cache[("NPC/00", index)] = null;
                npc.Direction = (MirDirection)2;
                if (Client.World.ActorBody(npc, Environment.TickCount64) == null) throw new Exception("NPC新方向缺帧时没有保留可用造型。");
            }
            finally { foreach (var (index, image) in original) cache[("NPC/00", index)] = image; }
            await CombatScreenshot("npc-continuity");
        }
        finally { State.Actors.Remove(id); }
    }

    private async Task VerifyTerrainLayers()
    {
        stage = "GPU animated floor beneath player and foot-position draw sorting";
        await Until(() => Client.World.ActorBody(State.Me!, Environment.TickCount64) != null);
        var world = Client.World; var at = State.User!.Location; var me = State.Me!;
        var cache = (Dictionary<(string, int), SpriteImage?>)typeof(ResourceCache).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client.Assets)!;
        const int index = 30000;
        var library = WorldView.MapLibrary(2);
        using var image = Image.CreateEmpty(96, 64, false, Image.Format.Rgba8); image.Fill(Colors.Magenta);
        var sprite = new SpriteImage(ImageTexture.CreateFromImage(image), Vector2.Zero, new Vector2(96, 64));
        var cell = world.Map!.MapCells[at.X, at.Y + 1];
        var previous = (cell.FrontIndex, cell.FrontImage, cell.FrontAnimationFrame);
        var add = typeof(ResourceCache).GetMethod("CacheTexture", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var frame = 0; frame < 2; frame++)
        {
            if (cache.ContainsKey((library, index + frame))) throw new Exception("诊断帧编号已被实际地图占用。");
            add.Invoke(Client.Assets, [(library, index + frame), sprite]);
        }
        try
        {
            cell.FrontIndex = 2; cell.FrontImage = index + 1; cell.FrontAnimationFrame = 2;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            var screen = world.Screen(at); var scale = screenshot.GetWidth() / 1024f;
            var colour = screenshot.GetPixel((int)((screen.X + 24) * scale), (int)((screen.Y + 8) * scale));
            var probe = screenshot.GetPixel((int)((screen.X + 24) * scale), (int)((screen.Y + 40) * scale));
            if (probe.R < .95 || probe.B < .95 || probe.G > .05) throw new Exception("地表诊断帧未实际绘制，不能判断遮挡。");
            if (colour.R > .95 && colour.B > .95 && colour.G < .05) throw new Exception("带动画的地表仍画在人物前景层。");
            var move = new Actor { Id = 0x70002002, Kind = ObjectType.Player, Location = at, Previous = at, Class = me.Class };
            State.Actors[move.Id] = move;
            move.Move(new Point(at.X, at.Y + 1), MirDirection.Down, MirAction.Walking, Environment.TickCount64 - 100);
            try
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var order = (List<(Actor Actor, float Depth)>)typeof(WorldView).GetField("drawActors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
                var depth = order.Single(entry => entry.Actor == move).Depth;
                if (depth <= at.Y || depth >= move.Location.Y) throw new Exception("人物遮挡仍按确认终点跳变，未使用当前脚底位置。");
            }
            finally { State.Actors.Remove(move.Id); }
        }
        finally
        {
            (cell.FrontIndex, cell.FrontImage, cell.FrontAnimationFrame) = previous;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var nodes = (Dictionary<(string, int), LinkedListNode<(string, int)>>)typeof(ResourceCache).GetField("textureNodes", flags)!.GetValue(Client.Assets)!;
            var order = (LinkedList<(string, int)>)typeof(ResourceCache).GetField("textureOrder", flags)!.GetValue(Client.Assets)!;
            var bytes = typeof(ResourceCache).GetField("textureBytes", flags)!;
            for (var frame = 0; frame < 2; frame++)
            {
                var key = (library, index + frame);
                if (cache.Remove(key, out var removed) && removed != null) bytes.SetValue(Client.Assets, (long)bytes.GetValue(Client.Assets)! - (long)removed.Size.X * (long)removed.Size.Y * 4);
                if (nodes.Remove(key, out var node)) order.Remove(node);
            }
        }
    }
}
