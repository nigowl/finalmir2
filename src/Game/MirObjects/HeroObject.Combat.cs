using Server.MirDatabase;
using System.Drawing;

namespace Server.MirObjects;

public partial class HeroObject
{
    protected bool PhysicalOnlyTarget => Target is MonsterObject { Info.AI: 3 };

    protected bool HasMagicMaterials(Spell spell) => spell switch
    {
        Spell.Poisoning or Spell.PoisonSword => GetPoison(1) != null,
        Spell.SummonShinsu => GetAmulet(5) != null,
        Spell.SummonHolyDeva => GetAmulet(2) != null,
        Spell.PoisonCloud => GetAmulet(5) != null && GetPoison(5, 1) != null,
        Spell.SoulFireBall or Spell.SummonSkeleton or Spell.SoulShield or Spell.BlessedArmour or Spell.UltimateEnhancer
            or Spell.Curse or Spell.Revelation => GetAmulet(1) != null,
        _ => true
    };

    protected bool MagicInRange(UserMagic magic, MapObject target)
    {
        if (target == null || target.Dead || target.CurrentMap != CurrentMap || target.Node == null) return false;
        var distance = Functions.MaxDistance(CurrentLocation, target.CurrentLocation);
        if (distance > ViewRange || magic.Info.Range != 0 && distance > magic.Info.Range) return false;
        var range = magic.Spell switch
        {
            Spell.FlameField or Spell.ThunderStorm => 2,
            Spell.PoisonSword or Spell.CrescentSlash => 1,
            Spell.HeavenlySword or Spell.BladeAvalanche or Spell.IceThrust => 3,
            Spell.HellFire => 4,
            Spell.Lightning => 5,
            _ => ViewRange
        };
        if (distance > range) return false;
        if (magic.Spell == Spell.HeavenlySword && target.CurrentLocation != Functions.PointMove(CurrentLocation,
                Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation), distance)) return false;
        return target == this || target.IsFriendlyTarget(this) || CanFly(target.CurrentLocation);
    }

    protected bool TryHeroMagic(Spell spell, MapObject target = null)
    {
        target ??= this;
        var magic = GetMagic(spell);
        if (!CanCast || !CanUseMagic(magic) || !HasMagicMaterials(spell) || !MagicInRange(magic, target)) return false;
        if (target != this && !target.IsFriendlyTarget(this) && (!target.IsAttackTarget(this) || !target.IsAttackTarget(Owner) ||
            target is MonsterObject { Info.AI: 3 })) return false;
        BeginMagic(spell, Functions.DirectionFromPoint(CurrentLocation, target.CurrentLocation), target.ObjectID, target.CurrentLocation);
        return true;
    }

    protected bool TryHeroBuff(Spell spell, BuffType buff, MapObject target = null)
    {
        target ??= this;
        return !target.HasBuff(buff) && TryHeroMagic(spell, target);
    }

    protected bool HasRangedMagic(params Spell[] spells) => !PhysicalOnlyTarget && Target != null && spells.Any(spell =>
        GetMagic(spell) is { Info: not null } magic && MagicCost(magic) <= MP && HasMagicMaterials(spell) && MagicInRange(magic, Target));

    protected bool ProcessQueuedMagic()
    {
        if (NextMagicSpell == Spell.None) return false;
        if (!CanCast) return true;
        var spell = NextMagicSpell;
        NextMagicSpell = Spell.None;
        Magic(spell, NextMagicDirection, NextMagicTargetID, NextMagicLocation);
        return true;
    }

    protected void ProcessRangedTarget(bool rangedMagic, bool bow = false)
    {
        // 辅助技能不依赖敌人或普攻计时；施法当帧不能再发普通射击。
        if (ProcessQueuedMagic() || Target == null || !CanAttack) return;
        TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
        if ((rangedMagic || bow) && TargetDistance <= ViewRange && CanFly(Target.CurrentLocation))
        {
            if (bow) RangeAttack(Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation), Target.CurrentLocation, Target.ObjectID);
            return;
        }
        if (Owner.Info.HeroBehaviour == HeroBehaviour.CounterAttack)
        {
            FollowOwner();
            return;
        }
        if (TargetDistance == 1) Attack();
        else if (CanMove) MoveTo(Target.CurrentLocation);
    }

    protected List<MapObject> CombatTargets(int distance, Point centre) => FindAllTargets(distance, centre)
        .Where(target => !target.Dead && target.IsAttackTarget(Owner) && target is not MonsterObject { Info.AI: 3 }).ToList();

    protected int ForwardTargets(int length, int halfWidth = 0)
    {
        var direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
        return CombatTargets(length + halfWidth, CurrentLocation).Count(target => Enumerable.Range(1, length).Any(step =>
            Functions.InRange(Functions.PointMove(CurrentLocation, direction, step), target.CurrentLocation, halfWidth)));
    }
}
