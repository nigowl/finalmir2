using System.Drawing;
using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public readonly Dictionary<BuffType, ClientBuff> UserBuffs = new();
    public void StartAttack(Actor actor, MirDirection direction, Spell spell, byte level, byte type = 0)
    {
        actor.Move(actor.Location, direction, actor.Kind == ObjectType.Player ? MirAction.Attack1 : type switch { 1 => MirAction.Attack2, 2 => MirAction.Attack3, 3 => MirAction.Attack4, 4 => MirAction.Attack5, _ => MirAction.Attack1 });
        actor.ActionSpell = spell; actor.SpellLevel = level; actor.Casting = false;
    }

    private void StartMagic(Actor actor, Point location, MirDirection direction, Spell spell, byte level)
    {
        actor.Move(location, direction, ClientSkillRules.Animation(spell));
        actor.ActionSpell = spell; actor.SpellLevel = level; actor.Casting = true;
    }

    private void MarkCast(Spell spell)
    {
        var magic = User?.Magics.FirstOrDefault(m => m.Spell == spell);
        if (magic != null) magic.CastTime = Environment.TickCount64;
    }

    private void SkillMove(uint id, Point point, MirDirection direction, MirAction action)
    {
        if (Actors.TryGetValue(id, out var actor)) actor.Move(point, direction, action, isSkillMovement: true);
        if (id == User?.ObjectID) { User.Location = point; User.Direction = direction; }
    }

    private void ApplySpellState(Packet packet)
    {
        switch (packet)
        {
            case S.UserInformation: UserBuffs.Clear(); break;
            case S.LogOutSuccess or S.ReturnToLogin or S.Disconnect: UserBuffs.Clear(); break;
            case S.Death:
                UserBuffs.Clear();
                if (Me != null) { Me.MagicShield = Me.ElementalBarrier = Me.Concentrating = false; Me.Buffs.Clear(); Me.Elements.Clear(); }
                break;
            case S.ObjectDied p when Actors.TryGetValue(p.ObjectID, out var actor): actor.MagicShield = actor.ElementalBarrier = actor.Concentrating = false; actor.Buffs.Clear(); actor.Elements.Clear(); break;
            case S.ObjectAttack p when p.ObjectID != User?.ObjectID && Actors.TryGetValue(p.ObjectID, out var actor):
                if (actor.IsShinsu) actor.Image = (int)Monster.Shinsu1;
                actor.Location = p.Location; StartAttack(actor, p.Direction, p.Spell, p.Level, p.Type); break;
            case S.ObjectMagic p when (p.ObjectID != User?.ObjectID || p.SelfBroadcast || p.Spell == Spell.TwinDrakeBlade) && Actors.TryGetValue(p.ObjectID, out var actor):
                StartMagic(actor, p.Location, p.Direction, p.Spell, p.Level);
                if (p.ObjectID == User?.ObjectID && p.Spell == Spell.TwinDrakeBlade) ActiveSkills.Add(p.Spell);
                break;
            case S.Magic p when Me != null:
                StartMagic(Me, Me.Location, Me.Direction, p.Spell, p.Level);
                if (p.Cast) MarkCast(p.Spell);
                break;
            case S.MagicCast p: MarkCast(p.Spell); break;
            case S.ObjectHarvest p when Actors.TryGetValue(p.ObjectID, out var actor) && !actor.Dead:
                actor.Move(p.Location, p.Direction, MirAction.Harvest); break;
            case S.Struck when Me is { Dead: false } me: ShowFlinch(me); break;
            case S.ObjectStruck p when p.ObjectID != User?.ObjectID && Actors.TryGetValue(p.ObjectID, out var actor) && !actor.Dead:
                ShowFlinch(actor); break;
            case S.RangeAttack p when Me != null:
                Me.Move(Me.Location, Me.Direction, MirAction.AttackRange1); Me.ActionSpell = p.Spell; Me.Casting = true; break;
            case S.ObjectRangeAttack p when p.ObjectID != User?.ObjectID && Actors.TryGetValue(p.ObjectID, out var actor):
                actor.Move(p.Location, p.Direction, actor.Kind == ObjectType.Player ? MirAction.AttackRange1 : p.Type switch { 1 => MirAction.AttackRange2, 2 => MirAction.AttackRange3, _ => MirAction.AttackRange1 });
                actor.ActionSpell = p.Spell; actor.SpellLevel = p.Level; actor.Casting = true; break;
            case S.UserDash p when User != null: SkillMove(User.ObjectID, p.Location, p.Direction, MirAction.Running); break;
            case S.ObjectDash p: SkillMove(p.ObjectID, p.Location, p.Direction, MirAction.Running); break;
            case S.UserBackStep p when User != null: SkillMove(User.ObjectID, p.Location, p.Direction, MirAction.Jump); break;
            case S.ObjectBackStep p: SkillMove(p.ObjectID, p.Location, p.Direction, MirAction.Jump); break;
            case S.UserDashAttack p when User != null: SkillMove(User.ObjectID, p.Location, p.Direction, MirAction.DashAttack); break;
            case S.ObjectDashAttack p: SkillMove(p.ObjectID, p.Location, p.Direction, MirAction.DashAttack); break;
            case S.UserAttackMove p when User != null: SkillMove(User.ObjectID, p.Location, p.Direction, MirAction.Running); break;
            case S.ObjectHidden p when Actors.TryGetValue(p.ObjectID, out var actor): actor.Hidden = p.Hidden; break;
            case S.ObjectSpell p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Kind = ObjectType.Spell, Location = p.Location, Previous = p.Location,
                    Direction = p.Direction, ActionSpell = p.Spell, SpellParam = p.Param, ActionTime = Environment.TickCount64 };
                break;
            case S.ObjectPlayer p when Actors.TryGetValue(p.ObjectID, out var actor):
                actor.MagicShield = p.Effect == SpellEffect.MagicShieldUp;
                actor.ElementalBarrier = p.Effect == SpellEffect.ElementalBarrierUp;
                actor.Buffs.UnionWith(p.Buffs); actor.ElementLevel = p.ElementOrbLvl;
                if (p.ElementOrbEffect == 4) actor.Elements[4] = p.ElementOrbMax;
                else for (uint type = 1; type <= Math.Min(p.ElementOrbEffect, 3); type++) actor.Elements[type] = p.ElementOrbMax;
                break;
            case S.SetConcentration p when Actors.TryGetValue(p.ObjectID, out var actor):
                actor.Concentrating = p.Enabled; actor.ConcentrationInterrupted = p.Interrupted; break;
            case S.SetElemental p when Actors.TryGetValue(p.ObjectID, out var actor):
                actor.ElementLevel = p.Value;
                foreach (var type in actor.Elements.Where(pair => !p.Enabled || pair.Key < 4 && p.Value >= pair.Value).Select(pair => pair.Key).ToArray()) actor.Elements.Remove(type);
                if (p.Enabled && p.ElementType is >= 1 and <= 4)
                { if (p.ElementType == 4) actor.Elements.Clear(); actor.Elements[p.ElementType] = p.ExpLast; }
                break;
            case S.ObjectEffect p when Actors.TryGetValue(p.ObjectID, out var actor):
                if (p.Effect is SpellEffect.MagicShieldUp or SpellEffect.MagicShieldDown) actor.MagicShield = p.Effect == SpellEffect.MagicShieldUp;
                if (p.Effect is SpellEffect.ElementalBarrierUp or SpellEffect.ElementalBarrierDown) actor.ElementalBarrier = p.Effect == SpellEffect.ElementalBarrierUp;
                break;
            case S.AddBuff p:
                if (p.Buff.ObjectID == Hero?.ObjectID) HeroBuffs[p.Buff.Type] = p.Buff;
                if (Actors.TryGetValue(p.Buff.ObjectID, out var buffActor)) buffActor.Buffs.Add(p.Buff.Type);
                if (p.Buff.ObjectID == User?.ObjectID)
                {
                    UserBuffs[p.Buff.Type] = p.Buff;
                    if (p.Buff.Type == BuffType.CounterAttack) MarkCast(Spell.CounterAttack);
                }
                break;
            case S.RemoveBuff p:
                if (p.ObjectID == Hero?.ObjectID) HeroBuffs.Remove(p.Type);
                if (Actors.TryGetValue(p.ObjectID, out var removedActor)) removedActor.Buffs.Remove(p.Type);
                if (p.ObjectID == User?.ObjectID) UserBuffs.Remove(p.Type);
                break;
            case S.PauseBuff p when p.ObjectID == User?.ObjectID && UserBuffs.TryGetValue(p.Type, out var buff): buff.Paused = p.Paused; break;
        }
    }

    private static void ShowFlinch(Actor actor)
    {
        var now = Environment.TickCount64;
        // 受击反馈不能重置仍在进行的位移、施法、攻击或割肉。
        var duration = actor.Action is MirAction.Walking or MirAction.Running ? actor.MovementDuration(Actor.MoveDuration) :
            actor.Kind == ObjectType.Player ? actor.PlayerTiming.Scale(1000) :
            actor.IsShinsu && actor.Action is (MirAction.Appear or MirAction.Show or MirAction.Hide) ? 1000 : 650;
        if (actor.Action is MirAction.Standing or MirAction.Stance || now - actor.ActionTime >= duration)
            actor.Move(actor.Location, actor.Direction, MirAction.Struck, now);
    }
}
