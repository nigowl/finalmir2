using System.Reflection;
using System.Diagnostics;
using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSpellSmoke
{
    private async Task VerifyComboFlow(MirClass job, Actor target)
    {
        stage = $"{job}技能与平砍衔接采样";
        Battle(false); Client.SetDestination(State.User!.Location);
        var point = Client.World.Approach(target, 1) ?? throw new Exception("连招检查目标附近无可达格。");
        Client.SetDestination(point, true); await Until(() => State.User.Location == point); await Pause(650);
        var primary = job switch { MirClass.Warrior => Spell.BladeAvalanche, MirClass.Wizard => Spell.FireBall,
            MirClass.Taoist => Spell.SoulFireBall, MirClass.Assassin => Spell.PoisonSword, _ => Spell.DoubleShot };
        var disabled = State.DisabledSkills.ToArray();
        try
        {
            State.DisabledSkills.UnionWith(State.User.Magics.Where(magic => !ClientSkillRules.IsPassive(magic.Spell) && magic.Spell != primary).Select(magic => magic.Spell));
            await Until(() => Client.World.PlayerAnimationReady(State.Me!.Direction, MirAction.Attack1) &&
                Client.World.PlayerAnimationReady(State.Me.Direction, ClientSkillRules.Animation(primary)));
            await Until(() => ClassSkillRules.Ready(State, State.User.Magics[Skill(primary)], Environment.TickCount64) &&
                Environment.TickCount64 >= (long)typeof(Main).GetField("spellTime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!);
            packets.Clear(); Client.World.Selected = target.Id; Client.Cast(Skill(primary));
            await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell == primary));
            var castAt = State.Me!.ActionTime; var castDuration = Client.World.ActionDuration(State.Me);
            Client.SelectTarget(target.Id);
            var lastAction = castAt; var firstAttack = 0L; var attacks = new List<long>();
            var maxFrameGap = 0L; var lastFrame = Environment.TickCount64; var timer = Stopwatch.StartNew(); var missingFrames = 0;
            while (timer.Elapsed.TotalSeconds < 4)
            {
                var now = Environment.TickCount64; maxFrameGap = Math.Max(maxFrameGap, now - lastFrame); lastFrame = now;
                var me = State.Me;
                if (Client.World.ActorBody(me, now) == null) missingFrames++;
                if (me.ActionTime != lastAction)
                {
                    lastAction = me.ActionTime;
                    if (!me.Casting && me.Action is MirAction.Attack1 or MirAction.Attack4 || me.Action == MirAction.AttackRange1 && me.ActionSpell == Spell.None)
                    { if (firstAttack == 0) firstAttack = me.ActionTime; attacks.Add(me.ActionTime); }
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Client.SetDestination(State.User.Location);
            var allowed = Math.Max(castDuration, State.PlayerTiming.MoveMilliseconds) + 220;
            if (firstAttack == 0 || firstAttack - castAt > allowed || attacks.Count < 2 || missingFrames > 0 || maxFrameGap > 200 ||
                !packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0))
                throw new Exception($"{job}连招衔接异常：施法动画{castDuration}ms，接平砍{firstAttack - castAt}ms，攻击{attacks.Count}次，缺帧{missingFrames}，最大帧间隔{maxFrameGap}ms。");
            GD.Print($"PASS native {job} combo flow: cast animation {castDuration}ms, first basic attack {firstAttack - castAt}ms, {attacks.Count} basics/4s, no missing body frames, max render gap {maxFrameGap}ms.");
        }
        finally { State.DisabledSkills.Clear(); State.DisabledSkills.UnionWith(disabled); Client.SetDestination(State.User.Location); }
        await VerifyLowHealthCombo(job, target);
        await VerifyEmptyManaCombo(job, target);
    }

    private async Task VerifyLowHealthCombo(MirClass job, Actor target)
    {
        stage = $"{job}低血技能选择";
        var injury = State.User!.Inventory.Single(item => item?.Info.Name == "隔离自疗测试药");
        packets.Clear(); Client.UseItem(injury.UniqueID);
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == injury.UniqueID && packet.Success));
        var hp = State.User.HP; var mp = State.User.MP;
        Client.SelectTarget(target.Id);
        var expected = job switch { MirClass.Warrior => Spell.ImmortalSkin, MirClass.Wizard => Spell.Vampirism, MirClass.Taoist => Spell.Healing,
            MirClass.Archer => Spell.VampireShot, _ => Spell.None };
        if (expected != Spell.None)
        {
            await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell == expected));
            if (job == MirClass.Warrior) await Until(() => State.UserBuffs.ContainsKey(BuffType.ImmortalSkin));
            else await Until(() => State.User.HP > hp + 30 && State.User.MP < mp);
        }
        else await Until(() => packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0));
        Client.SetDestination(State.User.Location);
        GD.Print($"PASS native {job} low health: {expected}, original cast/buff or damage and actual HP/MP.");
    }

    private async Task VerifyEmptyManaCombo(MirClass job, Actor target)
    {
        stage = $"{job}缺蓝回退平砍";
        var drain = State.User!.Inventory.Single(item => item?.Info.Name == "隔离缺蓝测试药");
        packets.Clear(); Client.UseItem(drain.UniqueID);
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == drain.UniqueID && packet.Success));
        if (State.User.MP != 0) throw new Exception("隔离缺蓝药没有实际扣空MP。");
        packets.Clear(); Client.SelectTarget(target.Id);
        await Until(() => State.Me!.Action is MirAction.Attack1 or MirAction.AttackRange1 &&
            packets.OfType<S.DamageIndicator>().Any(packet => packet.Damage < 0));
        Client.SetDestination(State.User.Location);
        if (packets.OfType<S.Magic>().Any(packet => packet.Cast && packet.Spell != Spell.None)) throw new Exception("缺蓝期间没有回退平砍。");
        GD.Print($"PASS native {job} empty mana: real zero MP, basic attack and actual target damage.");
    }
}
