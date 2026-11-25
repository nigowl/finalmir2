using Server.MirDatabase;

namespace Server.MirObjects
{
    public class WizardHero : HeroObject
    {
        public WizardHero(CharacterInfo info, PlayerObject owner) : base(info, owner) { }
        public int SurroundedCount;
        public int TargetSurroundedCount;
        protected override bool InAttackRange()
        {
            return Target.CurrentMap == CurrentMap && (HasRangedSpell || Target.CurrentLocation != CurrentLocation && Functions.InRange(CurrentLocation, Target.CurrentLocation, 1));
        }
        protected override void ProcessFriend()
        {
            if (!CanCast || Target == null) return;
            if (TryHeroBuff(Spell.MagicShield, BuffType.MagicShield)) return;
            if (TryHeroBuff(Spell.MagicBooster, BuffType.MagicBooster)) return;
            if (!CurrentMap.Info.NoPets && Envir.GetMonsterInfo(Server.Settings.CloneName) != null &&
                !Pets.Any(pet => !pet.Dead && pet.Info.Name == Server.Settings.CloneName)) TryHeroMagic(Spell.Mirroring);
        }

        protected override void ProcessAttack()
        {
            if (!CanCast || Target == null || Target.Dead || PhysicalOnlyTarget) return;
            TargetDistance = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);
            SurroundedCount = CombatTargets(2, CurrentLocation).Count;
            TargetSurroundedCount = CombatTargets(1, Target.CurrentLocation).Count;
            if (PercentHealth <= 50 && TryHeroMagic(Spell.Vampirism, Target)) return;
            if (TargetSurroundedCount >= 3 && !CurrentMap.Spells.Any(spell => spell.Node != null && spell.ExpireTime > Envir.Time &&
                spell.Spell == Spell.FireWall && spell.Caster == this && Functions.InRange(spell.CurrentLocation, Target.CurrentLocation, 1)) && TryHeroMagic(Spell.FireWall, Target)) return;
            if (TargetDistance <= 2 && SurroundedCount >= 2)
                foreach (var spell in new[] { Spell.FlameField, Spell.ThunderStorm })
                    if (TryHeroMagic(spell, Target)) return;
            if (TargetSurroundedCount >= 2)
                foreach (var spell in new[] { Spell.MeteorStrike, Spell.Blizzard, Spell.IceStorm, Spell.FireBang })
                    if (TryHeroMagic(spell, Target)) return;
            if (ForwardTargets(3, 1) >= 2 && TryHeroMagic(Spell.IceThrust, Target)) return;
            if (ForwardTargets(5) >= 2 && TryHeroMagic(Spell.Lightning, Target)) return;
            if (ForwardTargets(4) >= 2 && TryHeroMagic(Spell.HellFire, Target)) return;
            foreach (var spell in new[] { Spell.FlameDisruptor, Spell.ThunderBolt, Spell.GreatFireBall, Spell.FireBall, Spell.FrostCrunch, Spell.Vampirism })
                if (TryHeroMagic(spell, Target)) return;
        }

        protected override void ProcessTarget()
        {
            ProcessRangedTarget(HasRangedSpell);
        }

        private bool HasRangedSpell => HasRangedMagic(Spell.FlameDisruptor, Spell.ThunderBolt, Spell.GreatFireBall, Spell.FireBall, Spell.FrostCrunch, Spell.Vampirism) ||
            Target != null && CombatTargets(1, Target.CurrentLocation).Count >= 2 && HasRangedMagic(Spell.MeteorStrike, Spell.Blizzard, Spell.IceStorm, Spell.FireBang) ||
            Target != null && CombatTargets(1, Target.CurrentLocation).Count >= 3 && HasRangedMagic(Spell.FireWall);
    }
}
