using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.MirObjects
{
    public class WarriorHero : HeroObject
    {
        public WarriorHero(CharacterInfo info, PlayerObject owner) : base(info, owner) { }
        protected override bool InAttackRange()
        {
            if (Target.CurrentMap != CurrentMap) return false;
            var distance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            return distance == 1 || !PhysicalOnlyTarget && HasMagic(Spell.Thrusting) && distance == 2 &&
                Target.CurrentLocation == Functions.PointMove(CurrentLocation, Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation), 2);
        }
        protected override void ProcessFriend()
        {
            if (!CanCast || Target == null) return;
            if ((PercentHealth <= 50 || CombatTargets(1, CurrentLocation).Count >= 3) && TryHeroBuff(Spell.ImmortalSkin, BuffType.ImmortalSkin)) return;
            if (TryHeroBuff(Spell.ProtectionField, BuffType.ProtectionField)) return;
            if (TryHeroBuff(Spell.Rage, BuffType.Rage)) return;
            if (TryHeroBuff(Spell.Fury, BuffType.Fury)) return;
            var counter = GetMagic(Spell.CounterAttack);
            if (Functions.InRange(CurrentLocation, Target.CurrentLocation, 1) && !CounterAttack && Envir.Time >= CounterAttackTime &&
                CanUseMagic(counter) && counter.Info.BaseCost + counter.Level * counter.Info.LevelCost < MP)
                SpellToggle(Spell.CounterAttack, SpellToggleState.True);
        }
        protected override void ProcessAttack()
        {
            if (Target == null || Target.Dead) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            if (!CanAttack || PhysicalOnlyTarget || FlamingSword || TwinDrakeBlade) return;
            var flame = GetMagic(Spell.FlamingSword);
            if (TargetDistance == 1 && CanUseMagic(flame) && Envir.Time >= FlamingSwordTime && flame.Info.BaseCost + flame.Level * flame.Info.LevelCost < MP)
            {
                SpellToggle(Spell.FlamingSword, SpellToggleState.True);
                return;
            }
            var twin = GetMagic(Spell.TwinDrakeBlade);
            if (TargetDistance == 1 && CanUseMagic(twin) && 2L * (twin.Info.BaseCost + twin.Level * twin.Info.LevelCost) <= MP && MP > 0)
            {
                SpellToggle(Spell.TwinDrakeBlade, SpellToggleState.True);
                return;
            }
            if (TargetDistance <= 3 && ForwardTargets(3, 1) >= 2) TryHeroMagic(Spell.BladeAvalanche, Target);
        }
        protected override void Attack()
        {
            if (Target == null || !Target.IsAttackTarget(Owner) || !CanAttack) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            var spell = Spell.None;
            if (!PhysicalOnlyTarget)
            {
                if (TargetDistance == 2 && HasMagic(Spell.Thrusting)) spell = Spell.Thrusting;
                else if (TargetDistance == 1)
                {
                    if (FlamingSword) spell = Spell.FlamingSword;
                    else if (TwinDrakeBlade) spell = Spell.TwinDrakeBlade;
                    else
                    {
                        var targets = CombatTargets(1, CurrentLocation);
                        var cross = GetMagic(Spell.CrossHalfMoon);
                        var half = GetMagic(Spell.HalfMoon);
                        if (targets.Count >= 2 && cross != null && cross.Info.BaseCost + cross.Level * cross.Info.LevelCost <= MP) spell = Spell.CrossHalfMoon;
                        else if (half != null && half.Info.BaseCost + half.Level * half.Info.LevelCost <= MP && targets.Count(target =>
                            Enumerable.Range(0, 4).Any(step => target.CurrentLocation == Functions.PointMove(CurrentLocation,
                                (MirDirection)(((int)Direction + step + 7) % 8), 1))) >= 2) spell = Spell.HalfMoon;
                        else if (Slaying) spell = Spell.Slaying;
                    }
                }
            }
            Attack(Direction, spell);
        }
    }
}
