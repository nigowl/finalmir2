using System.Diagnostics;
using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

// 仅由隔离技能场景显式启动，战斗走原 TCP，额外远端角色只用于素材逐帧检查。
public partial class NativeSpellSmoke : Node
{
    public Main Client = null!;
    private readonly List<Packet> packets = new();
    private readonly HashSet<(string Library, int Frame)> drawn = new();
    private string stage = "连接";
    private GameState State => Client.State;
    private static string Argument(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(name + "="))?[(name.Length + 1)..] ?? "";

    public override async void _Ready()
    {
        Client.PacketReceived += packet => packets.Add(packet);
        Client.World.EffectFrameDrawn += (library, frame) => drawn.Add((library, frame));
        try
        {
            if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-skills-")) throw new InvalidOperationException("技能检查必须使用隔离缓存和版本。");
            await Until(() => Client.HandshakeReady);
            Client.Login("NativeSkills", "Native123"); await Until(() => State.Characters.Count > 0);
            Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Class == MirClass.Warrior).Index });
            await Until(() => State.User != null && !Client.World.Loading && State.Actors.Values.Any(actor => actor.Kind == ObjectType.Monster));
            var target = State.Actors.Values.Where(actor => actor.Kind == ObjectType.Monster).OrderBy(actor => Functions.MaxDistance(actor.Location, State.User!.Location)).First();
            await VerifyFlamingSword(target);
            await VerifyMagic(target);
            await VerifyMeleeFrames();
            await VerifyGroundAndShield();
            await VerifySpellGallery(target);
            GD.Print("PASS native skill effects: actual TCP flame preparation/MP/hit, caster and remote six-frame melee, all original melee types, projectile/area/healing/shield/ground rendering, rejection and map-effect cleanup.");
            await LeaveCharacter();
            await VerifyClassSkills();
            GetTree().Quit();
        }
        catch (Exception error)
        {
            var chat = ((RichTextLabel)Client.FindChild("ChatMessages", true, false)).GetParsedText();
            GD.PrintErr($"FAIL native skills [{stage}]: {error}\nBuffs={string.Join(',', State.UserBuffs.Keys)}, shield={State.Me?.MagicShield}/{State.Me?.ElementalBarrier}, concentration={State.Me?.Concentrating}\nMagic={string.Join(';', packets.OfType<S.Magic>().Select(packet => $"{packet.Spell}:{packet.Cast}"))}\nChat={chat[^Math.Min(650, chat.Length)..]}"); GetTree().Quit(1);
        }
    }

    private async Task Until(Func<bool> condition, int seconds = 40)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException($"{stage}：{string.Join(" / ", packets.OfType<S.Chat>().TakeLast(3).Select(chat => chat.Message))}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private async Task Pause(int milliseconds) => await ToSignal(GetTree().CreateTimer(milliseconds / 1000d), SceneTreeTimer.SignalName.Timeout);
    private int Skill(Spell spell) => State.User!.Magics.FindIndex(magic => magic.Spell == spell);
    private async Task Shot(string name)
    {
        if (Argument("--screenshot") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = Client.GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(directory, name + ".png")) != Error.Ok) throw new IOException("技能截图保存失败。");
    }

    private async Task VerifyFlamingSword(Actor target)
    {
        stage = "烈火真实TCP准备/耗蓝/命中与完整六帧";
        var direction = Main.Direction(State.User!.Location, target.Location);
        await Until(() => Client.World.MeleeReady(Spell.FlamingSword, 3, direction));
        var mp = State.User.MP;
        Client.Cast(Skill(Spell.FlamingSword));
        await Until(() => State.ActiveSkills.Contains(Spell.FlamingSword) && State.User.MP < mp);
        var paid = State.User.MP;
        if (packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage < 0)) throw new Exception("准备烈火提前产生了伤害。");
        packets.RemoveAll(packet => packet is S.DamageIndicator); drawn.Clear();
        Client.Attack(direction);
        await Until(() => drawn.Contains(("Magic", 3480 + (int)direction * 10 + 2)));
        await Shot("flaming-sword");
        await Until(() => drawn.Contains(("Magic", 3480 + (int)direction * 10 + 5)));
        await Until(() => packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage < 0));
        if (State.Me!.ActionSpell != Spell.FlamingSword || State.ActiveSkills.Contains(Spell.FlamingSword) || State.User.MP != paid)
            throw new Exception("烈火本地技能类型/一次性准备/攻击不重复扣蓝不正确。");
        await Pause(1900);
    }

    private async Task VerifyMagic(Actor target)
    {
        foreach (var spell in new[] { Spell.FireBall, Spell.ThunderBolt, Spell.SoulFireBall, Spell.IceStorm, Spell.Healing, Spell.MagicShield, Spell.FireWall })
        {
            stage = $"{spell} 真实TCP及原版素材"; packets.Clear(); drawn.Clear();
            if (spell == Spell.FireWall)
            {
                // 原火墙按地图对象规则禁止安全区攻击；先离开出生安全格再核对持续伤害。
                var point = new Point(State.User!.Location.X, State.User.Location.Y - 1);
                Client.SetDestination(point); await Until(() => State.User.Location == point); await Pause(700);
            }
            Client.World.Selected = target.Id;
            var hp = State.User!.HP;
            var magic = State.User!.Magics[Skill(spell)];
            await Until(() => Environment.TickCount64 >= magic.CastTime + magic.Delay);
            Client.Cast(Skill(spell));
            await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == spell && packet.Cast));
            var cast = OriginalSpellEffects.Casting(spell, State.Me!.Direction).FirstOrDefault();
            if (cast.Count > 0) await Until(() => drawn.Any(frame => frame.Library == cast.Library && frame.Frame >= cast.Index && frame.Frame < cast.Index + cast.Count));
            var impact = OriginalSpellEffects.Missile(spell)?.Impact ?? OriginalSpellEffects.Impact(spell, State.Me.Direction);
            if (impact is { } hit)
                await Until(() => drawn.Any(frame => frame.Library == hit.Library && frame.Frame >= hit.Index && frame.Frame < hit.Index + hit.Count));
            if (spell is Spell.FireBall or Spell.ThunderBolt or Spell.SoulFireBall or Spell.IceStorm)
                await Until(() => packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0));
            if (spell == Spell.MagicShield) await Until(() => State.Me.MagicShield && drawn.Contains(("Magic", 3890)));
            if (spell == Spell.Healing) await Until(() => State.User.HP > hp);
            if (spell == Spell.FireWall)
            {
                await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Spell && actor.ActionSpell == Spell.FireWall));
                stage = "FireWall持续命中：" + target.Id;
                await Until(() => packets.OfType<S.DamageIndicator>().Count(packet => packet.ObjectID == target.Id && packet.Damage < 0) >= 2);
            }
            await Shot(spell.ToString()); await Pause(1900);
        }
    }

    private async Task VerifyMeleeFrames()
    {
        var id = uint.MaxValue - 1;
        State.Actors[id] = new Actor { Id = id, Name = "远端技能检查", Kind = ObjectType.Player, Armour = 0, Weapon = 1,
            Location = new(State.User!.Location.X - 2, State.User.Location.Y - 1) };
        try
        {
            foreach (var spell in new[] { Spell.Slaying, Spell.Thrusting, Spell.HalfMoon, Spell.CrossHalfMoon, Spell.DoubleSlash, Spell.TwinDrakeBlade, Spell.FlamingSword })
            {
                stage = $"远端{spell}完整素材帧";
                await Until(() => Client.World.MeleeReady(spell, 3, MirDirection.Down)); drawn.Clear();
                State.Apply(new S.ObjectAttack { ObjectID = id, Location = State.Actors[id].Location, Direction = MirDirection.Down, Spell = spell, Level = 3 });
                var first = OriginalSpellEffects.Melee(spell, 3, MirDirection.Down)!.Value;
                await Until(() => Enumerable.Range(first.Index, first.Count).All(frame => drawn.Contains((first.Library, frame))));
                if (spell == Spell.DoubleSlash)
                {
                    var second = OriginalSpellEffects.Melee(spell, 3, MirDirection.Down, true)!.Value;
                    await Until(() => Enumerable.Range(second.Index, second.Count).All(frame => drawn.Contains((second.Library, frame))));
                }
                await Shot("remote-" + spell);
            }
        }
        finally { State.Actors.Remove(id); }
    }

    private async Task VerifyGroundAndShield()
    {
        stage = "持续效果移除及非阻挡/非点击对象";
        var id = uint.MaxValue;
        var point = new Point(State.User!.Location.X, State.User.Location.Y - 1);
        var packet = new S.ObjectSpell { ObjectID = id, Location = point, Spell = Spell.FireWall };
        State.Apply(packet); Client.World.SpellPacket(packet);
        var path = Client.World.PathTo(point);
        if (path.Length < 2 || Client.World.ActorAt(Client.World.Screen(point) + new Vector2(24, 16))?.Id == id)
            throw new Exception("火墙错误阻挡寻路或成为鼠标目标。");
        State.Apply(new S.ObjectRemove { ObjectID = id });
        State.Apply(new S.ObjectEffect { ObjectID = State.User.ObjectID, Effect = SpellEffect.MagicShieldDown });
        if (State.Me!.MagicShield) throw new Exception("护盾结束未清理。");
        var image = State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster).Image;
        var colours = new[] { PoisonType.Green, PoisonType.Red | PoisonType.Green };
        try
        {
            for (var i = 0; i < colours.Length; i++) State.Actors[uint.MaxValue - 10u - (uint)i] = new Actor { Id = uint.MaxValue - 10u - (uint)i,
                Kind = ObjectType.Monster, Image = image, Location = new Point(State.User.Location.X - 4 + i, State.User.Location.Y - 2),
                Poison = colours[i], Name = i == 0 ? "绿毒染色检查" : "红绿毒优先级检查" };
            await Until(() => Enumerable.Range(0, colours.Length).All(i => Client.World.ActorBody(State.Actors[uint.MaxValue - 10u - (uint)i], Environment.TickCount64) != null));
            await Shot("poison-colours");
        }
        finally { for (var i = 0; i < colours.Length; i++) State.Actors.Remove(uint.MaxValue - 10u - (uint)i); }
    }

    private async Task VerifySpellGallery(Actor target)
    {
        stage = "拒绝施法不产生命中/切图清理";
        await Client.World.Load(State.Map!.FileName); await Until(() => !Client.World.Loading);
        Client.World.ShowSpell(Spell.FireBall, State.User!.ObjectID, target.Id, target.Location, false);
        await Pause(2200);
        if (Client.World.EffectCount != 0) throw new Exception("失败火球仍发射或残留特效。");
        Client.World.ShowSpell(Spell.FireBall, State.User.ObjectID, target.Id, target.Location);
        if (Client.World.EffectCount == 0) throw new Exception("成功施法没有建立特效。");
        await Client.World.Load(State.Map.FileName);
        if (Client.World.EffectCount != 0) throw new Exception("切图后残留上一地图特效。");
    }
}
