using Client.MirObjects;
using Godot;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyCombatPresentation()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("战斗表现检查必须使用隔离原生fixture。");
        await EnterUiFixture(); await VerifyCrtPixels();
        stage = "learned Fencing normal attack, complete original sword trail and real damage";
        var npc = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        var start = new Point(npc.Location.X + 2, npc.Location.Y - 1);
        Client.SetDestination(start); await Until(() => State.User!.Location == start);
        var target = ClientAutomation.Targets(State).First();
        var near = Enumerable.Range(-1, 3).SelectMany(x => Enumerable.Range(-1, 3).Select(y => new Point(target.Location.X + x, target.Location.Y + y)))
            .First(point => point != target.Location && Client.World.Walkable(point) && (point == State.User!.Location || Client.World.NextStep(point) != null));
        Client.SetDestination(near); await Until(() => State.User!.Location == near);
        if (!CombatPresentation.HasBasicSword(State, State.Me)) throw new Exception("隔离原生角色未通过原服学习基础剑术。");
        var direction = Main.Direction(near, target.Location); var clip = OriginalSpellEffects.BasicSword(direction);
        await Until(() => Client.World.MeleeReady(Spell.None, 0, direction) && Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
        var frames = new HashSet<int>(); void Drawn(string library, int frame) { if (library == clip.Library && frame >= clip.Index && frame < clip.Index + clip.Count) frames.Add(frame); }
        Client.World.EffectFrameDrawn += Drawn;
        try
        {
            packets.Clear(); Client.Attack(direction, Spell.None);
            if (State.Me!.ActionSpell != Spell.None) throw new Exception("基础剑气篡改了普通攻击技能编号。");
            await Until(() => frames.Count >= 4 && packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage < 0));
            using var hitFrame = await CombatFrame(); await Until(() => frames.Count == 6);
            if (Argument("--screenshot") is { Length: > 0 } path) hitFrame.SavePng(Path.ChangeExtension(path, "basic-sword-real-hit.png"));
        }
        finally { Client.World.EffectFrameDrawn -= Drawn; }
        await VerifyCombatText(target);
        GD.Print("PASS native combat presentation: settings footer, CRT1/3/5 real scanline/RGB pixels and off restoration; server-learned Fencing six-frame normal trail, real hit, damage scaling/critical/heal/miss, buff dedupe/pause/expiry and bounded map cleanup.");
        await Client.Quit();
    }

    private async Task VerifyCrtPixels()
    {
        stage = "settings footer and physical CRT scanline/phosphor sampling";
        var panel = SettingsPanel(); var reset = (Button)panel.FindChild("ResetSettings", true, false); var hint = (Label)panel.FindChild("SettingsNotice", true, false);
        if (panel.Size != new Vector2(440, 350) || Math.Abs(reset.GetRect().GetCenter().Y - hint.GetRect().GetCenter().Y) > .5f ||
            reset.Position.Y < panel.Size.Y - 34 || hint.Position.Y < panel.Size.Y - 34 || reset.GetRect().End.Y > panel.Size.Y - 4 ||
            hint.GetRect().End.Y > panel.Size.Y - 4 || hint.GetRect().End.X >= reset.Position.X)
            throw new Exception("设置提示或重置按钮仍压在底部装饰/内框线上。");
        await Screenshot("settings-footer-fixed");
        var toggle = Setting(panel, "CRT"); var strength = (OptionButton)panel.FindChild("CrtStrength", true, false);
        toggle.ButtonPressed = false; CloseSettingsPanel(panel);
        var layer = new CanvasLayer { Layer = 99 }; Client.AddChild(layer);
        var sample = new ColorRect { Size = new Vector2(256, 128), Color = new Color(.65f, .65f, .65f), MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(sample);
        try
        {
            using var off = await CombatFrame(); var contrasts = new List<float>();
            foreach (var level in new[] { 1, 3, 5 })
            {
                panel = SettingsPanel(); toggle.ButtonPressed = true; SelectSetting(strength, level - 1); CloseSettingsPanel(panel);
                using var frame = await CombatFrame(); var rows = new List<float>(); var channels = new HashSet<int>();
                for (var y = 50; y < 82; y++)
                {
                    var sum = 0f;
                    for (var x = 50; x < 82; x++)
                    {
                        var colour = frame.GetPixel(x, y); sum += (colour.R + colour.G + colour.B) / 3;
                        if (Math.Max(colour.R, Math.Max(colour.G, colour.B)) - Math.Min(colour.R, Math.Min(colour.G, colour.B)) > .035f)
                            channels.Add(colour.R > colour.G && colour.R > colour.B ? 0 : colour.G > colour.B ? 1 : 2);
                    }
                    rows.Add(sum / 32);
                }
                contrasts.Add(rows.Max() - rows.Min());
                if (contrasts[^1] < .035f || channels.Count != 3) throw new Exception($"CRT{level}没有可见横纹或RGB点阵：contrast={contrasts[^1]}，channels={channels.Count}。");
            }
            if (contrasts[2] <= contrasts[0] * 1.5f) throw new Exception("CRT强度1–5没有递增强度。");
            panel = SettingsPanel(); toggle.ButtonPressed = false; CloseSettingsPanel(panel);
            using var restored = await CombatFrame();
            if (off.GetPixel(70, 70) != restored.GetPixel(70, 70)) throw new Exception("关闭CRT后没有恢复原始像素。");
            sample.Hide();
            panel = SettingsPanel(); toggle.ButtonPressed = true; SelectSetting(strength, 2); CloseSettingsPanel(panel);
            using var clean = await CombatFrame(); var colours = new HashSet<uint>();
            for (var y = 50; y < 82; y++) for (var x = 50; x < 82; x++) colours.Add(clean.GetPixel(x, y).ToRgba32());
            if (colours.Count < 60) throw new Exception("CRT左上角仍残留采样色块。");
            await CombatScreenshot("crt-game-clean");
            panel = SettingsPanel(); toggle.ButtonPressed = false; CloseSettingsPanel(panel);
            GD.Print($"PASS native CRT pixels: contrast levels1/3/5={string.Join('/', contrasts.Select(value => value.ToString("F3")))}, all three phosphor channels, off pixel restored.");
        }
        finally { layer.QueueFree(); }
    }

    private async Task<Image> CombatFrame()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetViewport().GetTexture().GetImage();
    }

    private async Task CombatScreenshot(string suffix)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (Argument("--screenshot") is { Length: > 0 } path) Capture(Path.ChangeExtension(path, suffix + ".png"));
    }

    private async Task VerifyCombatText(Actor target)
    {
        stage = "floating hit/buff presentation lifecycle without changing HP/MP";
        var world = Client.World; var hp = State.User!.HP; var mp = State.User.MP;
        foreach (var (amount, type) in new[] { (-9, DamageType.Hit), (-120, DamageType.Hit), (-2400, DamageType.Critical) })
            world.ShowDamage(target.Id, amount, type);
        world.ShowDamage(State.User.ObjectID, 0, DamageType.Miss); world.ShowDamage(State.User.ObjectID, 88);
        var text = world.CombatTexts;
        if (text.Single(item => item.Text == "9").Size >= text.Single(item => item.Text == "120").Size ||
            text.Single(item => item.Text == "120").Size >= text.Single(item => item.Text == "暴击 2400").Size || !text.Any(item => item.Text == "+88") || !text.Any(item => item.Text == "未命中"))
            throw new Exception("伤害大小/原暴击/治疗/闪避飘字没有区分。");
        var buff = new S.AddBuff { Buff = new ClientBuff { ObjectID = State.User.ObjectID, Type = BuffType.Rage, Visible = true } };
        world.SpellPacket(buff); var count = text.Count; world.SpellPacket(buff);
        if (text.Count != count || !text.Any(item => item.Status && item.Text == "+ 狂暴")) throw new Exception("Buff刷新重复飘字或没有中文名称。");
        world.SpellPacket(new S.PauseBuff { ObjectID = State.User.ObjectID, Type = BuffType.Rage, Paused = true });
        world.SpellPacket(new S.PauseBuff { ObjectID = State.User.ObjectID, Type = BuffType.Rage, Paused = false });
        world.SpellPacket(new S.RemoveBuff { ObjectID = State.User.ObjectID, Type = BuffType.Rage });
        if (!text.Any(item => item.Text.StartsWith("恢复")) || !text.Any(item => item.Text.EndsWith("消失")) || State.User.HP != hp || State.User.MP != mp)
            throw new Exception("Buff生命周期丢失或表现改变HP/MP。");
        await CombatScreenshot("combat-floating-text");
        if (world.CombatTextsDrawn < 8) throw new Exception("飘字仅在队列中，未实际绘制到独立前景层。");
        count = text.Count;
        world.SpellPacket(new S.AddBuff { Buff = new ClientBuff { ObjectID = target.Id, Type = BuffType.Hiding, Visible = false } });
        world.SpellPacket(new S.PauseBuff { ObjectID = target.Id, Type = BuffType.Hiding, Paused = false });
        world.SpellPacket(new S.RemoveBuff { ObjectID = target.Id, Type = BuffType.Hiding });
        if (text.Count != count) throw new Exception("不可见Buff或未知恢复包泄露了名称。");
        var shield = new S.ObjectEffect { ObjectID = State.User.ObjectID, Effect = SpellEffect.MagicShieldUp };
        world.SpellPacket(shield); count = text.Count; world.SpellPacket(shield);
        if (text.Count != count || !text.Any(item => item.Text == "+ 魔法盾")) throw new Exception("护盾提示缺失或重复。");
        for (var index = 0; index < 200; index++) world.ShowDamage(target.Id, -index - 1);
        if (text.Count != 128) throw new Exception("飘字列表没有固定上限。");
        world.SpellPacket(new S.MapChanged());
        if (text.Count != 0) throw new Exception("切图残留旧战斗飘字。");
    }
}
